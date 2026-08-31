using Xunit;

namespace SemiPlot.Tests.Data.Integration;

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
