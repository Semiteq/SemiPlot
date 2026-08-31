namespace SemiPlot.Tools.ArchiveSeeder;

public static class LiveTailGenerator
{
	// The rows of [from, toExclusive), in ascending order per pen. A row belongs to the span its own
	// timestamp falls in, so a change whose pre-anchor sits before `from` was already written by the
	// tick before this one.
	public static IReadOnlyList<ArchiveRow> Generate(FollowOptions options, DateTime from, DateTime toExclusive)
	{
		ArgumentNullException.ThrowIfNull(options);

		var rows = new List<ArchiveRow>();

		if (toExclusive <= from)
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
				fromTicks: from.Ticks,
				toExclusiveTicks: toExclusive.Ticks,
				resumedAfterBreak: false,
				breakFollows: false);
		}

		return rows;
	}
}
