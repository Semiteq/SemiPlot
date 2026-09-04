using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Startup;

/// <summary>
/// The empty pen catalogue, pinned as a state of its own: an unfinished commissioning answers correctly,
/// so startup runs to completion and <see cref="MainWindowViewModel.IsCatalogueEmpty"/> tells it apart from
/// a broken chart, which otherwise renders the same blank plot. Drives <c>App.InitializeServices</c> itself.
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class EmptyCatalogueStartupTests
{
	[AvaloniaFact]
	public async Task EmptyCatalogue_StartsNormallyAndReportsTheState()
	{
		var scheduler = new TestScheduler();
		using var container = BuildContainer(scheduler, NewProvider(scheduler, []));

		var probe = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		probe.IsSuccess.Should().BeTrue();
		probe.Errors.Should().BeEmpty();
		probe.Value.Pens.Should().BeEmpty();

		App.InitializeServices(probe.Value);

		var mainWindowViewModel = container.GetRequiredService<MainWindowViewModel>();

		mainWindowViewModel.ChartViewModel.Should().NotBeNull();
		mainWindowViewModel.ToolbarViewModel.Should().NotBeNull();
		mainWindowViewModel.LegendViewModel.Should().NotBeNull();
		mainWindowViewModel.MinimapViewModel.Should().NotBeNull();
		mainWindowViewModel.ChartViewModel!.Pens.Should().BeEmpty();
		mainWindowViewModel.PenCount.Should().Be(0);
		mainWindowViewModel.IsCatalogueEmpty.Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task PopulatedCatalogue_StartsWithTheEmptyCatalogueStateOff()
	{
		var scheduler = new TestScheduler();
		var dataProvider = NewProvider(scheduler);
		using var container = BuildContainer(scheduler, dataProvider);

		var probe = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		App.InitializeServices(probe.Value);

		var mainWindowViewModel = container.GetRequiredService<MainWindowViewModel>();

		mainWindowViewModel.PenCount.Should().Be(dataProvider.Pens.Count);
		mainWindowViewModel.IsCatalogueEmpty.Should().BeFalse();
	}

	// A TestScheduler, not CurrentThreadScheduler: InitializeServices calls TrendCoordinator.Start, and a
	// recurring realtime subscription on the current thread's trampoline never returns control.
	private static FakeDataProvider NewProvider(TestScheduler scheduler, IReadOnlyList<Pen>? pens = null)
	{
		return new FakeDataProvider(scheduler, TimeSpan.FromSeconds(1), pens);
	}

	private static ServiceProvider BuildContainer(TestScheduler scheduler, IDataProvider dataProvider)
	{
		var services =
			new ServiceCollection()
				.AddSingleton<IScheduler>(scheduler)
				.AddSingleton(dataProvider)
				.AddUi();

		services.AddLogging();

		return services.BuildServiceProvider();
	}
}
