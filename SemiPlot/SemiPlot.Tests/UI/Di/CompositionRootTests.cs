using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.DataSource.Stub;
using SemiPlot.UI;
using SemiPlot.UI.MainWindow;

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

		var viewModel = provider.GetRequiredService<MainWindowViewModel>();

		viewModel.Should().NotBeNull();
		viewModel.PenCount.Should().BeGreaterThan(0);
	}

	[AvaloniaFact]
	public void Container_ResolvesMainWindowViewModel_UnderHeadlessHarness()
	{
		using var provider = BuildContainer();

		var viewModel = provider.GetRequiredService<MainWindowViewModel>();

		viewModel.Should().NotBeNull();
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
