using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// One gate for both runtimes. A missing container runtime and a missing semibase binary are the same
// condition to a gated test: an unavailable reason with a stated cause, never a pass. A gated test
// that quietly succeeded without a database would assert nothing at all.
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
