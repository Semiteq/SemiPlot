using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// One gate, whatever left the suite without a database. The container path needs a container runtime
// and nothing else; the SEMIPLOT_TEST_PG path needs the server it names and the binary it provisions
// with. Every one of those reaches a gated test as the same thing: an unavailable reason with a stated
// cause, never a pass. A gated test that quietly succeeded without a database would assert nothing at
// all.
public static class DatabaseGate
{
	public static void Require(string? unavailableReason, bool databaseRequired)
	{
		if (unavailableReason is null)
		{
			return;
		}

		if (databaseRequired)
		{
			throw new InvalidOperationException(
				$"{TestEnvironment.RequireDatabaseVariable} is set, so an unavailable runtime fails "
					+ $"instead of skipping: {unavailableReason}");
		}

		Assert.Skip(unavailableReason);
	}
}
