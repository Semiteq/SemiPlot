using FluentResults;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data;

// The demo writer's own option type. It exists so that --end stays unconditionally required on the
// seeding path, so these cases are as much about what it refuses as about what it accepts.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class FollowOptionsTests
{
	private const string Connection = "Host=localhost;Database=archive;Username=scada_writer";

	[Fact]
	public void ParseAppliesDefaultsWhenOnlyRequiredOptionsAreGiven()
	{
		var parsed = FollowOptions.Parse(["--connection", Connection, "--follow", "1"]);

		Assert.True(parsed.IsSuccess);

		var options = parsed.Value;

		Assert.Equal(Connection, options.ConnectionString);
		Assert.Equal(TimeSpan.FromSeconds(1), options.Interval);
		Assert.Equal(SeederOptions.DefaultPenCount, options.PenCount);
		Assert.Equal(SeederOptions.DefaultSeed, options.Seed);
		Assert.Equal(SeederOptions.DefaultChangeSeconds, options.ChangeSeconds);
	}

	[Fact]
	public void ParseAcceptsEveryParameter()
	{
		var parsed = FollowOptions.Parse(
		[
			"--connection", Connection,
			"--follow", "0.5",
			"--pens", "12",
			"--seed", "77",
			"--change-seconds", "2.5"
		]);

		Assert.True(parsed.IsSuccess);

		var options = parsed.Value;

		Assert.Equal(TimeSpan.FromMilliseconds(500), options.Interval);
		Assert.Equal(12, options.PenCount);
		Assert.Equal(77L, options.Seed);
		Assert.Equal(2.5, options.ChangeSeconds);
	}

	// The mode is decided from the raw argument list, ahead of either parser, so a seeding run reaches
	// SeederOptions.Parse on exactly the path it reaches it on today.
	[Fact]
	public void FollowIsRequestedOnlyWhenTheSwitchIsPresent()
	{
		Assert.True(FollowOptions.IsRequested(["--connection", Connection, "--follow", "1"]));
		Assert.False(FollowOptions.IsRequested(["--connection", Connection, "--end", "2026-01-02T00:00:00"]));
	}

	// A follow run appends to an archive somebody else seeded. Each of these states a span, a break plan
	// or a tag catalogue, none of which it has, so each is answered with what it does instead of being
	// silently ignored.
	[Theory]
	[InlineData("--end", "2026-01-02T00:00:00")]
	[InlineData("--days", "3")]
	[InlineData("--break-count", "4")]
	[InlineData("--admin-connection", "Host=localhost;Database=archive;Username=postgres")]
	public void ParseRejectsASeedingOption(string option, string value)
	{
		var parsed = FollowOptions.Parse(["--connection", Connection, "--follow", "1", option, value]);

		AssertFailedWith(parsed, option);
		AssertFailedWith(parsed, "seeding run");
	}

	[Fact]
	public void ParseRejectsAMissingConnection()
	{
		var parsed = FollowOptions.Parse(["--follow", "1"]);

		AssertFailedWith(parsed, "--connection");
	}

	[Fact]
	public void ParseRejectsAnEmptyConnection()
	{
		var parsed = FollowOptions.Parse(["--connection", "", "--follow", "1"]);

		AssertFailedWith(parsed, "--connection");
	}

	[Fact]
	public void ParseRejectsAMissingFollow()
	{
		var parsed = FollowOptions.Parse(["--connection", Connection]);

		AssertFailedWith(parsed, "--follow");
	}

	[Fact]
	public void ParseRejectsAnUnknownOption()
	{
		var parsed = FollowOptions.Parse(["--connection", Connection, "--follow", "1", "--layers", "4"]);

		AssertFailedWith(parsed, "--layers");
	}

	[Fact]
	public void ParseRejectsAPositionalArgument()
	{
		var parsed = FollowOptions.Parse(["follow-it", "--connection", Connection, "--follow", "1"]);

		AssertFailedWith(parsed, "follow-it");
	}

	[Fact]
	public void ParseRejectsARepeatedOption()
	{
		var parsed = FollowOptions.Parse(["--connection", Connection, "--follow", "1", "--follow", "2"]);

		AssertFailedWith(parsed, "more than once");
	}

	[Theory]
	[InlineData("--follow", "often")]
	[InlineData("--pens", "8.5")]
	[InlineData("--seed", "one")]
	[InlineData("--change-seconds", "fast")]
	public void ParseRejectsANonNumericValue(string option, string value)
	{
		AssertFailedWith(FollowOptions.Parse(Arguments(option, value)), value);
	}

	// NaN fails every comparison, so a bare `<= 0` check lets it through; Infinity and a value far above
	// the ceiling overflow the tick arithmetic behind the generator.
	[Theory]
	[InlineData("--follow", "0")]
	[InlineData("--follow", "-1")]
	[InlineData("--follow", "NaN")]
	[InlineData("--follow", "Infinity")]
	[InlineData("--follow", "86401")]
	[InlineData("--follow", "1e18")]
	[InlineData("--pens", "0")]
	[InlineData("--pens", "51")]
	[InlineData("--change-seconds", "0")]
	[InlineData("--change-seconds", "-1")]
	[InlineData("--change-seconds", "NaN")]
	[InlineData("--change-seconds", "Infinity")]
	[InlineData("--change-seconds", "86401")]
	[InlineData("--change-seconds", "1e18")]
	public void ParseRejectsAValueOutsideItsRange(string option, string value)
	{
		AssertFailedWith(FollowOptions.Parse(Arguments(option, value)), option);
	}

	[Theory]
	[InlineData("--follow", "86400")]
	[InlineData("--change-seconds", "86400")]
	public void ParseAcceptsTheLargestCadenceTheCeilingAllows(string option, string value)
	{
		Assert.True(FollowOptions.Parse(Arguments(option, value)).IsSuccess);
	}

	[Fact]
	public void UsageNamesTheModeAndTheOptionsItRefuses()
	{
		Assert.Contains("--follow", FollowOptions.Usage, StringComparison.Ordinal);
		Assert.Contains("--end", FollowOptions.Usage, StringComparison.Ordinal);
	}

	// --follow carries the cadence and selects the mode at once, so a case exercising it must not also
	// supply the valid one: the repeated-option rule would answer first.
	private static string[] Arguments(string option, string value)
	{
		string[] required = option == "--follow"
			? ["--connection", Connection]
			: ["--connection", Connection, "--follow", "1"];

		return [.. required, option, value];
	}

	private static void AssertFailedWith<TValue>(Result<TValue> parsed, string expectedFragment)
	{
		Assert.True(parsed.IsFailed);
		Assert.Contains(parsed.Errors, error => error.Message.Contains(expectedFragment, StringComparison.Ordinal));
	}
}
