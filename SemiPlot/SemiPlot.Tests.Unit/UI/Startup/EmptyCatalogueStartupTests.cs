using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using ReactiveUI.Avalonia;

using SemiPlot.UI.Chart;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Startup;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Startup;

/// <summary>
/// The empty pen catalogue, pinned as a state of its own: an unfinished commissioning answers correctly,
/// so startup runs to completion and <see cref="TrendChartViewModel.HasNoPens"/> tells it apart from
/// a broken chart, which otherwise renders the same blank plot.
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class EmptyCatalogueStartupTests
{
	[AvaloniaFact]
	public async Task EmptyCatalogue_StartsNormallyAndReportsTheState()
	{
		using var stand = NewArchiveStand([]);
		var container = stand.Data.ServiceProvider;

		var probe = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		probe.IsSuccess.Should().BeTrue();
		probe.Errors.Should().BeEmpty();
		probe.Value.Pens.Should().BeEmpty();

		using var window = TrendWindow.Build(
			probe.Value, TestLaunch.LauncherAt(AppContext.BaseDirectory), AvaloniaScheduler.Instance);
		var mainWindowViewModel = window.ViewModel;

		mainWindowViewModel.ChartViewModel.Pens.Should().BeEmpty();
		mainWindowViewModel.ChartViewModel.HasNoPens.Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task PopulatedCatalogue_StartsWithTheEmptyCatalogueStateOff()
	{
		using var stand = NewArchiveStand();
		var dataProvider = stand.Provider;
		var container = stand.Data.ServiceProvider;

		var probe = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		using var window = TrendWindow.Build(
			probe.Value, TestLaunch.LauncherAt(AppContext.BaseDirectory), AvaloniaScheduler.Instance);
		var mainWindowViewModel = window.ViewModel;

		mainWindowViewModel.ChartViewModel.Pens.Should().HaveCount(dataProvider.Pens.Count);
		mainWindowViewModel.ChartViewModel.HasNoPens.Should().BeFalse();
	}
}
