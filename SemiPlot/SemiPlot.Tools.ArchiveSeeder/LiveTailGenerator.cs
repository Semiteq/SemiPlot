namespace SemiPlot.Tools.ArchiveSeeder;

// One follow tick's raw rows. RawLayerGenerator walks a whole span from a seeded random stream and
// keeps its state in that walk; a follow run generates one wall-clock span after another and keeps no
// state between them, so every row here is instead a pure function of absolute time. That is what
// makes two adjacent spans disjoint, which PRIMARY KEY (id, l, t) requires: the second copy of a row
// fails the COPY.
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

		var interval = ChangeIntervalTicks(options.ChangeSeconds);

		foreach (var pen in RawLayerGenerator.SelectPens(options.PenCount))
		{
			AppendPen(rows, pen, options.Seed, interval, from.Ticks, toExclusive.Ticks);
		}

		return rows;
	}

	// A change carries its pre-anchor — the previous value one poll interval earlier, then the new
	// value at the change tick — which is the vendor's two-rows-per-change shape
	// (docs/architecture/bench.md#what-the-generator-emits). A change interval no longer than the poll
	// interval leaves no room for the anchor, and the archive holds none there either.
	private static void AppendPen(
		List<ArchiveRow> rows,
		SyntheticPen pen,
		long seed,
		long intervalTicks,
		long fromTicks,
		long toExclusiveTicks)
	{
		var anchorOffset = RawLayerGenerator.PollInterval.Ticks;
		var carriesAnchor = intervalTicks > anchorOffset;

		for (var index = FirstIndex(fromTicks, intervalTicks); ; index++)
		{
			var changeTicks = index * intervalTicks;
			var anchorTicks = changeTicks - anchorOffset;

			if (anchorTicks >= toExclusiveTicks)
			{
				return;
			}

			if (carriesAnchor && anchorTicks >= fromTicks && anchorTicks < toExclusiveTicks)
			{
				rows.Add(Row(pen, anchorTicks, ValueAt(seed, pen, index - 1)));
			}

			if (changeTicks >= fromTicks && changeTicks < toExclusiveTicks)
			{
				rows.Add(Row(pen, changeTicks, ValueAt(seed, pen, index)));
			}
		}
	}

	// The first change on the lattice at or after the span's start. Its own anchor may fall before the
	// start, and is then the previous span's row rather than a dropped one. Index 0 is skipped so that
	// the anchor of the first emitted change reads a segment the walk has.
	private static long FirstIndex(long fromTicks, long intervalTicks)
	{
		return Math.Max(1L, (fromTicks + intervalTicks - 1) / intervalTicks);
	}

	// Whole milliseconds, so every instant on the lattice is one 'timestamp(3)' keeps exactly and an
	// in-memory uniqueness check means what the primary key means.
	private static long ChangeIntervalTicks(double changeSeconds)
	{
		if (!double.IsFinite(changeSeconds) || changeSeconds <= 0.0 || changeSeconds > FollowOptions.MaximumSeconds)
		{
			throw new ArgumentOutOfRangeException(
				nameof(changeSeconds),
				changeSeconds,
				$"The mean change interval must be finite, above 0 and at most {FollowOptions.MaximumSeconds:0}.");
		}

		var milliseconds = Math.Max(1.0, Math.Round(changeSeconds * 1000.0));

		return (long)milliseconds * TimeSpan.TicksPerMillisecond;
	}

	private static double ValueAt(long seed, SyntheticPen pen, long segmentIndex)
	{
		return SyntheticValueWalk.Value(seed, pen.PenId, segmentIndex, pen.MinValue, pen.MaxValue);
	}

	private static ArchiveRow Row(SyntheticPen pen, long ticks, double value)
	{
		return new((int)pen.PenId, ArchiveRow.RawLayer, new DateTime(ticks), value, ArchiveRow.OrdinaryQuality);
	}
}
