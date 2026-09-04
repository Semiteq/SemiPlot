using AwesomeAssertions;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Unit;

// The command line is exercised through RunAsync, the only entry point: a run that parses reaches one of
// the two delegates with its options, a run that does not prints its reasons and exits 1.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class SeederCommandTests
{
	private const string Connection = BenchOptions.ConnectionString;
	private const string EndText = BenchOptions.EndText;

	[Fact]
	public async Task ASeedingRunAppliesDefaultsWhenOnlyRequiredOptionsAreGiven()
	{
		var run = await RunAsync(["--connection", Connection, "--end", EndText]);

		run.ExitCode.Should().Be(0);
		run.Follow.Should().BeNull();

		var options = run.Seed.Should().BeOfType<SeederOptions>().Which;

		options.ConnectionString.Should().Be(Connection);
		options.End.Should().Be(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified));
		options.Days.Should().Be(SeederOptions.DefaultDays);
		options.PenCount.Should().Be(SeederOptions.DefaultPenCount);
		options.Seed.Should().Be(SeederOptions.DefaultSeed);
		options.ChangeSeconds.Should().Be(SeederOptions.DefaultChangeSeconds);
		options.BreakCount.Should().Be(SeederOptions.DefaultBreakCount);
		options.AdminConnectionString.Should().BeNull();
		options.Start.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));
	}

	[Fact]
	public async Task ASeedingRunAcceptsEveryParameter()
	{
		var run = await RunAsync(
		[
			"--connection", Connection,
			"--admin-connection", "Host=localhost;Database=archive;Username=postgres",
			"--days", "3",
			"--pens", "12",
			"--seed", "77",
			"--change-seconds", "2.5",
			"--end", EndText
		]);

		run.ExitCode.Should().Be(0);

		var options = run.Seed.Should().BeOfType<SeederOptions>().Which;

		options.AdminConnectionString.Should().Be("Host=localhost;Database=archive;Username=postgres");
		options.Days.Should().Be(3);
		options.PenCount.Should().Be(12);
		options.Seed.Should().Be(77L);
		options.ChangeSeconds.Should().Be(2.5);
	}

	[Fact]
	public async Task AFollowRunAppliesDefaultsWhenOnlyRequiredOptionsAreGiven()
	{
		var run = await RunAsync(["--connection", Connection, "--follow", "1"]);

		run.ExitCode.Should().Be(0);
		run.Seed.Should().BeNull();

		var options = run.Follow.Should().BeOfType<FollowOptions>().Which;

		options.ConnectionString.Should().Be(Connection);
		options.Interval.Should().Be(TimeSpan.FromSeconds(1));
		options.PenCount.Should().Be(SeederOptions.DefaultPenCount);
		options.Seed.Should().Be(SeederOptions.DefaultSeed);
		options.ChangeSeconds.Should().Be(SeederOptions.DefaultChangeSeconds);
	}

	[Fact]
	public async Task AFollowRunAcceptsEveryParameter()
	{
		var run = await RunAsync(
			["--connection", Connection, "--follow", "0.5", "--pens", "12", "--seed", "77", "--change-seconds", "2.5"]);

		run.ExitCode.Should().Be(0);

		var options = run.Follow.Should().BeOfType<FollowOptions>().Which;

		options.Interval.Should().Be(TimeSpan.FromMilliseconds(500));
		options.PenCount.Should().Be(12);
		options.Seed.Should().Be(77L);
		options.ChangeSeconds.Should().Be(2.5);
	}

	// A follow run appends to an archive somebody else seeded. Each of these states a span or a tag
	// catalogue, neither of which it has, so each is answered rather than silently ignored.
	[Theory]
	[InlineData("--end", "2026-01-02T00:00:00")]
	[InlineData("--days", "3")]
	[InlineData("--admin-connection", "Host=localhost;Database=archive;Username=postgres")]
	public async Task AFollowRunRejectsASeedingOption(string option, string value)
	{
		var run = await RunAsync(["--connection", Connection, "--follow", "1", option, value]);

		AssertRejectedWith(run, option);
		AssertRejectedWith(run, "seeding run");
	}

	[Theory]
	[InlineData("--days", "many")]
	[InlineData("--pens", "8.5")]
	[InlineData("--seed", "one")]
	[InlineData("--change-seconds", "fast")]
	public async Task ANonNumericValueIsRejectedNamingTheValue(string option, string value)
	{
		var run = await RunAsync(["--connection", Connection, "--end", EndText, option, value]);

		AssertRejectedWith(run, value);
	}

	[Fact]
	public async Task AMissingOrBlankConnectionIsRejected()
	{
		AssertRejectedWith(await RunAsync(["--end", EndText]), "--connection");
		AssertRejectedWith(await RunAsync(["--connection", "", "--end", EndText]), "--connection");
	}

	[Fact]
	public async Task ASeedingRunWithoutAnEndIsRejected()
	{
		AssertRejectedWith(await RunAsync(["--connection", Connection]), "--end");
	}

	// The archive column is 'timestamp without time zone', so a bound carrying one must not be silently
	// reinterpreted, and PartitionScript.CoveredDays cannot walk past the last representable day.
	[Theory]
	[InlineData("the-second-of-january")]
	[InlineData("2026-01-02T00:00:00Z")]
	[InlineData("2026-01-02T00:00:00+03:00")]
	[InlineData("9999-12-31T23:59:59")]
	public async Task AnEndThatCannotBoundTheSpanIsRejected(string value)
	{
		AssertRejectedWith(await RunAsync(["--connection", Connection, "--end", value]), "--end");
	}

	[Fact]
	public async Task TheLatestEndThatCanBePartitionedIsAccepted()
	{
		var run = await RunAsync(["--connection", Connection, "--end", "9999-12-31T00:00:00"]);

		run.ExitCode.Should().Be(0);
		run.Seed.Should().NotBeNull();
		PartitionScript.CoveredDays(run.Seed.Start, run.Seed.End).Should().NotBeEmpty();
	}

	// The span is the ceiling on the change interval.
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
	public async Task ASeedingValueOutsideItsRangeIsRejected(string option, string value)
	{
		var run = await RunAsync(["--connection", Connection, "--end", EndText, option, value]);

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
	public async Task AFollowValueOutsideItsRangeIsRejected(string option, string value)
	{
		string[] required = option == "--follow"
			? ["--connection", Connection]
			: ["--connection", Connection, "--follow", "1"];

		AssertRejectedWith(await RunAsync([.. required, option, value]), option);
	}

	[Fact]
	public async Task AChangeIntervalAsWideAsTheSpanIsAccepted()
	{
		var run = await RunAsync(["--connection", Connection, "--end", EndText, "--change-seconds", "86400"]);

		run.ExitCode.Should().Be(0);
		run.Seed.Should().NotBeNull();
	}

	[Fact]
	public async Task EveryFailureIsReportedAtOnce()
	{
		var run = await RunAsync(["--connection", Connection, "--end", EndText, "--days", "many", "--pens", "lots"]);

		AssertRejectedWith(run, "many");
		AssertRejectedWith(run, "lots");
	}

	[Fact]
	public async Task AParseFailurePointsAtHelpAndExitsWithOne()
	{
		var run = await RunAsync(["--bogus"]);

		run.ExitCode.Should().Be(1);
		AssertRejectedWith(run, "--bogus");
		AssertRejectedWith(run, "--help");
	}

	[Fact]
	public async Task AConvergeRunAppliesDefaultsWhenOnlyRequiredOptionsAreGiven()
	{
		var run = await RunAsync(
			["converge", "--connection", Connection, "--admin-connection", "Host=localhost;Database=postgres", "--config-dir", "C:\\config"]);

		run.ExitCode.Should().Be(0);
		run.Seed.Should().BeNull();
		run.Follow.Should().BeNull();

		var options = run.Converge.Should().BeOfType<ConvergeOptions>().Which;

		options.ConnectionString.Should().Be(Connection);
		options.AdminConnectionString.Should().Be("Host=localhost;Database=postgres");
		options.ConfigDirectory.Should().Be("C:\\config");
		options.End.Should().BeNull();
		options.ChangeSeconds.Should().Be(SeederOptions.DefaultChangeSeconds);
	}

	[Fact]
	public async Task AConvergeRunAcceptsAChangeInterval()
	{
		var run = await RunAsync(
		[
			"converge", "--connection", Connection, "--admin-connection", "Host=localhost;Database=postgres",
			"--config-dir", "C:\\config", "--change-seconds", "0.5"
		]);

		run.ExitCode.Should().Be(0);
		run.Converge.Should().BeOfType<ConvergeOptions>().Which.ChangeSeconds.Should().Be(0.5);
	}

	[Fact]
	public async Task AConvergeRunAcceptsAnEnd()
	{
		var run = await RunAsync(
		[
			"converge", "--connection", Connection, "--admin-connection", "Host=localhost;Database=postgres",
			"--config-dir", "C:\\config", "--end", EndText
		]);

		run.ExitCode.Should().Be(0);

		var options = run.Converge.Should().BeOfType<ConvergeOptions>().Which;

		options.End.Should().Be(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified));
	}

	[Theory]
	[InlineData("--connection")]
	[InlineData("--admin-connection")]
	[InlineData("--config-dir")]
	public async Task AConvergeRunRejectsAMissingRequiredOption(string missing)
	{
		(string Flag, string Value)[] every =
		[
			("--connection", Connection),
			("--admin-connection", "Host=localhost;Database=postgres"),
			("--config-dir", "C:\\config")
		];

		var arguments = new List<string> { "converge" };

		foreach (var (flag, value) in every)
		{
			if (flag != missing)
			{
				arguments.Add(flag);
				arguments.Add(value);
			}
		}

		AssertRejectedWith(await RunAsync([.. arguments]), missing);
	}

	[Theory]
	[InlineData("--connection")]
	[InlineData("--admin-connection")]
	[InlineData("--config-dir")]
	public async Task AConvergeRunRejectsABlankRequiredOption(string blank)
	{
		(string Flag, string Value)[] every =
		[
			("--connection", Connection),
			("--admin-connection", "Host=localhost;Database=postgres"),
			("--config-dir", "C:\\config")
		];

		var arguments = new List<string> { "converge" };

		foreach (var (flag, value) in every)
		{
			arguments.Add(flag);
			arguments.Add(flag == blank ? "" : value);
		}

		AssertRejectedWith(await RunAsync([.. arguments]), blank);
	}

	private static void AssertRejectedWith(SeederOutcome run, string expectedFragment)
	{
		run.Seed.Should().BeNull();
		run.Follow.Should().BeNull();
		run.Converge.Should().BeNull();
		run.ExitCode.Should().Be(1);
		run.Error.Should().Contain(expectedFragment);
	}

	// Console.Error is process-wide, and no other class in this project redirects it; xunit runs the methods
	// of one class one at a time, so the capture never overlaps itself.
	private static async Task<SeederOutcome> RunAsync(string[] arguments)
	{
		SeederOptions? seeded = null;
		FollowOptions? followed = null;
		ConvergeOptions? converged = null;
		var captured = new StringWriter();
		var previous = Console.Error;

		try
		{
			Console.SetError(captured);

			var exitCode = await SeederCommand.RunAsync(
				arguments,
				options =>
				{
					seeded = options;

					return Task.FromResult(0);
				},
				options =>
				{
					followed = options;

					return Task.FromResult(0);
				},
				options =>
				{
					converged = options;

					return Task.FromResult(0);
				});

			return new SeederOutcome(seeded, followed, converged, exitCode, captured.ToString());
		}
		finally
		{
			Console.SetError(previous);
		}
	}

	private sealed record SeederOutcome(
		SeederOptions? Seed,
		FollowOptions? Follow,
		ConvergeOptions? Converge,
		int ExitCode,
		string Error);
}
