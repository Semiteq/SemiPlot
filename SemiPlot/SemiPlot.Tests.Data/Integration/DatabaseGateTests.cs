using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The gate is the whole availability policy, so it is asserted without a container: the reason a test
// was skipped has to reach the report, and a pipeline has to fail on the same reason.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class DatabaseGateTests
{
	private const string Reason = "no container runtime started postgres:17-alpine: dead endpoint";

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void AnAvailableRuntimePassesTheGate(bool databaseRequired)
	{
		DatabaseGate.Require(null, databaseRequired);
	}

	[Fact]
	public void AMissingRuntimeSkipsWithItsStatedReason()
	{
		var exception = Capture(() => DatabaseGate.Require(Reason, databaseRequired: false));

		Assert.NotNull(exception);
		Assert.Equal("SkipException", exception.GetType().Name);
		Assert.Contains(Reason, exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void AMissingRuntimeFailsWhenTheDatabaseIsRequired()
	{
		var exception = Assert.Throws<InvalidOperationException>(
			() => DatabaseGate.Require(Reason, databaseRequired: true));

		Assert.Contains(TestEnvironment.RequireDatabaseVariable, exception.Message, StringComparison.Ordinal);
		Assert.Contains(Reason, exception.Message, StringComparison.Ordinal);
	}

	// Record.Exception rethrows a dynamic skip so that Assert.Skip inside a lambda still skips the test
	// that called it. Asserting on the skip instead of taking it needs the exception caught plainly.
	private static Exception? Capture(Action action)
	{
		try
		{
			action();

			return null;
		}
		catch (Exception exception)
		{
			return exception;
		}
	}
}
