namespace SemiPlot.Tools.ArchiveSeeder;

public static class RawLayerGenerator
{
	public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

	public static IReadOnlyList<ArchiveRow> Generate(SeederOptions options)
	{
		return [.. PenStreams(options).SelectMany(pen => pen)];
	}

	/// <summary>The raw rows of every pen, one calendar day per list in day order; a day without a row is
	/// skipped.</summary>
	public static IEnumerable<IReadOnlyList<ArchiveRow>> GenerateDays(SeederOptions options)
	{
		return ByDay(PenStreams(options), options.Start.Date);
	}

	// Round-robin across the groups, never the first N: the first eight of the catalogue are all Heaters.
	public static IReadOnlyList<SyntheticPen> SelectPens(int count)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

		var groups = SyntheticPenCatalog.Build()
			.GroupBy(pen => pen.PrimaryGroup, StringComparer.Ordinal)
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
	internal static IEnumerable<ArchiveRow> WindowRows(
		SyntheticPen pen,
		LatticeValues values,
		long intervalTicks,
		long fromTicks,
		long toExclusiveTicks,
		bool resumedAfterBreak,
		bool breakFollows)
	{
		var anchorOffset = PollInterval.Ticks;
		var stride = pen.ChangeStride(intervalTicks);

		var carriesAnchor = intervalTicks > anchorOffset;

		// The plant kept moving while archiving was stopped, so the first change of a resumed run opens
		// on a level of its own.
		var dropsAnchor = resumedAfterBreak;

		for (var index = FirstIndex(fromTicks, intervalTicks); ; index++)
		{
			var changeTicks = index * intervalTicks;
			var anchorTicks = changeTicks - anchorOffset;

			// A change past the end still hands its anchor over unless a break follows:
			// that anchor would carry the q = 32 mark.
			if (anchorTicks >= toExclusiveTicks || (breakFollows && changeTicks >= toExclusiveTicks))
			{
				yield break;
			}

			var isChange = index % stride == 0;

			if (isChange && carriesAnchor && !dropsAnchor && anchorTicks >= fromTicks)
			{
				yield return Row(pen, anchorTicks, values.At(pen, stride, index - 1));
			}

			// The project start and stop write every pen, so a pen holding its value still gets the row a
			// break marker goes on: docs/architecture/bench.md#breaks.
			var marksBoundary = dropsAnchor || (breakFollows && changeTicks + intervalTicks >= toExclusiveTicks);

			if (changeTicks >= fromTicks && changeTicks < toExclusiveTicks && (isChange || marksBoundary))
			{
				yield return Row(pen, changeTicks, values.At(pen, stride, index));

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

	// Not an iterator, so a refused plan throws here rather than inside the COPY that enumerates the rows.
	private static IReadOnlyList<IEnumerable<ArchiveRow>> PenStreams(SeederOptions options)
	{
		var breaks = BreakPlan.Create(options);
		var intervalTicks = ChangeIntervalTicks(options.ChangeSeconds);
		var values = ValuesFor(options, intervalTicks);

		RequireTwoChangesPerRun(breaks, intervalTicks, options);

		return [.. SelectPens(options.PenCount).Select(pen => PenRows(pen, values, intervalTicks, breaks))];
	}

	private static LatticeValues ValuesFor(SeederOptions options, long intervalTicks)
	{
		return options.Schedule == ArchiveSchedule.Shifts
			? ShiftValues(options, intervalTicks)
			: LatticeValues.Continuous(options.Seed);
	}

	private static IEnumerable<ArchiveRow> PenRows(
		SyntheticPen pen,
		LatticeValues values,
		long intervalTicks,
		BreakPlan breaks)
	{
		for (var runIndex = 0; runIndex < breaks.Runs.Count; runIndex++)
		{
			var run = breaks.Runs[runIndex];
			var resumed = runIndex > 0;
			var breakFollows = runIndex < breaks.Runs.Count - 1;

			var rows = WindowRows(
				pen,
				values,
				intervalTicks,
				fromTicks: run.Start.Ticks,
				toExclusiveTicks: run.End.Ticks,
				resumedAfterBreak: resumed,
				breakFollows: breakFollows);

			foreach (var row in MarkRunBoundaries(rows, resumed, breakFollows))
			{
				yield return row;
			}
		}
	}

	// Both markers land on lattice rows: the run's first for q = 16 and its last for q = 32, and
	// RequireTwoChangesPerRun has made sure a run bounded by a break holds two distinct ones.
	private static IEnumerable<ArchiveRow> MarkRunBoundaries(
		IEnumerable<ArchiveRow> rows,
		bool resumed,
		bool breakFollows)
	{
		ArchiveRow? held = null;

		foreach (var row in rows)
		{
			if (held is not null)
			{
				yield return held;
			}

			held = held is null && resumed ? row with { Quality = ArchiveRow.FirstAfterBreakQuality } : row;
		}

		if (held is not null)
		{
			yield return breakFollows ? held with { Quality = ArchiveRow.LastBeforeBreakQuality } : held;
		}
	}

	// Each pen's rows ascend in time, so a day is every pen's rows up to the next midnight. A minute or an
	// hour period never crosses midnight, so the day is also a whole set of thinning periods.
	private static IEnumerable<IReadOnlyList<ArchiveRow>> ByDay(
		IReadOnlyList<IEnumerable<ArchiveRow>> pens,
		DateTime firstMidnight)
	{
		var cursors = pens.Select(pen => pen.GetEnumerator()).ToArray();
		var pending = new bool[cursors.Length];

		try
		{
			for (var pen = 0; pen < cursors.Length; pen++)
			{
				pending[pen] = cursors[pen].MoveNext();
			}

			for (var dayEnd = firstMidnight.AddDays(1); pending.Contains(true); dayEnd = dayEnd.AddDays(1))
			{
				var day = new List<ArchiveRow>();

				for (var pen = 0; pen < cursors.Length; pen++)
				{
					while (pending[pen] && cursors[pen].Current.Timestamp < dayEnd)
					{
						day.Add(cursors[pen].Current);
						pending[pen] = cursors[pen].MoveNext();
					}
				}

				if (day.Count > 0)
				{
					yield return day;
				}
			}
		}
		finally
		{
			foreach (var cursor in cursors)
			{
				cursor.Dispose();
			}
		}
	}

	// A run holding a single change would have to carry both marker codes at once, which the archive has
	// no code for, so with breaks every archiving run holds two.
	private static void RequireTwoChangesPerRun(BreakPlan breaks, long intervalTicks, SeederOptions options)
	{
		if (breaks.Breaks.Count == 0)
		{
			return;
		}

		foreach (var run in breaks.Runs)
		{
			var changes = FirstIndex(run.End.Ticks, intervalTicks) - FirstIndex(run.Start.Ticks, intervalTicks);

			if (changes < 2)
			{
				throw new ArgumentOutOfRangeException(
					nameof(options),
					options.ChangeSeconds,
					$"the archiving run {run.Start:O} to {run.End:O} holds fewer than two changes, so the break "
					+ "it bounds gets no marker pair.");
			}
		}
	}

	// Index 0 is skipped because its anchor would sit one poll interval before absolute tick zero, on a
	// value index of -1 the lattice does not reach.
	private static long FirstIndex(long fromTicks, long intervalTicks)
	{
		return Math.Max(1L, (fromTicks + intervalTicks - 1) / intervalTicks);
	}

	private static LatticeValues ShiftValues(SeederOptions options, long intervalTicks)
	{
		var schedule = ShiftSchedule.For(options);
		var firstDay = DateOnly.FromDateTime(options.Start);
		var dayCount = DateOnly.FromDateTime(options.End).DayNumber - firstDay.DayNumber + 1;
		var runs = Enumerable.Range(0, dayCount).SelectMany(offset => schedule.RecipeRuns(firstDay.AddDays(offset)));

		return LatticeValues.Shifts(options.Seed, intervalTicks, runs);
	}

	private static ArchiveRow Row(SyntheticPen pen, long ticks, double value)
	{
		return new ArchiveRow(pen.PenId, ArchiveRow.RawLayer, new DateTime(ticks), value,
			ArchiveRow.OrdinaryQuality);
	}
}
