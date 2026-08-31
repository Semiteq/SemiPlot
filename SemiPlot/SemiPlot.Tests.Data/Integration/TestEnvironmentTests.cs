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
	[InlineData("  TRUE  ", true)]
	[InlineData(null, false)]
	[InlineData("   ", false)]
	[InlineData("yes", false)]
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

	// The same Read governs the image, and there the blank rejection is what stops a run being pointed at
	// an empty image name: the fallback has to be the default rather than "".
	[Theory]
	[InlineData(null, TestEnvironment.DefaultImage)]
	[InlineData("   ", TestEnvironment.DefaultImage)]
	[InlineData("  postgres:16-alpine  ", "postgres:16-alpine")]
	public void TheImageVariableSelectsTheBaseImageAndFallsBackToTheDefault(string? value, string expected)
	{
		var previous = Environment.GetEnvironmentVariable(TestEnvironment.ImageVariable);

		try
		{
			Environment.SetEnvironmentVariable(TestEnvironment.ImageVariable, value);

			Assert.Equal(expected, TestEnvironment.Image);
		}
		finally
		{
			Environment.SetEnvironmentVariable(TestEnvironment.ImageVariable, previous);
		}
	}
}
