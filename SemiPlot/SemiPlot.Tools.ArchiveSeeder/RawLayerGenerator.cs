namespace SemiPlot.Tools.ArchiveSeeder;

public static class RawLayerGenerator
{
	public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

	private const double IdleShare = 0.47;
	private const double StepShare = 0.40;
	private const double RampShare = 0.05;

	private const double MinimumRampSeconds = 0.4;
	private const double MaximumRampSeconds = 1.5;
	private const int MinimumSpikeTicks = 2;
	private const int SpikeTicksExclusiveMaximum = 5;
	private const double IntervalCapFactor = 8.0;

	private enum SegmentKind
	{
		Idle,
		Step,
		Ramp,
		Spike
	}

	public static IReadOnlyList<ArchiveRow> Generate(SeederOptions options)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(options.Days, 1);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ChangeSeconds, 0.0);

		var rows = new List<ArchiveRow>();
		var breaks = BreakPlan.Create(options);

		foreach (var pen in SelectPens(options.PenCount))
		{
			AppendPen(rows, pen, options, breaks);
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

	private static void AppendPen(List<ArchiveRow> rows, SyntheticPen pen, SeederOptions options, BreakPlan breaks)
	{
		var random = new SeededRandom(options.Seed, pen.PenId);
		var trace = new PenTrace(rows, (int)pen.PenId, PollInterval);

		trace.RestingLevel = LevelFor(options.Seed, pen, trace.NextSegment());

		for (var runIndex = 0; runIndex < breaks.Runs.Count; runIndex++)
		{
			var run = breaks.Runs[runIndex];
			var firstIndex = rows.Count;

			// The plant kept moving while archiving was stopped, so a resumed run opens on a level of
			// its own — which is what makes the q = 16 row a change row with no pre-anchor.
			if (runIndex > 0)
			{
				trace.RestingLevel = LevelFor(options.Seed, pen, trace.NextSegment());
			}

			trace.StartRun(run.Start);

			AppendRun(trace, pen, options, random, run.End);

			MarkRunBoundaries(rows, firstIndex, runIndex > 0, runIndex < breaks.Runs.Count - 1);
		}
	}

	private static void AppendRun(
		PenTrace trace,
		SyntheticPen pen,
		SeederOptions options,
		SeededRandom random,
		DateTime end)
	{
		var cursor = trace.LastTimestamp;

		while (cursor < end)
		{
			var kind = NextKind(random);
			var target = LevelFor(options.Seed, pen, trace.NextSegment());

			if (kind == SegmentKind.Idle)
			{
				cursor = Advance(cursor, NextInterval(random, options.ChangeSeconds));

				continue;
			}

			var instant = ChangeInstant(
				Advance(cursor, NextInterval(random, options.ChangeSeconds)),
				trace.LastTimestamp);

			if (instant >= end)
			{
				break;
			}

			Emit(trace, kind, instant, target, random, end);
			cursor = trace.LastTimestamp;
		}
	}

	// A run holding a single row between two breaks would have to carry both codes at once, which the
	// archive has no code for; it gets the poll tick it certainly also recorded.
	private static void MarkRunBoundaries(List<ArchiveRow> rows, int firstIndex, bool resumed, bool breakFollows)
	{
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

			rows.Add(only with { Timestamp = Advance(only.Timestamp, PollInterval) });
		}

		var lastIndex = rows.Count - 1;

		rows[lastIndex] = rows[lastIndex] with { Quality = ArchiveRow.LastBeforeBreakQuality };
	}

	// Segment boundaries fall at arbitrary millisecond offsets — the archive's timestamps sit on no
	// global lattice — but a 100 ms poll cannot report two changes closer together than one interval.
	private static DateTime ChangeInstant(DateTime drawn, DateTime lastTimestamp)
	{
		var earliest = Advance(lastTimestamp, PollInterval);

		return drawn < earliest ? earliest : drawn;
	}

	// The step that ends a walk may land outside the run, but not outside the calendar, which is where
	// DateTime addition throws. Saturating at the last representable instant leaves it at or after every
	// end, so every loop reading it still stops.
	private static DateTime Advance(DateTime from, TimeSpan step)
	{
		return step < DateTime.MaxValue - from ? from + step : DateTime.MaxValue;
	}

	private static void Emit(
		PenTrace trace,
		SegmentKind kind,
		DateTime instant,
		double target,
		SeededRandom random,
		DateTime end)
	{
		trace.RestingLevel = kind switch
		{
			SegmentKind.Step => EmitStep(trace, instant, target),
			SegmentKind.Ramp => EmitRamp(trace, instant, trace.RestingLevel, target, random, end),
			_ => EmitSpike(trace, instant, trace.RestingLevel, target, random, end)
		};
	}

	private static double EmitStep(PenTrace trace, DateTime instant, double target)
	{
		trace.Change(instant, target);

		return target;
	}

	private static double EmitRamp(
		PenTrace trace,
		DateTime instant,
		double from,
		double to,
		SeededRandom random,
		DateTime end)
	{
		var seconds = MinimumRampSeconds + random.NextDouble() * (MaximumRampSeconds - MinimumRampSeconds);
		var ticks = (int)Math.Round(seconds / PollInterval.TotalSeconds);
		var reached = from;

		for (var tick = 1; tick <= ticks; tick++)
		{
			var at = Advance(instant, PollInterval * (tick - 1));

			if (at >= end)
			{
				break;
			}

			reached = from + (to - from) * (tick / (double)ticks);
			trace.Change(at, reached);
		}

		return reached;
	}

	private static double EmitSpike(
		PenTrace trace,
		DateTime instant,
		double level,
		double peak,
		SeededRandom random,
		DateTime end)
	{
		var ticks = random.NextInt32(MinimumSpikeTicks, SpikeTicksExclusiveMaximum);

		for (var tick = 0; tick < ticks; tick++)
		{
			var at = Advance(instant, PollInterval * tick);

			if (at >= end)
			{
				return level;
			}

			trace.Change(at, level + (peak - level) * ((ticks - tick) / (double)ticks));
		}

		var back = Advance(instant, PollInterval * ticks);

		if (back < end)
		{
			trace.Change(back, level);
		}

		return level;
	}

	private static SegmentKind NextKind(SeededRandom random)
	{
		var draw = random.NextDouble();

		if (draw < IdleShare)
		{
			return SegmentKind.Idle;
		}

		if (draw < IdleShare + StepShare)
		{
			return SegmentKind.Step;
		}

		return draw < IdleShare + StepShare + RampShare ? SegmentKind.Ramp : SegmentKind.Spike;
	}

	// Whole milliseconds only: the column keeps three decimal places, and an interval that survives
	// rounding is one the primary key can distinguish.
	private static TimeSpan NextInterval(SeededRandom random, double changeSeconds)
	{
		var seconds = Math.Min(random.NextExponential(changeSeconds), changeSeconds * IntervalCapFactor);
		var milliseconds = Math.Max(PollInterval.TotalMilliseconds, Math.Round(seconds * 1000.0));

		return TimeSpan.FromMilliseconds(milliseconds);
	}

	private static double LevelFor(long seed, SyntheticPen pen, int segmentIndex)
	{
		return SyntheticValueWalk.Value(seed, pen.PenId, segmentIndex, pen.MinValue, pen.MaxValue);
	}
}
