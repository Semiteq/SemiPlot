using System.Reactive.Concurrency;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tests.Unit.Postgres;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Di;

// The container is the one StartupProbe builds, not a look-alike assembled here. Resolving the graph
// constructs objects and opens no connection, so it needs no server: its settings point at an address
// nothing answers.
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class CompositionRootTests
{
	[Fact]
	public void DefaultContainer_ResolvesThePostgresProvider()
	{
		using var provider = BuildContainer();

		provider.GetRequiredService<IDataProvider>().Should().BeOfType<PostgresDataProvider>();
	}

	[Fact]
	public void Container_ResolvesDataProvider()
	{
		using var provider = BuildContainer();

		provider.GetRequiredService<IDataProvider>().Should().NotBeNull();
	}

	[Fact]
	public void Container_ResolvesDataScheduler()
	{
		using var provider = BuildContainer();

		provider.GetRequiredService<IScheduler>().Should().NotBeNull();
	}

	[Fact]
	public void Container_ResolvesLogging()
	{
		using var provider = BuildContainer();

		provider.GetRequiredService<ILogger<MainWindowViewModel>>().Should().NotBeNull();
	}

	[Fact]
	public void Container_ResolvesMainWindowViewModel()
	{
		using var provider = BuildContainer();

		provider.GetRequiredService<MainWindowViewModel>().Should().NotBeNull();
	}

	private static ServiceProvider BuildContainer()
	{
		return StartupProbe.BuildArchiveServiceProvider(ConnectionSettingsFactory.Create());
	}
}
