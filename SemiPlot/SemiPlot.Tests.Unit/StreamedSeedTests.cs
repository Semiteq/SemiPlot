using AwesomeAssertions;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Unit;

// The fill hands the COPY a day at a time, so no whole span is ever held in memory; the rows it writes
// are still those of one materialised pass over the span.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class StreamedSeedTests
{
	[Theory]
	[InlineData(ArchiveSchedule.Continuous)]
	[InlineData(ArchiveSchedule.Shifts)]
	public void TheStreamedRowsOfAThreeDayRunEqualOneMaterialisedPass(ArchiveSchedule schedule)
	{
		var options = ThreeDays(schedule);
		var raw = RawLayerGenerator.Generate(options);

		BreakPlan.Create(options).Runs.Should().Contain(
			run => run.Start.Date != run.End.AddTicks(-1).Date,
			"a run crossing midnight is what the day split has to cut through");

		InKeyOrder(SeedFiller.Rows(options)).Should().Equal(InKeyOrder([.. raw, .. LayerThinner.ThinAll(raw)]));
	}

	[Theory]
	[InlineData(ArchiveSchedule.Continuous)]
	[InlineData(ArchiveSchedule.Shifts)]
	public void EachDayIsStreamedWholeRawRowsFirstBeforeTheNextDay(ArchiveSchedule schedule)
	{
		var rows = SeedFiller.Rows(ThreeDays(schedule)).ToArray();

		rows.Select(row => row.Timestamp.Date).Distinct().Should().HaveCountGreaterThanOrEqualTo(3);
		rows.Select(StreamPosition).Should().BeInAscendingOrder();
	}

	[Fact]
	public void ARefusedPlanThrowsBeforeAnyRowIsEnumerated()
	{
		var act = () => SeedFiller.Rows(BenchOptions.For(breaks: 200));

		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	// Three days to a Friday 03:00 under shifts, so Thursday's session runs past midnight to the end.
	private static SeederOptions ThreeDays(ArchiveSchedule schedule)
	{
		return schedule == ArchiveSchedule.Shifts
			? BenchOptions.For(days: 3, pens: 7, changeSeconds: 30.0, breaks: 0) with
			{
				End = BenchOptions.End.AddHours(3),
				Schedule = ArchiveSchedule.Shifts,
			}
			: BenchOptions.For(days: 3, pens: 4);
	}

	// A day's raw rows, then its coarse rows, then the next day's.
	private static long StreamPosition(ArchiveRow row)
	{
		return (row.Timestamp.Date.Ticks * 2) + (row.Layer == ArchiveRow.RawLayer ? 0 : 1);
	}

	private static IReadOnlyList<ArchiveRow> InKeyOrder(IEnumerable<ArchiveRow> rows)
	{
		return [.. rows.OrderBy(row => row.Layer).ThenBy(row => row.Id).ThenBy(row => row.Timestamp)];
	}
}
