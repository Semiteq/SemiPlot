using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data;

// The parse-failure path of the entry point: exit 1, the reason, and the usage block. It is the only
// branch of Main that touches no database, and it is the branch a mistyped command line takes.
[Collection(ProcessStateCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class SeederEntryPointTests
{
	[Fact]
	public async Task AnUnknownOptionExitsWithOneAndPrintsTheUsage()
	{
		var reported = await RunAsync(["--bogus"]);

		Assert.Equal(1, reported.ExitCode);
		Assert.Contains("--bogus", reported.Error, StringComparison.Ordinal);
		Assert.Contains("Usage:", reported.Error, StringComparison.Ordinal);
	}

	// --follow in the raw arguments selects the demo writer ahead of either parser, so its rejection has
	// to arrive with the demo writer's own usage rather than with the seeding one.
	[Fact]
	public async Task AFollowRunWithoutACadenceExitsWithOneAndPrintsTheFollowUsage()
	{
		var reported = await RunAsync(["--connection", BenchOptions.ConnectionString, "--follow"]);

		Assert.Equal(1, reported.ExitCode);
		Assert.Contains("requires a value", reported.Error, StringComparison.Ordinal);
		Assert.Contains("thins them into the coarse layers", reported.Error, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AFollowRunCarryingASeedingOptionExitsWithOneAndPrintsTheFollowUsage()
	{
		var reported = await RunAsync([
			"--connection", BenchOptions.ConnectionString,
			"--follow", "1",
			"--end", BenchOptions.EndText]);

		Assert.Equal(1, reported.ExitCode);
		Assert.Contains("--end", reported.Error, StringComparison.Ordinal);
		Assert.Contains("thins them into the coarse layers", reported.Error, StringComparison.Ordinal);
	}

	// A day count no span can hold underflows SeederOptions.Start inside Validate, before any check can
	// return a Result. The entry point catches the class rather than the input: an operator who mistyped
	// a number gets the usage block, not a stack trace.
	[Fact]
	public async Task ADayCountTheSpanCannotHoldExitsWithOneAndPrintsTheUsage()
	{
		var reported = await RunAsync(
			["--connection", BenchOptions.ConnectionString, "--end", BenchOptions.EndText, "--days", "1000000"]);

		Assert.Equal(1, reported.ExitCode);
		Assert.Contains("Usage:", reported.Error, StringComparison.Ordinal);
	}

	private static async Task<(int ExitCode, string Error)> RunAsync(string[] arguments)
	{
		var captured = new StringWriter();
		var previous = Console.Error;

		try
		{
			Console.SetError(captured);

			return (await Program.Main(arguments), captured.ToString());
		}
		finally
		{
			Console.SetError(previous);
		}
	}
}
