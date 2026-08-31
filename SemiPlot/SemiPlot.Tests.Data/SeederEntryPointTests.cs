using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data;

// The parse-failure path of the entry point: exit 1, the reason and a pointer at --help. It is the only
// branch of Main that touches no database, and it is the branch a mistyped command line takes.
[Collection(ProcessStateCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class SeederEntryPointTests
{
	[Fact]
	public async Task AnUnknownOptionExitsWithOneAndPointsAtHelp()
	{
		var reported = await RunAsync(["--bogus"]);

		Assert.Equal(1, reported.ExitCode);
		Assert.Contains("--bogus", reported.Error, StringComparison.Ordinal);
		Assert.Contains("--help", reported.Error, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AFollowRunCarryingASeedingOptionExitsWithOne()
	{
		var reported = await RunAsync([
			"--connection", BenchOptions.ConnectionString,
			"--follow", "1",
			"--end", BenchOptions.EndText]);

		Assert.Equal(1, reported.ExitCode);
		Assert.Contains("--end", reported.Error, StringComparison.Ordinal);
		Assert.Contains("seeding run", reported.Error, StringComparison.Ordinal);
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
