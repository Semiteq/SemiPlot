"""Per-frame cost and cadence of a drag, read from a dotnet-trace Speedscope export.

Collect while dragging the chart, then read the export:

    dotnet-trace collect -p (Get-Process SemiPlot.UI).Id --format Speedscope `
        --duration 00:00:45 -o drag.speedscope.json
    python scripts/perf/trace-shares.py drag.speedscope.speedscope.json --skip-seconds 10

dotnet-trace writes the converted file next to the requested output and names it
`<name>.speedscope.json`, so the argument above is not a typo.

Frames are matched by substring, so `Polygon.Render` matches
`ScottPlot!ScottPlot.Plottables.Polygon.Render(class ScottPlot.RenderPack)`. A frame that recurses
is timed on its outermost call only.

See docs/architecture/testing-strategy.md#frame-cost.
"""

import argparse
import bisect
import json
import statistics
import sys
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

FRAMES = (
    "RenderOnce",
    "Polygon.Render",
    "SKCanvas.DrawPath",
    "OnNavigationWindowChanged",
    "ApplyHistory",
    "QueryHistoryAsync",
)

RENDER_ONCE = "RenderOnce"
COMPOSITOR_FRAME = "ServerCompositor.RenderCore"
GENERATE_TICKS = "NumericAutomatic.GenerateTicks"
MEASURE_LABEL = "LabelStyle.Measure"
CHART_POINTER_MOVED = "TrendChartView.OnPointerMoved"
CHART_WINDOW_CHANGED = "TrendChartViewModel.OnNavigationWindowChanged"
LOCK_CONTENTION = "Monitor.Enter_Slowpath"

TRACKED = (
    *FRAMES,
    COMPOSITOR_FRAME,
    GENERATE_TICKS,
    MEASURE_LABEL,
    CHART_POINTER_MOVED,
    CHART_WINDOW_CHANGED,
    LOCK_CONTENTION,
)

UI_THREAD_MARKER = "Win32DispatcherImpl.RunLoop"
RENDER_THREAD_MARKER = "WinUiCompositorConnection.RunLoop"

MILLISECONDS_PER_UNIT = {
    "nanoseconds": 1e-6,
    "microseconds": 1e-3,
    "milliseconds": 1.0,
    "seconds": 1000.0,
}


@dataclass
class Run:
    """One outermost span of a tracked frame on one thread."""

    start: float
    end: float
    parent: str
    enclosing: frozenset[str]
    nested: set[str] = field(default_factory=set)

    @property
    def duration(self) -> float:
        return self.end - self.start


@dataclass
class Trace:
    """Runs per tracked frame, pooled and per thread, past the skipped head of an export."""

    runs: dict[str, list[Run]]
    ui_runs: dict[str, list[Run]]
    render_runs: dict[str, list[Run]]
    span: float


def method_of(frame_name: str) -> str:
    """The qualified method of a frame name, without its module and parameter list."""
    return frame_name.split("!", 1)[-1].split("(", 1)[0]


def labels_of(frame_name: str) -> tuple[str, ...]:
    return tuple(label for label in TRACKED if label in frame_name)


def read_profile(
    profile: dict[str, Any],
    frame_names: list[str],
    frame_labels: list[tuple[str, ...]],
    scale: float,
) -> dict[str, list[Run]]:
    """Outermost runs per tracked frame, from one evented profile's open and close events."""
    runs: dict[str, list[Run]] = defaultdict(list)
    open_runs: dict[str, Run] = {}
    depth: dict[str, int] = defaultdict(int)
    stack: list[int] = []

    for event in profile["events"]:
        at = event["at"] * scale
        frame = event["frame"]
        if event["type"] == "O":
            for label in frame_labels[frame]:
                if depth[label] == 0:
                    for enclosing in open_runs.values():
                        enclosing.nested.add(label)
                    parent = method_of(frame_names[stack[-1]]) if stack else ""
                    open_runs[label] = Run(at, at, parent, frozenset(open_runs))
                depth[label] += 1
            stack.append(frame)
            continue

        if not stack:
            raise ValueError(
                "The export closes a frame that was never opened, so it is truncated and every "
                "total below it would be wrong."
            )

        closed = stack.pop()
        for label in frame_labels[closed]:
            depth[label] -= 1
            if depth[label] == 0:
                run = open_runs.pop(label)
                run.end = at
                runs[label].append(run)

    return runs


