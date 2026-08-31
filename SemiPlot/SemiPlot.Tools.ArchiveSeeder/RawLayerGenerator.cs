namespace SemiPlot.Tools.ArchiveSeeder;

public static class RawLayerGenerator
{
	public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

	public static IReadOnlyList<ArchiveRow> Generate(SeederOptions options)
	{
		var rows = new List<ArchiveRow>();
		var breaks = BreakPlan.Create(options);
		var intervalTicks = ChangeIntervalTicks(options.ChangeSeconds);

		if (DescribeUnmarkableRun(breaks, intervalTicks) is { } unmarkable)
		{
			throw new ArgumentOutOfRangeException(nameof(options), options.ChangeSeconds, unmarkable);
		}

		foreach (var pen in SelectPens(options.PenCount))
		{
			AppendPen(rows, pen, options.Seed, intervalTicks, breaks);
		}

		return rows;
	}

	// Round-robin across the groups, never the first N: the first eight of the catalogue are all
	// Heaters, which would leave every later slice developing against one group and one value range.
	public static IReadOnlyList<SyntheticPen> SelectPens(int count)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

		var groups = SyntheticPenCatalog.Build()
			.GroupBy(pen => pen.Group, StringComparer.Ordinal)
			.Select(group => group.ToArray())
			.ToArray();

		var available = groups.Sum(group => group.Length);

		ArgumentOutOfRangeException.ThrowIfGreaterThan(count, available);

		var selected = new List<SyntheticPen>(count);

		for (var round = 0; selected.Count < count; round++)
		{
			foreach (var group in groups)
			{
				if (round < group.Length && selected.Count < count)
				{
					selected.Add(group[round]);
				}
			}
		}

		return selected;
	}

	// The one lattice both generators write on: docs/architecture/bench.md#what-the-generator-emits.
	internal static void AppendWindow(
		List<ArchiveRow> rows,
		SyntheticPen pen,
		long seed,
		long intervalTicks,
		long fromTicks,
		long toExclusiveTicks,
		bool resumedAfterBreak,
		bool breakFollows)
	{
		var anchorOffset = PollInterval.Ticks;

		var carriesAnchor = intervalTicks > anchorOffset;

		// The plant kept moving while archiving was stopped, so the first change of a resumed run opens
		// on a level of its own — which is what makes the q = 16 row a change row with no pre-anchor.
		var dropsAnchor = resumedAfterBreak;

		for (var index = FirstIndex(fromTicks, intervalTicks); ; index++)
		{
			var changeTicks = index * intervalTicks;
			var anchorTicks = changeTicks - anchorOffset;

			// A change past the window's end still hands its anchor to the window, which is what keeps two
			// adjacent spans of a follow run from leaving a hole at the seam. A window a break closes has no
			// such successor: the anchor would be a row repeating the previous value with no change behind
			// it, and MarkRunBoundaries would put the q = 32 marker on it rather than on a real change.
			if (anchorTicks >= toExclusiveTicks || (breakFollows && changeTicks >= toExclusiveTicks))
			{
				return;
			}

			if (carriesAnchor && !dropsAnchor && anchorTicks >= fromTicks)
			{
				rows.Add(Row(pen, anchorTicks, ValueAt(seed, pen, index - 1)));
			}

			if (changeTicks >= fromTicks && changeTicks < toExclusiveTicks)
			{
				rows.Add(Row(pen, changeTicks, ValueAt(seed, pen, index)));

				dropsAnchor = false;
			}
		}
	}

	// Whole milliseconds only: the column keeps three decimal places, so an interval that survives the
	// rounding is one the primary key can distinguish.
	internal static long ChangeIntervalTicks(double changeSeconds)
	{
		var milliseconds = Math.Max(1.0, Math.Round(changeSeconds * 1000.0));

		return (long)milliseconds * TimeSpan.TicksPerMillisecond;
	}

	private static void AppendPen(
		List<ArchiveRow> rows,
		SyntheticPen pen,
		long seed,
		long intervalTicks,
		BreakPlan breaks)
	{
		for (var runIndex = 0; runIndex < breaks.Runs.Count; runIndex++)
		{
			var run = breaks.Runs[runIndex];
			var firstIndex = rows.Count;
			var resumed = runIndex > 0;
			var breakFollows = runIndex < breaks.Runs.Count - 1;

			AppendWindow(
				rows,
				pen,
				seed,
				intervalTicks,
				fromTicks: run.Start.Ticks,
				toExclusiveTicks: run.End.Ticks,
				resumedAfterBreak: resumed,
				breakFollows: breakFollows);

			MarkRunBoundaries(rows, firstIndex, resumed, breakFollows);
		}
	}

	// A run holding a single row between two breaks would have to carry both codes at once, which the
	// archive has no code for; it gets the poll tick it certainly also recorded. The empty run this
	// returns on is the one with no break on either side — Generate refuses every other kind ahead of
	// the walk, through DescribeUnmarkableRun.
	private static void MarkRunBoundaries(List<ArchiveRow> rows, int firstIndex, bool resumed, bool breakFollows)
	{
		if (firstIndex == rows.Count)
		{
			return;
		}

		if (resumed)
		{
			rows[firstIndex] = rows[firstIndex] with { Quality = ArchiveRow.FirstAfterBreakQuality };
		}

		if (!breakFollows)
		{
			return;
		}

		if (resumed && rows.Count - 1 == firstIndex)
		{
			var only = rows[firstIndex];

			rows.Add(only with { Timestamp = only.Timestamp + PollInterval });
		}

		var lastIndex = rows.Count - 1;

		rows[lastIndex] = rows[lastIndex] with { Quality = ArchiveRow.LastBeforeBreakQuality };
	}

	internal static string? DescribeUnmarkableRun(BreakPlan breaks, long intervalTicks)
	{
		for (var runIndex = 0; runIndex < breaks.Runs.Count; runIndex++)
		{
			var run = breaks.Runs[runIndex];
			var resumed = runIndex > 0;
			var breakFollows = runIndex < breaks.Runs.Count - 1;

			if (!resumed && !breakFollows)
			{
				continue;
			}

			var changeTicks = FirstIndex(run.Start.Ticks, intervalTicks) * intervalTicks;

			if (changeTicks >= run.End.Ticks)
			{
				return $"the archiving run {run.Start:O} to {run.End:O} holds no change, so the break it bounds "
					+ "gets no marker pair.";
			}

			var holdsOneChange = changeTicks + intervalTicks >= run.End.Ticks;

			if (resumed && breakFollows && holdsOneChange
				&& changeTicks + PollInterval.Ticks >= run.End.Ticks)
			{
				return $"the archiving run {run.Start:O} to {run.End:O} holds a single change with no room for "
					+ "the stop row synthesised one poll interval after it.";
			}
		}

		return null;
	}

	// Index 0 is skipped because its anchor would sit one poll interval before absolute tick zero, on a
	// value index of -1 the lattice does not reach.
	private static long FirstIndex(long fromTicks, long intervalTicks)
	{
		return Math.Max(1L, (fromTicks + intervalTicks - 1) / intervalTicks);
	}

	private static double ValueAt(long seed, SyntheticPen pen, long index)
	{
		return SyntheticValueWalk.Value(seed, pen.PenId, index, pen.MinValue, pen.MaxValue);
	}

	private static ArchiveRow Row(SyntheticPen pen, long ticks, double value)
	{
		return new ArchiveRow(pen.PenId, ArchiveRow.RawLayer, new DateTime(ticks), value,
			ArchiveRow.OrdinaryQuality);
	}
}
