using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.UI.Bridge;
using SemiPlot.UI;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.UI.Startup;

/// <summary>
/// The empty pen catalogue, pinned as a state of its own. A provisioned database whose
/// <c>semiplot_tags</c> holds no rows answered correctly — commissioning is unfinished, nothing is
/// broken — so startup must run to completion, build the view-models and raise no failure. The
/// operator-visible half is <see cref="MainWindowViewModel.IsCatalogueEmpty"/>: without it an empty
/// catalogue and a broken chart both render as a blank plot with <c>Pens: 0</c> in a corner.
/// <para>
/// These drive <c>App.InitializeServices</c> itself rather than a rebuilt look-alike, so they cover the
/// startup body the running application executes. That needs Avalonia's scheduler, hence
/// <c>[AvaloniaFact]</c> here and plain <c>[Fact]</c> in <see cref="StartupProbeTests"/>.
/// </para>
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
