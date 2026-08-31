namespace SemiPlot.Tools.ArchiveSeeder;

public static class LiveTailGenerator
{
	// The rows with `after` < t <= `to`, in ascending order per pen. The window is open at its start and
	// closed at its end because both bounds are instants the archive already accounts for: a restart hands
	// in the newest row the archive holds, and the tick loop hands in the previous tick's own instant, whose
	// rows that tick wrote. Every timestamp is a whole millisecond, so one tick past each bound is the exact
	// conversion to the inclusive start and exclusive end AppendWindow walks.
	public static IReadOnlyList<ArchiveRow> Generate(FollowOptions options, DateTime after, DateTime to)
	{
		ArgumentNullException.ThrowIfNull(options);

		var rows = new List<ArchiveRow>();

		if (to <= after)
		{
			return rows;
		}

		var interval = RawLayerGenerator.ChangeIntervalTicks(options.ChangeSeconds);

		foreach (var pen in RawLayerGenerator.SelectPens(options.PenCount))
		{
			RawLayerGenerator.AppendWindow(
				rows,
				pen,
				options.Seed,
				interval,
				fromTicks: after.Ticks + 1,
				toExclusiveTicks: to.Ticks + 1,
				resumedAfterBreak: false,
				breakFollows: false);
		}

		return rows;
	}
}
