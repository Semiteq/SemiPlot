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

	// A break count the span cannot hold is a rejected option with usage, not an
	// ArgumentOutOfRangeException out of BreakPlan.Create reaching the operator as a stack trace.
	[Fact]
	public async Task ABreakCountLargerThanTheSpanHoldsIsRejectedWithTheUsage()
	{
		var reported = await RunAsync([
			"--connection", "Host=localhost;Database=archive",
			"--end", "2026-01-02T00:00:00",
			"--break-count", "200"]);

		Assert.Equal(1, reported.ExitCode);
		Assert.Contains("--break-count", reported.Error, StringComparison.Ordinal);
		Assert.Contains("Usage:", reported.Error, StringComparison.Ordinal);
	}

	// --follow in the raw arguments selects the demo writer ahead of either parser, so its rejection has
	// to arrive with the demo writer's own usage rather than with the seeding one.
	[Fact]
	public async Task AFollowRunWithoutACadenceExitsWithOneAndPrintsTheFollowUsage()
	{
		var reported = await RunAsync(["--connection", "Host=localhost;Database=archive", "--follow"]);

		Assert.Equal(1, reported.ExitCode);
		Assert.Contains("requires a value", reported.Error, StringComparison.Ordinal);
		Assert.Contains("thins them into the coarse layers", reported.Error, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AFollowRunCarryingASeedingOptionExitsWithOneAndPrintsTheFollowUsage()
	{
		var reported = await RunAsync([
			"--connection", "Host=localhost;Database=archive",
			"--follow", "1",
			"--end", "2026-01-02T00:00:00"]);

		Assert.Equal(1, reported.ExitCode);
		Assert.Contains("--end", reported.Error, StringComparison.Ordinal);
		Assert.Contains("thins them into the coarse layers", reported.Error, StringComparison.Ordinal);
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
