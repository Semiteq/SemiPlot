using AwesomeAssertions;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Unit;

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

		plan.Breaks.Count.Should().Be(6);
		plan.Runs.Count.Should().Be(7);
	}

	[Fact]
	public void EveryBreakLastsLongEnoughToEmptyAWholeMinutePeriod()
	{
		var plan = BreakPlan.Create(Options(breaks: 6));

		foreach (var window in plan.Breaks)
		{
			var duration = window.End - window.Start;

			duration.Should().BeGreaterThanOrEqualTo(BreakPlan.MinimumDuration).And.BeLessThanOrEqualTo(BreakPlan.MaximumDuration);
			(duration > TimeSpan.FromMinutes(2)).Should().BeTrue($"a break of {duration} can leave every minute occupied");
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
			(window.Start - previousEnd >= BreakPlan.MinimumRun).Should().BeTrue(
				"two breaks left no archiving between them");
			(window.End < options.End).Should().BeTrue();

			previousEnd = window.End;
		}

		(options.End - previousEnd >= BreakPlan.MinimumRun).Should().BeTrue();
	}

	[Fact]
	public void RunsTileTheSpanAroundTheBreaks()
	{
		var options = Options(breaks: 4);
		var plan = BreakPlan.Create(options);

		plan.Runs[0].Start.Should().Be(options.Start);
		plan.Runs[^1].End.Should().Be(options.End);

		for (var index = 0; index < plan.Breaks.Count; index++)
		{
			plan.Runs[index].End.Should().Be(plan.Breaks[index].Start);
			plan.Runs[index + 1].Start.Should().Be(plan.Breaks[index].End);
		}
	}

	[Fact]
	public void IdenticalSeedsPlaceIdenticalBreaks()
	{
		BreakPlan.Create(Options()).Breaks.Should().Equal(BreakPlan.Create(Options()).Breaks);
		BreakPlan.Create(Options(seed: 2)).Breaks.Should().NotEqual(BreakPlan.Create(Options()).Breaks);
	}

	[Fact]
	public void ASpanTooShortForTheRequestedBreaksIsRejected()
	{
		var act = () => BreakPlan.Create(Options(breaks: 200));

		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	// The cap SeederOptions rejects a --break-count against is the exact count Create still accepts, so
	// the CLI never refuses a span that would have worked and never admits one that would throw.
	[Fact]
	public void TheReportedMaximumIsTheLargestBreakCountTheSpanHolds()
	{
		var maximum = BreakPlan.MaximumBreaks(TimeSpan.FromDays(1));

		maximum.Should().Be(72);
		BreakPlan.Create(Options(breaks: maximum)).Breaks.Count.Should().Be(maximum);

		var act = () => BreakPlan.Create(Options(breaks: maximum + 1));

		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Fact]
	public void NoRowFallsInsideABreak()
	{
		var options = Options();
		var plan = BreakPlan.Create(options);

		foreach (var row in RawLayerGenerator.Generate(options))
		{
			plan.Breaks.Should().NotContain(
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

			markers.Length.Should().Be(expected * 2);

			for (var index = 0; index < markers.Length; index++)
			{
				var quality = index % 2 == 0
					? ArchiveRow.LastBeforeBreakQuality
					: ArchiveRow.FirstAfterBreakQuality;

				markers[index].Quality.Should().Be(quality);
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

		rows.Where(row => row.Quality == ArchiveRow.LastBeforeBreakQuality).Should().AllSatisfy(
			row => (row.Timestamp.Ticks % interval.Ticks).Should().Be(0));
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
				(markers[index * 2].Timestamp < plan.Breaks[index].Start).Should().BeTrue();
				markers[(index * 2) + 1].Timestamp.Should().BeOnOrAfter(plan.Breaks[index].End)
					.And.BeOnOrBefore(plan.Breaks[index].End + interval);
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

		resumes.Should().AllSatisfy(pen => pen.Should().Equal(resumes[0]));
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

			double.IsNaN(row.Value).Should().BeFalse();
			row.Value.Should().BeInRange(pen.MinValue, pen.MaxValue);
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
				occupied.Should().NotContain(minute);

				empty++;
			}

			(empty >= 1).Should().BeTrue($"a break of {window.End - window.Start} left no whole minute empty");
		}
	}

	[Fact]
	public void ARunWithZeroBreaksHasNoMarkerRows()
	{
		var options = Options(breaks: 0);

		BreakPlan.Create(options).Breaks.Should().BeEmpty();
		RawLayerGenerator.Generate(options).Should().AllSatisfy(
			row => row.Quality.Should().Be(ArchiveRow.OrdinaryQuality));
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

				pen[index - 1].Quality.Should().Be(ArchiveRow.LastBeforeBreakQuality);
				(pen[index].Timestamp - pen[index - 1].Timestamp > RawLayerGenerator.PollInterval).Should().BeTrue();
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
