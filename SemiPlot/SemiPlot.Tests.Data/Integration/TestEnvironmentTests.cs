using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// SEMIPLOT_REQUIRE_DB decides whether an unavailable runtime is a skip or a failure, so it is the switch
// behind the whole availability policy: were it to read false by accident, the CI job would report the
// gated tests as skipped and stay green while proving nothing. DatabaseGateTests passes the flag as a
// literal, which leaves the variable-to-bool mapping itself unasserted — this is where it is asserted.
[Collection(ProcessStateCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class TestEnvironmentTests
{
	[Theory]
	[InlineData("1", true)]
	[InlineData("true", true)]
	[InlineData("TRUE", true)]
	[InlineData("  true  ", true)]
	[InlineData("0", false)]
	[InlineData("", false)]
	[InlineData("   ", false)]
	[InlineData("yes", false)]
	[InlineData(null, false)]
	public void TheRequireDatabaseVariableDecidesTheAvailabilityPolicy(string? value, bool required)
	{
		var previous = Environment.GetEnvironmentVariable(TestEnvironment.RequireDatabaseVariable);

		try
		{
			Environment.SetEnvironmentVariable(TestEnvironment.RequireDatabaseVariable, value);

			Assert.Equal(required, TestEnvironment.DatabaseRequired);
		}
		finally
		{
			Environment.SetEnvironmentVariable(TestEnvironment.RequireDatabaseVariable, previous);
		}
	}
}
