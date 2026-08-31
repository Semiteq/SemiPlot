namespace SemiPlot.Tools.ArchiveSeeder;

// A break is the SCADA project stopped: no rows anywhere in the interval, the last row before it
// marked q = 32 and the first row after it q = 16 (docs/architecture/scada-archive.md#quality-and-gaps).
// Breaks hit every pen at the same instants, because the project stops as a whole.
public sealed class BreakPlan
{
	// Three minutes is the shortest break that always leaves a whole calendar minute with no rows in
	// it, so LayerThinner faces an empty period rather than a merely thinner one.
	public static readonly TimeSpan MinimumDuration = TimeSpan.FromMinutes(3);
	public static readonly TimeSpan MaximumDuration = TimeSpan.FromMinutes(10);

	// Archiving on either side of a break, which also keeps two breaks from touching.
	public static readonly TimeSpan MinimumRun = TimeSpan.FromMinutes(5);

	private BreakPlan(IReadOnlyList<Window> breaks, DateTime start, DateTime end)
	{
		Breaks = breaks;
		Runs = BuildRuns(breaks, start, end);
	}

	public IReadOnlyList<Window> Breaks { get; }

	// The archiving runs the breaks leave behind, in order and one more than there are breaks: the
	// first reaches the first break and the last covers everything after the final one.
	public IReadOnlyList<Window> Runs { get; }

	// The largest break count a span can hold: every break takes a slot of its own, and a slot has to
	// fit the longest downtime that can be drawn plus the minimum archiving run on either side of it.
	public static int MaximumBreaks(TimeSpan span)
	{
		return (int)(span.Ticks / (MaximumDuration + MinimumRun + MinimumRun).Ticks);
	}

	public static BreakPlan Create(SeederOptions options)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(options.BreakCount);

		if (options.BreakCount == 0)
		{
			return new BreakPlan([], options.Start, options.End);
		}

		var span = options.End - options.Start;

		if (options.BreakCount > MaximumBreaks(span))
		{
			throw new ArgumentOutOfRangeException(
				nameof(options),
				options.BreakCount,
				$"A span of {span} holds no room for that many breaks: each needs up to "
					+ $"{MaximumDuration} of downtime with at least {MinimumRun} of archiving on either side.");
		}

		return new BreakPlan(
			BuildWindows(options.Seed, options.Start, span / options.BreakCount, options.BreakCount),
			options.Start,
			options.End);
	}

	// One break per equal slot of the span, which spaces them across the run without letting two of
	// them meet — a stop and a start that overlap is not a shape the archive can hold.
	private static IReadOnlyList<Window> BuildWindows(long seed, DateTime start, TimeSpan slot, int count)
	{
		var windows = new List<Window>(count);

		for (var index = 0; index < count; index++)
		{
			// Two draws per break, so two coordinates. They run below zero, clear of the pen identifiers
			// the same hash carries — the catalogue starts at 1000 — so break placement and pen values
			// never read the same point.
			var durationFraction = SyntheticValueWalk.Fraction(seed, -1 - (2 * index));
			var offsetFraction = SyntheticValueWalk.Fraction(seed, -2 - (2 * index));

			var drawn = MinimumDuration + (MaximumDuration - MinimumDuration) * durationFraction;
			var duration = WholeMilliseconds(drawn);
			var headroom = slot - duration - MinimumRun - MinimumRun;
			var offset = WholeMilliseconds(headroom * offsetFraction);
			var breakStart = ArchiveRow.TruncateToMilliseconds(start + slot * index + MinimumRun + offset);

			windows.Add(new Window(breakStart, ArchiveRow.TruncateToMilliseconds(breakStart + duration)));
		}

		return windows;
	}

	private static IReadOnlyList<Window> BuildRuns(IReadOnlyList<Window> breaks, DateTime start, DateTime end)
	{
		var runs = new List<Window>(breaks.Count + 1);
		var cursor = start;

		foreach (var window in breaks)
		{
			runs.Add(new Window(cursor, window.Start));
			cursor = window.End;
		}

		runs.Add(new Window(cursor, end));

		return runs;
	}

	// Break boundaries carry the rows' millisecond resolution so boundary and row compare exactly.
	private static TimeSpan WholeMilliseconds(TimeSpan value)
	{
		return TimeSpan.FromMilliseconds(Math.Round(value.TotalMilliseconds));
	}

	public readonly record struct Window(DateTime Start, DateTime End);
}