def holds_frame(profile: dict[str, Any], frame_names: list[str], marker: str) -> bool:
    return any(marker in frame_names[event["frame"]] for event in profile["events"])


def read_trace(path: Path, skip_seconds: float) -> Trace:
    """Runs per tracked frame over an export's evented profiles, past the first skip_seconds."""
    with path.open(encoding="utf-8") as export:
        document = json.load(export)

    frame_names = [frame["name"] for frame in document["shared"]["frames"]]
    frame_labels = [labels_of(name) for name in frame_names]
    profiles = [profile for profile in document["profiles"] if profile["type"] == "evented"]
    if not profiles:
        raise ValueError("The export holds no evented profile.")

    scales = []
    for profile in profiles:
        unit = profile.get("unit", "milliseconds")
        if unit not in MILLISECONDS_PER_UNIT:
            raise ValueError(f"Unsupported profile unit: {unit}")
        scales.append(MILLISECONDS_PER_UNIT[unit])

    trace_start = min(p["startValue"] * s for p, s in zip(profiles, scales))
    trace_end = max(p["endValue"] * s for p, s in zip(profiles, scales))
    cutoff = trace_start + skip_seconds * 1000

    pooled: dict[str, list[Run]] = defaultdict(list)
    ui_runs: dict[str, list[Run]] | None = None
    render_runs: dict[str, list[Run]] | None = None
    for profile, scale in zip(profiles, scales):
        runs = read_profile(profile, frame_names, frame_labels, scale)
        kept = {
            label: [run for run in found if run.start >= cutoff] for label, found in runs.items()
        }
        for label, found in kept.items():
            pooled[label].extend(found)
        if ui_runs is None and holds_frame(profile, frame_names, UI_THREAD_MARKER):
            ui_runs = kept
        elif render_runs is None and holds_frame(profile, frame_names, RENDER_THREAD_MARKER):
            render_runs = kept

    if ui_runs is None:
        raise ValueError(f"No profile holds {UI_THREAD_MARKER}, so the UI thread is unknown.")
    if render_runs is None:
        raise ValueError(
            f"No profile holds {RENDER_THREAD_MARKER}, so the render thread is unknown."
        )

    for found in pooled.values():
        found.sort(key=lambda run: run.start)
    return Trace(pooled, ui_runs, render_runs, max(0.0, trace_end - cutoff))


def p90(values: list[float]) -> float:
    if len(values) == 1:
        return values[0]
    return statistics.quantiles(values, n=10, method="inclusive")[8]


def spread(values: list[float]) -> str:
    if not values:
        return f"{'-':>8}{'-':>8}{'-':>8}{0:>6}"
    return (
        f"{statistics.median(values):>8.1f}{p90(values):>8.1f}"
        f"{max(values):>8.1f}{len(values):>6}"
    )


def starts_of(runs: list[Run]) -> list[float]:
    return sorted(run.start for run in runs)


def spacings(starts: list[float]) -> list[float]:
    return [later - earlier for earlier, later in zip(starts, starts[1:])]


def drag_spacings(frame_starts: list[float], pointer_starts: list[float]) -> list[float]:
    """Spacings between consecutive frames whose gap holds the start of a chart pointer move."""
    held = []
    for earlier, later in zip(frame_starts, frame_starts[1:]):
        index = bisect.bisect_left(pointer_starts, earlier)
        if index < len(pointer_starts) and pointer_starts[index] < later:
            held.append(later - earlier)
    return held


def delays_to_next(marks: list[float], frame_starts: list[float]) -> list[float]:
    delays = []
    for mark in marks:
        index = bisect.bisect_left(frame_starts, mark)
        if index < len(frame_starts):
            delays.append(frame_starts[index] - mark)
    return delays


def report_frames(trace: Trace) -> None:
    print(f"{'frame':<28}{'runs':>8}{'total ms':>12}{'mean ms':>10}{'max ms':>10}")
    for frame in FRAMES:
        measured = [run.duration for run in trace.runs.get(frame, [])]
        if not measured:
            print(f"{frame:<28}{0:>8}{'-':>12}{'-':>10}{'-':>10}")
            continue

        total = sum(measured)
        mean = total / len(measured)
        print(f"{frame:<28}{len(measured):>8}{total:>12.1f}{mean:>10.1f}{max(measured):>10.1f}")


