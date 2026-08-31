using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data;

// docs/architecture/scada-archive.md#quality-and-gaps: a break is bounded by a q = 32 row and a q = 16
// row with nothing in between, and the project stops as a whole, so every pen breaks at the same
// instant.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class BreakGenerationTests
{
	[Fact]
	public void BreakPlanPlacesTheRequestedNumberOfBreaks()
	{
		var plan = BreakPlan.Create(Options(breaks: 6));

		Assert.Equal(6, plan.Breaks.Count);
		Assert.Equal(7, plan.Runs.Count);
	}

	[Fact]
	public void EveryBreakLastsLongEnoughToEmptyAWholeMinutePeriod()
	{
		var plan = BreakPlan.Create(Options(breaks: 6));

		foreach (var window in plan.Breaks)
		{
			var duration = window.End - window.Start;

			Assert.InRange(duration, BreakPlan.MinimumDuration, BreakPlan.MaximumDuration);
			Assert.True(duration > TimeSpan.FromMinutes(2), $"a break of {duration} can leave every minute occupied");
		}
	}

	[Fact]
	public void BreaksAreOrderedInsideTheSpanAndNeverTouch()
	{
		var options = Options(breaks: 6);
		var plan = BreakPlan.Create(options);
		var previousEnd = options.Start;

		foreach (var window in plan.Breaks)
		{
			Assert.True(
				window.Start - previousEnd >= BreakPlan.MinimumRun,
				"two breaks left no archiving between them");
			Assert.True(window.End < options.End);

			previousEnd = window.End;
		}

		Assert.True(options.End - previousEnd >= BreakPlan.MinimumRun);
	}

	[Fact]
	public void RunsTileTheSpanAroundTheBreaks()
	{
		var options = Options(breaks: 4);
		var plan = BreakPlan.Create(options);

		Assert.Equal(options.Start, plan.Runs[0].Start);
		Assert.Equal(options.End, plan.Runs[^1].End);

		for (var index = 0; index < plan.Breaks.Count; index++)
		{
			Assert.Equal(plan.Breaks[index].Start, plan.Runs[index].End);
			Assert.Equal(plan.Breaks[index].End, plan.Runs[index + 1].Start);
		}
	}

	[Fact]
	public void IdenticalSeedsPlaceIdenticalBreaks()
	{
		Assert.Equal(BreakPlan.Create(Options()).Breaks, BreakPlan.Create(Options()).Breaks);
		Assert.NotEqual(BreakPlan.Create(Options()).Breaks, BreakPlan.Create(Options(seed: 2)).Breaks);
	}

	[Fact]
	public void ASpanTooShortForTheRequestedBreaksIsRejected()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => BreakPlan.Create(Options(breaks: 200)));
	}

	// The cap SeederOptions rejects a --break-count against is the exact count Create still accepts, so
	// the CLI never refuses a span that would have worked and never admits one that would throw.
	[Fact]
	public void TheReportedMaximumIsTheLargestBreakCountTheSpanHolds()
	{
		var maximum = BreakPlan.MaximumBreaks(TimeSpan.FromDays(1));

		Assert.Equal(72, maximum);
		Assert.Equal(maximum, BreakPlan.Create(Options(breaks: maximum)).Breaks.Count);
		Assert.Throws<ArgumentOutOfRangeException>(() => BreakPlan.Create(Options(breaks: maximum + 1)));
	}

	[Fact]
	public void NoRowFallsInsideABreak()
	{
		var options = Options();
		var plan = BreakPlan.Create(options);

		foreach (var row in RawLayerGenerator.Generate(options))
		{
			Assert.DoesNotContain(
				plan.Breaks,
				window => row.Timestamp >= window.Start && row.Timestamp < window.End);
		}
	}

	// The marker rows of one pen read as a strict 32, 16, 32, 16 sequence: a stop is always followed
	// by the start that ends it, and never by a second stop.
	[Fact]
	public void MarkersComeInOrderedStopThenResumePairs()
	{
		var options = Options();
		var expected = options.BreakCount;

		foreach (var pen in BenchRows.ByPen(RawLayerGenerator.Generate(options)))
		{
			var markers = pen.Where(row => row.Quality != ArchiveRow.OrdinaryQuality).ToArray();

			Assert.Equal(expected * 2, markers.Length);

			for (var index = 0; index < markers.Length; index++)
			{
				var quality = index % 2 == 0
					? ArchiveRow.LastBeforeBreakQuality
					: ArchiveRow.FirstAfterBreakQuality;

				Assert.Equal(quality, markers[index].Quality);
			}
		}
	}

	// Every stop marker is a real change row: a run bounded by a break holds at least two changes, and the
	// generator refuses a change interval that leaves one with fewer rather than inventing a row.
	[Theory]
	[InlineData(5.0, 60)]
	[InlineData(1.0, 60)]
	[InlineData(1.0, 72)]
	public void EveryStopRowSitsOnTheChangeLattice(double changeSeconds, int breaks)
	{
		var options = BenchOptions.For(pens: 1, changeSeconds: changeSeconds, breaks: breaks);
		var interval = TimeSpan.FromSeconds(changeSeconds);
		var rows = RawLayerGenerator.Generate(options);

		Assert.All(
			rows.Where(row => row.Quality == ArchiveRow.LastBeforeBreakQuality),
			row => Assert.Equal(0, row.Timestamp.Ticks % interval.Ticks));
	}

	[Fact]
	public void EachMarkerPairBoundsOneBreakWindow()
	{
		var options = Options();
		var plan = BreakPlan.Create(options);
		var interval = TimeSpan.FromSeconds(options.ChangeSeconds);

		foreach (var pen in BenchRows.ByPen(RawLayerGenerator.Generate(options)))
		{
			var markers = pen.Where(row => row.Quality != ArchiveRow.OrdinaryQuality).ToArray();

			for (var index = 0; index < plan.Breaks.Count; index++)
			{
				Assert.True(markers[index * 2].Timestamp < plan.Breaks[index].Start);
				Assert.InRange(
					markers[(index * 2) + 1].Timestamp,
					plan.Breaks[index].End,
					plan.Breaks[index].End + interval);
			}
		}
	}

	// A project stop takes every pen down at once, so the resume rows of all pens share one instant.
	[Fact]
	public void BreaksApplyToEveryPenAtTheSameInstants()
	{
		var options = Options();
		var resumes = BenchRows.ByPen(RawLayerGenerator.Generate(options))
			.Select(pen => pen
				.Where(row => row.Quality == ArchiveRow.FirstAfterBreakQuality)
				.Select(row => row.Timestamp)
				.ToArray())
			.ToArray();

		Assert.All(resumes, pen => Assert.Equal(resumes[0], pen));
	}

	[Fact]
	public void BothMarkerRowsCarryRealValues()
	{
		var options = Options();
		var pens = RawLayerGenerator.SelectPens(options.PenCount).ToDictionary(pen => pen.PenId);

		foreach (var row in RawLayerGenerator.Generate(options))
		{
			if (row.Quality == ArchiveRow.OrdinaryQuality)
			{
				continue;
			}

			var pen = pens[row.Id];

			Assert.False(double.IsNaN(row.Value));
			Assert.InRange(row.Value, pen.MinValue, pen.MaxValue);
		}
	}

	// A break spans several minutes, so at least one whole calendar minute inside it holds no row of
	// any pen — the empty period LayerThinner has to survive.
	[Fact]
	public void ABreakLeavesWholeMinutePeriodsEmpty()
	{
		var options = Options();
		var plan = BreakPlan.Create(options);
		var occupied = RawLayerGenerator.Generate(options)
			.Select(row => LayerThinner.PeriodStart(row.Timestamp, LayerThinner.MinuteLayer))
			.ToHashSet();

		foreach (var window in plan.Breaks)
		{
			var first = FirstWholeMinute(window.Start);
			var empty = 0;

			for (var minute = first; minute + TimeSpan.FromMinutes(1) <= window.End; minute += TimeSpan.FromMinutes(1))
			{
				Assert.DoesNotContain(minute, occupied);

				empty++;
			}

			Assert.True(empty >= 1, $"a break of {window.End - window.Start} left no whole minute empty");
		}
	}

	[Fact]
	public void ARunWithZeroBreaksHasNoMarkerRows()
	{
		var options = Options(breaks: 0);

		Assert.Empty(BreakPlan.Create(options).Breaks);
		Assert.All(
			RawLayerGenerator.Generate(options),
			row => Assert.Equal(ArchiveRow.OrdinaryQuality, row.Quality));
	}

	// The resume row is exception two of the pair-local invariant: it carries a value of its own and
	// no pre-anchor, because a pre-anchor would fall inside the break.
	[Fact]
	public void TheResumeRowHasNoPreAnchorInsideTheBreak()
	{
		var options = Options();

		foreach (var pen in BenchRows.ByPen(RawLayerGenerator.Generate(options)))
		{
			for (var index = 1; index < pen.Count; index++)
			{
				if (pen[index].Quality != ArchiveRow.FirstAfterBreakQuality)
				{
					continue;
				}

				Assert.Equal(ArchiveRow.LastBeforeBreakQuality, pen[index - 1].Quality);
				Assert.True(pen[index].Timestamp - pen[index - 1].Timestamp > RawLayerGenerator.PollInterval);
			}
		}
	}

	private static DateTime FirstWholeMinute(DateTime timestamp)
	{
		var floor = LayerThinner.PeriodStart(timestamp, LayerThinner.MinuteLayer);

		return floor == timestamp ? floor : floor + TimeSpan.FromMinutes(1);
	}

	// Three pens carry every per-pen invariant asserted here and keep a whole day of rows cheap.
	private static SeederOptions Options(
		long seed = SeederOptions.DefaultSeed,
		int breaks = SeederOptions.DefaultBreakCount,
		int pens = 3)
	{
		return BenchOptions.For(pens: pens, seed: seed, breaks: breaks);
	}
}
