namespace SemiPlot.Tools.ArchiveSeeder;

public static class LiveTailGenerator
{
	// The rows with `after` < t <= `to`, in ascending order per pen.
	public static IReadOnlyList<ArchiveRow> Generate(FollowOptions options, DateTime after, DateTime to)
	{
		var rows = new List<ArchiveRow>();

		if (to <= after)
		{
			return rows;
		}

		var interval = RawLayerGenerator.ChangeIntervalTicks(options.ChangeSeconds);

		foreach (var pen in RawLayerGenerator.SelectPens(options.PenCount))
		{
			rows.AddRange(RawLayerGenerator.WindowRows(
				pen,
				LatticeValues.Continuous(options.Seed),
				interval,
				fromTicks: after.Ticks + 1,
				toExclusiveTicks: to.Ticks + 1,
				resumedAfterBreak: false,
				breakFollows: false));
		}

		return rows;
	}
}
