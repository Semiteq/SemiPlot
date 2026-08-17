using System.Reactive.Concurrency;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.DataSource.Stub;
using SemiPlot.UI;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Minimap;

using Xunit;

namespace SemiPlot.Tests.UI.Di;

[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class CompositionRootTests
{
	[Fact]
	public void Container_ResolvesDataProvider()
	{
		using var provider = BuildContainer();

		provider.GetRequiredService<IDataProvider>().Should().NotBeNull();
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

	[Fact]
	public void Container_ResolvesChartFactory()
	{
		using var provider = BuildContainer();

		provider
			.GetRequiredService<Func<TrendCoordinator, IScheduler, TrendChartViewModel>>()
			.Should().NotBeNull();
	}

	[Fact]
	public void Container_ResolvesMinimapFactory()
	{
		using var provider = BuildContainer();

		provider
			.GetRequiredService<Func<TrendCoordinator, ChartNavigationController, IScheduler, MinimapViewModel>>()
			.Should().NotBeNull();
	}

	private static ServiceProvider BuildContainer()
	{
		var services =
			new ServiceCollection()
				.AddData()
				.AddUi();

		services.AddLogging();

		return services.BuildServiceProvider();
	}
}
