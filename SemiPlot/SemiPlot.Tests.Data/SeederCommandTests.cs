using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class SeederCommandTests
{
	private const string Connection = BenchOptions.ConnectionString;
	private const string EndText = BenchOptions.EndText;

	[Fact]
	public void ASeedingRunAppliesDefaultsWhenOnlyRequiredOptionsAreGiven()
	{
		var run = SeederCommand.Parse(["--connection", Connection, "--end", EndText]);

		Assert.Empty(run.Errors);
		Assert.Null(run.Follow);

		var options = Assert.IsType<SeederOptions>(run.Seed);

		Assert.Equal(Connection, options.ConnectionString);
		Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified), options.End);
		Assert.Equal(SeederOptions.DefaultDays, options.Days);
		Assert.Equal(SeederOptions.DefaultPenCount, options.PenCount);
		Assert.Equal(SeederOptions.DefaultSeed, options.Seed);
		Assert.Equal(SeederOptions.DefaultChangeSeconds, options.ChangeSeconds);
		Assert.Equal(SeederOptions.DefaultBreakCount, options.BreakCount);
		Assert.Null(options.AdminConnectionString);
		Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), options.Start);
	}

	[Fact]
	public void ASeedingRunAcceptsEveryParameter()
	{
		var run = SeederCommand.Parse(
		[
			"--connection", Connection,
			"--admin-connection", "Host=localhost;Database=archive;Username=postgres",
			"--days", "3",
			"--pens", "12",
			"--seed", "77",
			"--change-seconds", "2.5",
			"--break-count", "6",
			"--end", EndText
		]);

		Assert.Empty(run.Errors);

		var options = Assert.IsType<SeederOptions>(run.Seed);

		Assert.Equal("Host=localhost;Database=archive;Username=postgres", options.AdminConnectionString);
		Assert.Equal(3, options.Days);
		Assert.Equal(12, options.PenCount);
		Assert.Equal(77L, options.Seed);
		Assert.Equal(2.5, options.ChangeSeconds);
		Assert.Equal(6, options.BreakCount);
	}

	[Fact]
	public void AFollowRunAppliesDefaultsWhenOnlyRequiredOptionsAreGiven()
	{
		var run = SeederCommand.Parse(["--connection", Connection, "--follow", "1"]);

		Assert.Empty(run.Errors);
		Assert.Null(run.Seed);

		var options = Assert.IsType<FollowOptions>(run.Follow);

		Assert.Equal(Connection, options.ConnectionString);
		Assert.Equal(TimeSpan.FromSeconds(1), options.Interval);
		Assert.Equal(SeederOptions.DefaultPenCount, options.PenCount);
		Assert.Equal(SeederOptions.DefaultSeed, options.Seed);
		Assert.Equal(SeederOptions.DefaultChangeSeconds, options.ChangeSeconds);
	}

	[Fact]
	public void AFollowRunAcceptsEveryParameter()
	{
		var run = SeederCommand.Parse(
			["--connection", Connection, "--follow", "0.5", "--pens", "12", "--seed", "77", "--change-seconds", "2.5"]);

		Assert.Empty(run.Errors);

		var options = Assert.IsType<FollowOptions>(run.Follow);

		Assert.Equal(TimeSpan.FromMilliseconds(500), options.Interval);
		Assert.Equal(12, options.PenCount);
		Assert.Equal(77L, options.Seed);
		Assert.Equal(2.5, options.ChangeSeconds);
	}

	// A follow run appends to an archive somebody else seeded. Each of these states a span, a break plan
	// or a tag catalogue, none of which it has, so each is answered rather than silently ignored.
	[Theory]
	[InlineData("--end", "2026-01-02T00:00:00")]
	[InlineData("--days", "3")]
	[InlineData("--break-count", "4")]
	[InlineData("--admin-connection", "Host=localhost;Database=archive;Username=postgres")]
	public void AFollowRunRejectsASeedingOption(string option, string value)
	{
		var run = SeederCommand.Parse(["--connection", Connection, "--follow", "1", option, value]);

		AssertRejectedWith(run, option);
		AssertRejectedWith(run, "seeding run");
	}

	[Theory]
	[InlineData("--layers", "4")]
	[InlineData("seed-it", "--days")]
	public void AnUnknownTokenIsRejectedByName(string token, string value)
	{
		var run = SeederCommand.Parse(["--connection", Connection, "--end", EndText, token, value]);

		AssertRejectedWith(run, token);
	}

	[Fact]
	public void AnOptionWithoutAValueIsRejected()
	{
		AssertRejectedWith(SeederCommand.Parse(["--connection", Connection, "--end"]), "--end");
		AssertRejectedWith(SeederCommand.Parse(["--connection", Connection, "--follow"]), "--follow");
	}

	[Fact]
	public void ARepeatedOptionIsRejected()
	{
		var run = SeederCommand.Parse(["--connection", Connection, "--connection", Connection, "--end", EndText]);

		AssertRejectedWith(run, "--connection");
	}

	[Theory]
	[InlineData("--days", "many")]
	[InlineData("--pens", "8.5")]
	[InlineData("--seed", "one")]
	[InlineData("--change-seconds", "fast")]
	[InlineData("--break-count", "few")]
	public void ANonNumericValueIsRejectedNamingTheValue(string option, string value)
	{
		var run = SeederCommand.Parse(["--connection", Connection, "--end", EndText, option, value]);

		AssertRejectedWith(run, value);
	}

	[Fact]
	public void AMissingOrBlankConnectionIsRejected()
	{
		AssertRejectedWith(SeederCommand.Parse(["--end", EndText]), "--connection");
		AssertRejectedWith(SeederCommand.Parse(["--connection", "", "--end", EndText]), "--connection");
	}

	[Fact]
	public void ASeedingRunWithoutAnEndIsRejected()
	{
		AssertRejectedWith(SeederCommand.Parse(["--connection", Connection]), "--end");
	}

	// The archive column is 'timestamp without time zone', so a bound carrying one must not be silently
	// reinterpreted, and PartitionScript.CoveredDays cannot walk past the last representable day.
	[Theory]
	[InlineData("the-second-of-january")]
	[InlineData("2026-01-02T00:00:00Z")]
	[InlineData("2026-01-02T00:00:00+03:00")]
	[InlineData("9999-12-31T23:59:59")]
	public void AnEndThatCannotBoundTheSpanIsRejected(string value)
	{
		AssertRejectedWith(SeederCommand.Parse(["--connection", Connection, "--end", value]), "--end");
	}

	[Fact]
	public void TheLatestEndThatCanBePartitionedIsAccepted()
	{
		var run = SeederCommand.Parse(["--connection", Connection, "--end", "9999-12-31T00:00:00"]);

		Assert.Empty(run.Errors);
		Assert.NotEmpty(PartitionScript.CoveredDays(run.Seed!.Start, run.Seed.End));
	}

	// A break needs up to 10 minutes of downtime with 5 minutes of archiving on either side, so a day
	// holds at most 72; the span is the ceiling on the change interval.
	[Theory]
	[InlineData("--days", "0")]
	[InlineData("--days", "-1")]
	[InlineData("--pens", "0")]
	[InlineData("--pens", "51")]
	[InlineData("--change-seconds", "0")]
	[InlineData("--change-seconds", "-1")]
	[InlineData("--change-seconds", "NaN")]
	[InlineData("--change-seconds", "Infinity")]
	[InlineData("--change-seconds", "86401")]
	[InlineData("--break-count", "-1")]
	[InlineData("--break-count", "73")]
	public void ASeedingValueOutsideItsRangeIsRejected(string option, string value)
	{
		var run = SeederCommand.Parse(["--connection", Connection, "--end", EndText, option, value]);

		AssertRejectedWith(run, option);
	}

	[Theory]
	[InlineData("--follow", "0")]
	[InlineData("--follow", "-1")]
	[InlineData("--follow", "NaN")]
	[InlineData("--follow", "Infinity")]
	[InlineData("--follow", "86401")]
	[InlineData("--follow", "1e18")]
	[InlineData("--pens", "0")]
	[InlineData("--change-seconds", "0")]
	[InlineData("--change-seconds", "86401")]
	public void AFollowValueOutsideItsRangeIsRejected(string option, string value)
	{
		string[] required = option == "--follow"
			? ["--connection", Connection]
			: ["--connection", Connection, "--follow", "1"];

		AssertRejectedWith(SeederCommand.Parse([.. required, option, value]), option);
	}

	[Theory]
	[InlineData("--break-count", "72")]
	[InlineData("--break-count", "0")]
	[InlineData("--change-seconds", "86400")]
	public void ASeedingValueAtTheEdgeOfItsRangeIsAccepted(string option, string value)
	{
		var run = SeederCommand.Parse(["--connection", Connection, "--end", EndText, option, value]);

		Assert.Empty(run.Errors);
	}

	[Fact]
	public void EveryFailureIsReportedAtOnce()
	{
		var run = SeederCommand.Parse(
			["--connection", Connection, "--end", EndText, "--days", "many", "--pens", "lots"]);

		AssertRejectedWith(run, "many");
		AssertRejectedWith(run, "lots");
	}

	private static void AssertRejectedWith(SeederRun run, string expectedFragment)
	{
		Assert.Null(run.Seed);
		Assert.Null(run.Follow);
		Assert.Contains(run.Errors, error => error.Contains(expectedFragment, StringComparison.Ordinal));
	}
}
