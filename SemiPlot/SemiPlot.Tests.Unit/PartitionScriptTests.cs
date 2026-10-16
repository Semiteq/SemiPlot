using AwesomeAssertions;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Unit;

// docs/architecture/scada-archive.md#database-objects for the tpYYYYmMMdDD name; a missing partition
// sends the seeder's rows to tpdefault instead of failing the write.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class PartitionScriptTests
{
	[Theory]
	[InlineData(2026, 8, 4, "tp2026m08d04")]
	[InlineData(2026, 1, 1, "tp2026m01d01")]
	[InlineData(2026, 12, 31, "tp2026m12d31")]
	[InlineData(2024, 2, 29, "tp2024m02d29")]
	public void ThePartitionNameCarriesTheZeroPaddedDay(int year, int month, int day, string expected)
	{
		PartitionScript.PartitionName(new DateTime(year, month, day)).Should().Be(expected);
	}

	[Fact]
	public void ThePartitionNameIgnoresTheTimeOfDay()
	{
		var noon = new DateTime(2026, 8, 4, 12, 34, 56, DateTimeKind.Unspecified);

		PartitionScript.PartitionName(noon).Should().Be("tp2026m08d04");
	}

	[Theory]
	[InlineData(2026, 1, 1, "tp2026m01d01", "2026-01-01", "2026-01-02")]
	[InlineData(2026, 1, 31, "tp2026m01d31", "2026-01-31", "2026-02-01")]
	[InlineData(2026, 12, 31, "tp2026m12d31", "2026-12-31", "2027-01-01")]
	public void TheStatementBoundsRunFromMidnightToTheNextMidnight(
		int year, int month, int day, string partition, string from, string to)
	{
		var statement = PartitionScript.CreateStatement(new DateTime(year, month, day));

		statement.Should().Be(
			$"CREATE TABLE IF NOT EXISTS public.{partition} PARTITION OF public.trends "
				+ $"FOR VALUES FROM ('{from} 00:00:00') TO ('{to} 00:00:00');");
	}

	// --end is exclusive, so the newest row falls strictly before it. A partition for the following day
	// would hold nothing, and the day after a run is not a day the run covers.
	[Fact]
	public void AnEndOnMidnightCreatesNoPartitionForTheFollowingDay()
	{
		var days = PartitionScript.CoveredDays(new DateTime(2026, 1, 1), new DateTime(2026, 1, 2));

		days.Should().Equal([new DateTime(2026, 1, 1)]);
	}

	[Fact]
	public void AnEndOneMillisecondPastMidnightCoversTheFollowingDay()
	{
		var days = PartitionScript.CoveredDays(
			new DateTime(2026, 1, 1),
			new DateTime(2026, 1, 2).AddMilliseconds(1));

		days.Should().Equal([new DateTime(2026, 1, 1), new DateTime(2026, 1, 2)]);
	}

	[Fact]
	public void AStartInsideADayCoversThatDayFromItsMidnight()
	{
		var days = PartitionScript.CoveredDays(
			new DateTime(2026, 1, 1, 13, 50, 44, DateTimeKind.Unspecified),
			new DateTime(2026, 1, 2));

		days.Should().Equal([new DateTime(2026, 1, 1)]);
	}

	[Fact]
	public void AMultiDaySpanCoversEveryDayItTouches()
	{
		var days = PartitionScript.CoveredDays(new DateTime(2026, 1, 30), new DateTime(2026, 2, 2));

		days.Should().Equal(
			[new DateTime(2026, 1, 30), new DateTime(2026, 1, 31), new DateTime(2026, 2, 1)]);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void AnEndAtOrBeforeTheStartIsRejected(int days)
	{
		var start = new DateTime(2026, 1, 2);

		var act = () => PartitionScript.CoveredDays(start, start.AddDays(days));

		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Fact]
	public void OneStatementIsBuiltPerCoveredDay()
	{
		var start = new DateTime(2026, 1, 30);
		var end = new DateTime(2026, 2, 2);

		var statements = PartitionScript.CreateStatements(start, end);

		statements.Count.Should().Be(PartitionScript.CoveredDays(start, end).Count);
		statements.Should().AllSatisfy(statement => statement.Should().StartWith("CREATE TABLE IF NOT EXISTS public.tp"));
		statements.Distinct(StringComparer.Ordinal).Count().Should().Be(statements.Count);
	}
}
