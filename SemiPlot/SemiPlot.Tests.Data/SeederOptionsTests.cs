using System.Globalization;

using FluentResults;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class SeederOptionsTests
{
	private const string Connection = "Host=localhost;Database=archive;Username=scada_writer";
	private const string EndText = "2026-01-02T00:00:00";

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
	// holds at most 72. Every bound here is a rejected option with usage rather than an exception out of
	// Parse. The upper ones are the ones arithmetic would otherwise reach first: --days past 2026-01-02
	// minus DateTime.MinValue overflows the subtraction behind Start, and a --change-seconds far above
	// the span overflows the interval arithmetic in the generator.
	[Theory]
	[InlineData("--days", "0")]
	[InlineData("--days", "-1")]
	[InlineData("--days", "1000000")]
	[InlineData("--days", "20000000")]
	[InlineData("--pens", "0")]
	[InlineData("--pens", "51")]
	[InlineData("--change-seconds", "0")]
	[InlineData("--change-seconds", "-1")]
	[InlineData("--change-seconds", "NaN")]
	[InlineData("--change-seconds", "Infinity")]
	[InlineData("--change-seconds", "86401")]
	[InlineData("--change-seconds", "1e18")]
	[InlineData("--break-count", "-1")]
	[InlineData("--break-count", "73")]
	[InlineData("--break-count", "200")]
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

	// The span is the ceiling on --change-seconds, and the ceiling itself is inside it.
	[Fact]
	public void ParseAcceptsAChangeIntervalAsLongAsTheSpan()
	{
		var parsed = SeederOptions.Parse(["--connection", Connection, "--end", EndText, "--change-seconds", "86400"]);

		Assert.True(parsed.IsSuccess);
	}

	// A span reaching back to the earliest representable timestamp is still a span the parser accepts;
	// only one reaching past it is refused.
	[Fact]
	public void ParseAcceptsTheLongestSpanTheEndAllows()
	{
		var days = (int)(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified) - DateTime.MinValue).TotalDays;

		var parsed = SeederOptions.Parse(
			["--connection", Connection, "--end", EndText, "--days", days.ToString(CultureInfo.InvariantCulture)]);

		Assert.True(parsed.IsSuccess);
		Assert.True(parsed.Value.Start >= DateTime.MinValue);
	}

	// The end itself has an upper bound, because the day it falls in has to be given a partition and a
	// partition's upper bound is midnight of the day after it. Past that the arithmetic behind the
	// partition list throws, and at the default --change-seconds too, so no other option can be blamed.
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
	}

	// The same bound, on an end that is not midnight. 2026-01-01T23:59:59.9999999 sits one tick under a
	// whole number of days from DateTime.MinValue, and TotalDays — a rounded double — lands exactly on
	// that whole number, so a bound taken from it is one day too generous and Start reaches past the
	// earliest representable timestamp. The midnight end above cannot catch this: there the division is
	// exact.
	[Fact]
	public void ParseRejectsTheDayPastTheLongestSpanANonMidnightEndAllows()
	{
		var parsed = SeederOptions.Parse(
			["--connection", Connection, "--end", "2026-01-01T23:59:59.9999999", "--days", "739617"]);

		AssertFailedWith(parsed, "--days");
	}

	[Fact]
	public void ParseAcceptsTheLongestSpanANonMidnightEndAllows()
	{
		var parsed = SeederOptions.Parse(
			["--connection", Connection, "--end", "2026-01-01T23:59:59.9999999", "--days", "739616"]);

		Assert.True(parsed.IsSuccess);
		Assert.True(parsed.Value.Start >= DateTime.MinValue);
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
