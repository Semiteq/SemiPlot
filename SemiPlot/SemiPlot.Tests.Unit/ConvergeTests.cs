using AwesomeAssertions;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Unit;

// Both refusals below fire before Converge.RunAsync opens a connection, so they need no server.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ConvergeTests
{
	[Fact]
	public async Task AConnectionStringWithNoDatabaseIsRejected()
	{
		var options = new ConvergeOptions("Host=localhost;Username=postgres", "Host=localhost", "C:\\config", null, SeederOptions.DefaultChangeSeconds);

		Func<Task> act = () => Converge.RunAsync(options, TestContext.Current.CancellationToken);

		(await act.Should().ThrowAsync<SeederException>()).Which.Message.Should().Contain("--connection");
	}

	[Theory]
	[InlineData("archive")]
	[InlineData("semiplot_provisioned")]
	public async Task AConnectionNamingANonBenchDatabaseIsRejected(string database)
	{
		var options = new ConvergeOptions(
			$"Host=localhost;Database={database};Username=scada_writer", "Host=localhost", "C:\\config", null, SeederOptions.DefaultChangeSeconds);

		Func<Task> act = () => Converge.RunAsync(options, TestContext.Current.CancellationToken);

		(await act.Should().ThrowAsync<SeederException>()).Which.Message.Should().Contain(database);
	}
}
