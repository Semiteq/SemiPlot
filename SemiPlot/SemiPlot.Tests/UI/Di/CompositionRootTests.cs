using System.Reactive.Concurrency;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.DataSource.Postgres;
using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.DataSource.Stub;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Minimap;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.UI.Di;

// Both containers are the ones StartupProbe builds, not look-alikes assembled here. Resolving the graph
// constructs objects and opens no connection, so the archive container needs no server: its settings
// point at an address nothing answers.
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class CompositionRootTests
{
	[Fact]
	public void DefaultContainer_ResolvesThePostgresProvider()
	{
		using var provider = StartupProbe.BuildArchiveServiceProvider(UnreachableSettings());

		provider.GetRequiredService<IDataProvider>().Should().BeOfType<PostgresDataProvider>();
	}

	[Fact]
	public void StubContainer_ResolvesTheStubProvider()
	{
		using var provider = StartupProbe.BuildStubServiceProvider();

		provider.GetRequiredService<IDataProvider>().Should().BeOfType<RandomStubDataProvider>();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Container_ResolvesDataProvider(bool useStub)
	{
		using var provider = BuildContainer(useStub);

		provider.GetRequiredService<IDataProvider>().Should().NotBeNull();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Container_ResolvesDataScheduler(bool useStub)
	{
		using var provider = BuildContainer(useStub);

		provider.GetRequiredService<IScheduler>().Should().NotBeNull();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Container_ResolvesLogging(bool useStub)
	{
		using var provider = BuildContainer(useStub);

		provider.GetRequiredService<ILogger<MainWindowViewModel>>().Should().NotBeNull();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Container_ResolvesMainWindowViewModel(bool useStub)
	{
		using var provider = BuildContainer(useStub);

		provider.GetRequiredService<MainWindowViewModel>().Should().NotBeNull();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Container_ResolvesChartFactory(bool useStub)
	{
		using var provider = BuildContainer(useStub);

		provider
			.GetRequiredService<Func<TrendCoordinator, IScheduler, TrendChartViewModel>>()
			.Should().NotBeNull();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Container_ResolvesMinimapFactory(bool useStub)
	{
		using var provider = BuildContainer(useStub);

		provider
			.GetRequiredService<Func<TrendCoordinator, ChartNavigationController, IScheduler, MinimapViewModel>>()
			.Should().NotBeNull();
	}

	private static ServiceProvider BuildContainer(bool useStub)
	{
		return useStub
			? StartupProbe.BuildStubServiceProvider()
			: StartupProbe.BuildArchiveServiceProvider(UnreachableSettings());
	}

	// A local copy of the SemiPlot.Tests.Data equivalent rather than a reference between two test
	// projects: port 1 answers nowhere, and nothing here issues a read.
	private static PostgresConnectionSettings UnreachableSettings()
	{
		return new PostgresConnectionSettings(
			FileVersion: PostgresConnectionLoader.SupportedFileVersion,
			Host: "127.0.0.1",
			Port: 1,
			Database: "semiplot_dev",
			Username: "semiplot_reader",
			Password: "unused",
			SourceTimeZone: TimeZoneInfo.Utc,
			PollInterval: TimeSpan.FromSeconds(1),
			Schema: "public");
	}
}