def report_cadence(trace: Trace) -> None:
    frame_starts = starts_of(trace.render_runs.get(RENDER_ONCE, []))
    pointer_starts = starts_of(trace.ui_runs.get(CHART_POINTER_MOVED, []))
    compositor_spacings = spacings(starts_of(trace.render_runs.get(COMPOSITOR_FRAME, [])))
    pan_steps = [
        run.start
        for run in trace.ui_runs.get(CHART_POINTER_MOVED, [])
        if CHART_WINDOW_CHANGED in run.nested
    ]

    print()
    rows = (
        ("RenderOnce spacing, all", spacings(frame_starts)),
        ("RenderOnce spacing, drag phase", drag_spacings(frame_starts, pointer_starts)),
        ("compositor frame interval", compositor_spacings),
        ("pan step to next RenderOnce", delays_to_next(sorted(pan_steps), frame_starts)),
    )
    print(f"{'cadence ms':<40}{'p50':>8}{'p90':>8}{'max':>8}{'n':>6}")
    for name, values in rows:
        print(f"{name:<40}{spread(values)}")
    if compositor_spacings:
        rate = 1000 / statistics.median(compositor_spacings)
        print(f"compositor rate {rate:.1f} frames/s at the median interval")


def report_ticks(trace: Trace) -> None:
    frames = len(trace.render_runs.get(RENDER_ONCE, []))
    print()
    if frames == 0:
        print("no RenderOnce run, so nothing is attributed per frame")
        return

    ticks = trace.render_runs.get(GENERATE_TICKS, [])
    tick_total = sum(run.duration for run in ticks)
    print(
        f"{GENERATE_TICKS}: {len(ticks)} runs, {tick_total:.1f} ms, "
        f"{len(ticks) / frames:.2f} runs and {tick_total / frames:.3f} ms per RenderOnce"
    )

    by_parent: dict[str, list[Run]] = defaultdict(list)
    for run in trace.render_runs.get(MEASURE_LABEL, []):
        by_parent[run.parent].append(run)
    measure_total = sum(run.duration for found in by_parent.values() for run in found)
    print(
        f"{MEASURE_LABEL}: {measure_total:.1f} ms, "
        f"{measure_total / frames:.3f} ms per RenderOnce, by caller"
    )
    ordered = sorted(by_parent.items(), key=lambda item: -sum(run.duration for run in item[1]))
    for parent, found in ordered:
        total = sum(run.duration for run in found)
        print(f"  {len(found):>6} runs {total:>8.1f} ms {total / frames:>7.3f} ms/frame  {parent}")


def report_contention(trace: Trace) -> None:
    stalls = [
        run.duration
        for run in trace.ui_runs.get(LOCK_CONTENTION, [])
        if CHART_WINDOW_CHANGED in run.enclosing
    ]
    print()
    if not stalls:
        print(f"{LOCK_CONTENTION} under {CHART_WINDOW_CHANGED}: none")
        return
    print(
        f"{LOCK_CONTENTION} under {CHART_WINDOW_CHANGED}: "
        f"{len(stalls)} runs, max {max(stalls):.2f} ms"
    )


def report(trace: Trace, skip_seconds: float) -> None:
    print(f"sampled span {trace.span / 1000:.1f} s after the first {skip_seconds:g} s")
    report_frames(trace)
    report_cadence(trace)
    report_ticks(trace)
    report_contention(trace)


def parse_arguments(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("export", type=Path, help="a dotnet-trace Speedscope export")
    parser.add_argument(
        "--skip-seconds",
        type=float,
        default=0.0,
        help="ignore runs that start within this many seconds of the export's start",
    )
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    arguments = parse_arguments(argv)
    if arguments.skip_seconds < 0:
        print("--skip-seconds must not be negative", file=sys.stderr)
        return 2
    if not arguments.export.is_file():
        print(f"no such file: {arguments.export}", file=sys.stderr)
        return 1

    trace = read_trace(arguments.export, arguments.skip_seconds)
    report(trace, arguments.skip_seconds)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
