"""Per-frame cost of a drag, read from a dotnet-trace Speedscope export.

Collect while dragging the chart, then read the export:

    dotnet-trace collect -p (Get-Process SemiPlot.UI).Id --format Speedscope `
        --duration 00:00:45 -o drag.speedscope.json
    python scripts/perf/trace-shares.py drag.speedscope.speedscope.json

dotnet-trace writes the converted file next to the requested output and names it
`<name>.speedscope.json`, so the argument above is not a typo.

The script prints call count, total, mean and max milliseconds for the frames of the chart's
frame and history paths, summed over every evented profile in the export. Frames are matched by
substring, so `Polygon.Render` matches
`ScottPlot!ScottPlot.Plottables.Polygon.Render(class ScottPlot.RenderPack)`. A frame that recurses
is timed on its outermost call only.
"""

import json
import sys
from pathlib import Path

FRAMES = (
    "RenderOnce",
    "Polygon.Render",
    "SKCanvas.DrawPath",
    "OnNavigationWindowChanged",
    "ApplyHistory",
    "QueryHistoryAsync",
)

MILLISECONDS_PER_UNIT = {
    "nanoseconds": 1e-6,
    "microseconds": 1e-3,
    "milliseconds": 1.0,
    "seconds": 1000.0,
}


def label_of(frame_name: str) -> str | None:
    for frame in FRAMES:
        if frame in frame_name:
            return frame
    return None


def measure_profile(
    profile: dict, labels: list[str | None], scale: float
) -> dict[str, list[float]]:
    """Durations per frame of interest, from one evented profile's open and close events."""
    durations: dict[str, list[float]] = {frame: [] for frame in FRAMES}
    depth = dict.fromkeys(FRAMES, 0)
    opened_at: dict[str, float] = {}
    stack: list[str | None] = []

    for event in profile["events"]:
        at = event["at"] * scale
        if event["type"] == "O":
            label = labels[event["frame"]]
            stack.append(label)
            if label is not None:
                if depth[label] == 0:
                    opened_at[label] = at
                depth[label] += 1
            continue

        if not stack:
            raise ValueError(
                "The export closes a frame that was never opened, so it is truncated and every "
                "total below it would be wrong."
            )

        label = stack.pop()
        if label is None:
            continue

        depth[label] -= 1
        if depth[label] == 0:
            durations[label].append(at - opened_at[label])

    return durations


def read_durations(path: Path) -> tuple[dict[str, list[float]], float]:
    """Durations per frame of interest and the sampled span, over an export's evented profiles."""
    with path.open(encoding="utf-8") as export:
        document = json.load(export)

    labels = [label_of(frame["name"]) for frame in document["shared"]["frames"]]
    durations: dict[str, list[float]] = {frame: [] for frame in FRAMES}
    span = 0.0

    for profile in document["profiles"]:
        if profile["type"] != "evented":
            continue

        unit = profile.get("unit", "milliseconds")
        if unit not in MILLISECONDS_PER_UNIT:
            raise ValueError(f"Unsupported profile unit: {unit}")

        scale = MILLISECONDS_PER_UNIT[unit]
        span = max(span, (profile["endValue"] - profile["startValue"]) * scale)
        for frame, measured in measure_profile(profile, labels, scale).items():
            durations[frame].extend(measured)

    return durations, span


def report(durations: dict[str, list[float]], span: float) -> None:
    print(f"sampled span {span / 1000:.1f} s")
    print(f"{'frame':<28}{'calls':>8}{'total ms':>12}{'mean ms':>10}{'max ms':>10}")

    for frame in FRAMES:
        measured = durations[frame]
        if not measured:
            print(f"{frame:<28}{0:>8}{'-':>12}{'-':>10}{'-':>10}")
            continue

        total = sum(measured)
        mean = total / len(measured)
        print(f"{frame:<28}{len(measured):>8}{total:>12.1f}{mean:>10.1f}{max(measured):>10.1f}")


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(f"usage: {Path(argv[0]).name} <export.speedscope.json>", file=sys.stderr)
        return 2

    path = Path(argv[1])
    if not path.is_file():
        print(f"no such file: {path}", file=sys.stderr)
        return 1

    durations, span = read_durations(path)
    report(durations, span)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
