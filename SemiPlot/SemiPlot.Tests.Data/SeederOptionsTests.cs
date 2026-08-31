using FluentResults;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class SeederOptionsTests
{
	private const string Connection = BenchOptions.ConnectionString;
	private const string EndText = BenchOptions.EndText;

	[Fact]
	public void ParseAppliesDefaultsWhenOnlyRequiredOptionsAreGiven()
	{
		var parsed = SeederOptions.Parse(["--connection", Connection, "--end", EndText]);

		Assert.True(parsed.IsSuccess);

		var options = parsed.Value;

		Assert.Equal(Connection, options.ConnectionString);
		Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified), options.End);
		Assert.Equal(SeederOptions.DefaultDays, options.Days);
		Assert.Equal(SeederOptions.DefaultPenCount, options.PenCount);
		Assert.Equal(SeederOptions.DefaultSeed, options.Seed);
		Assert.Equal(SeederOptions.DefaultChangeSeconds, options.ChangeSeconds);
		Assert.Equal(SeederOptions.DefaultBreakCount, options.BreakCount);
		Assert.Null(options.AdminConnectionString);
	}

	[Fact]
	public void ParseAcceptsEveryParameter()
	{
		var parsed = SeederOptions.Parse(
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

		Assert.True(parsed.IsSuccess);

		var options = parsed.Value;

		Assert.Equal("Host=localhost;Database=archive;Username=postgres", options.AdminConnectionString);
		Assert.Equal(3, options.Days);
		Assert.Equal(12, options.PenCount);
		Assert.Equal(77L, options.Seed);
		Assert.Equal(2.5, options.ChangeSeconds);
		Assert.Equal(6, options.BreakCount);
	}

	[Fact]
	public void StartIsEndMinusTheCoveredDays()
	{
		var parsed = SeederOptions.Parse(["--connection", Connection, "--end", EndText, "--days", "3"]);

		Assert.Equal(new DateTime(2025, 12, 30, 0, 0, 0, DateTimeKind.Unspecified), parsed.Value.Start);
	}

	[Fact]
	public void ParseRejectsAnUnknownOption()
	{
		var parsed = SeederOptions.Parse(["--connection", Connection, "--end", EndText, "--layers", "4"]);

		AssertFailedWith(parsed, "--layers");
	}

	// The seeding parser knows nothing of --follow: the entry point routes on it before either parser
	// runs, so this is what keeps the two option sets from drifting into one.
	[Fact]
	public void ParseRejectsFollowAsAnUnknownOption()
	{
		var parsed = SeederOptions.Parse(["--connection", Connection, "--end", EndText, "--follow", "1"]);

		AssertFailedWith(parsed, "--follow");
	}

	[Fact]
	public void ParseRejectsAPositionalArgument()
	{
		var parsed = SeederOptions.Parse(["seed-it", "--connection", Connection, "--end", EndText]);

		AssertFailedWith(parsed, "seed-it");
	}

	[Fact]
	public void ParseRejectsAnOptionWithoutAValue()
	{
		var parsed = SeederOptions.Parse(["--connection", Connection, "--end"]);

		AssertFailedWith(parsed, "requires a value");
	}

	[Fact]
	public void ParseRejectsARepeatedOption()
	{
		var parsed = SeederOptions.Parse(["--connection", Connection, "--connection", Connection, "--end", EndText]);

		AssertFailedWith(parsed, "more than once");
	}

	[Theory]
	[InlineData("--days", "many")]
	[InlineData("--pens", "8.5")]
	[InlineData("--seed", "one")]
	[InlineData("--change-seconds", "fast")]
	[InlineData("--break-count", "few")]
	public void ParseRejectsANonNumericValue(string option, string value)
	{
		var parsed = SeederOptions.Parse(["--connection", Connection, "--end", EndText, option, value]);

		AssertFailedWith(parsed, value);
	}

	[Fact]
	public void ParseRejectsAMissingConnection()
	{
		var parsed = SeederOptions.Parse(["--end", EndText]);

		AssertFailedWith(parsed, "--connection");
	}

	[Fact]
	public void ParseRejectsAnEmptyConnection()
	{
		var parsed = SeederOptions.Parse(["--connection", "", "--end", EndText]);

		AssertFailedWith(parsed, "--connection");
	}

	[Fact]
	public void ParseRejectsAMissingEnd()
	{
		var parsed = SeederOptions.Parse(["--connection", Connection]);

		AssertFailedWith(parsed, "--end");
	}

	[Fact]
	public void ParseRejectsAnUnparsableEnd()
	{
		var parsed = SeederOptions.Parse(["--connection", Connection, "--end", "the-second-of-january"]);

		AssertFailedWith(parsed, "--end");
	}

	// The archive column is 'timestamp without time zone', so a bound carrying one must not be
	// silently reinterpreted.
	[Theory]
	[InlineData("2026-01-02T00:00:00Z")]
	[InlineData("2026-01-02T00:00:00+03:00")]
	public void ParseRejectsAnEndCarryingATimeZone(string value)
	{
		var parsed = SeederOptions.Parse(["--connection", Connection, "--end", value]);

		AssertFailedWith(parsed, "time zone");
	}

	// A break needs up to 10 minutes of downtime with 5 minutes of archiving on either side, so a day
	// holds at most 72.
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
	public void ParseRejectsAValueOutsideItsRange(string option, string value)
	{
		var parsed = SeederOptions.Parse(["--connection", Connection, "--end", EndText, option, value]);

		AssertFailedWith(parsed, option);
	}

	[Fact]
	public void ParseAcceptsTheLargestBreakCountTheSpanHolds()
	{
		var parsed = SeederOptions.Parse(["--connection", Connection, "--end", EndText, "--break-count", "72"]);

		Assert.True(parsed.IsSuccess);
	}

	// PartitionScript.CoveredDays steps one day past the last day it covers, and Program reaches it from
	// ReportPlan, after the block it wraps in an ArgumentException catch. The refusal belongs here, where
	// a mistyped --end is still a usage line rather than a stack trace out of the partition walk.
	[Fact]
	public void ParseRejectsAnEndInsideTheLastRepresentableDay()
	{
		var parsed = SeederOptions.Parse(["--connection", Connection, "--end", "9999-12-31T23:59:59"]);

		AssertFailedWith(parsed, "--end");
	}

	[Fact]
	public void ParseAcceptsTheLatestEndThatCanBePartitioned()
	{
		var parsed = SeederOptions.Parse(["--connection", Connection, "--end", "9999-12-31T00:00:00"]);

		Assert.True(parsed.IsSuccess);
		Assert.NotEmpty(PartitionScript.CoveredDays(parsed.Value.Start, parsed.Value.End));
	}

	// The span is the ceiling on --change-seconds, and the ceiling itself is inside it.
	[Fact]
	public void ParseAcceptsAChangeIntervalAsLongAsTheSpan()
	{
		var parsed = SeederOptions.Parse(
			["--connection", Connection, "--end", EndText, "--change-seconds", "86400"]);

		Assert.True(parsed.IsSuccess);
	}

	// The value taken for an option must not be the next option: swallowing it would report the failure
	// against a later, innocent token instead of against the option that was left empty.
	[Fact]
	public void ParseRejectsAnOptionWhoseValueIsItselfAnOption()
	{
		var parsed = SeederOptions.Parse(["--connection", "--days", "3", "--end", EndText]);

		AssertFailedWith(parsed, "--connection");
		AssertFailedWith(parsed, "--days");
	}

	[Fact]
	public void ParseAcceptsZeroBreaks()
	{
		var parsed = SeederOptions.Parse(["--connection", Connection, "--end", EndText, "--break-count", "0"]);

		Assert.True(parsed.IsSuccess);
		Assert.Equal(0, parsed.Value.BreakCount);
	}

	[Fact]
	public void ParseReportsEveryNumericFailureAtOnce()
	{
		var parsed = SeederOptions.Parse(
			["--connection", Connection, "--end", EndText, "--days", "many", "--pens", "lots"]);

		Assert.True(parsed.IsFailed);
		Assert.Equal(2, parsed.Errors.Count);
	}

	private static void AssertFailedWith<TValue>(Result<TValue> parsed, string expectedFragment)
	{
		Assert.True(parsed.IsFailed);
		Assert.Contains(parsed.Errors, error => error.Message.Contains(expectedFragment, StringComparison.Ordinal));
	}
}
