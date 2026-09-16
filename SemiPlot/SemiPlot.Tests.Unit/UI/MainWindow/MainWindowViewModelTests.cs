using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class MainWindowViewModelTests
{
	[AvaloniaFact]
	public void SetChart_BuildsTheNavigationBarAndTheLegend()
	{
		using var viewModel = NewViewModel();
		var chart = CreateChartWithPens();

		viewModel.SetChart(chart);

		viewModel.NavigationBarViewModel.Should().NotBeNull();
		viewModel.LegendViewModel.Should().NotBeNull();
	}

	[AvaloniaFact]
	public void SetChart_WithNull_DropsTheNavigationBarAndTheLegend()
	{
		using var viewModel = NewViewModel();
		viewModel.SetChart(CreateChartWithPens());

		viewModel.SetChart(null);

		viewModel.NavigationBarViewModel.Should().BeNull();
		viewModel.LegendViewModel.Should().BeNull();
	}

	[AvaloniaFact]
	public void SetChart_WithTheSameInstance_KeepsTheChartAlive()
	{
		using var viewModel = NewViewModel();
		var chart = CreateChartWithPens();
		viewModel.SetChart(chart);
		var activePenId = chart.ActivePenId;
		var navigationBar = viewModel.NavigationBarViewModel;

		viewModel.SetChart(chart);

		// Every mutating member throws ObjectDisposedException once the chart is disposed.
		chart.SetActivePen(activePenId).Should().BeTrue();
		viewModel.NavigationBarViewModel.Should().BeSameAs(navigationBar);
	}

	// The status bar outlives the chart, so the bar follows whichever chart is in force rather than being
	// rebuilt with it: a rebuilt bar would lose the connection stream bound once at startup.
	[AvaloniaFact]
	public void SetChart_PointsTheStatusBarAtItsLayer()
	{
		using var panel = new MessagePanelViewModel();
		var statusBar = NewStatusBar(panel);
		using var viewModel = new MainWindowViewModel(
			panel, statusBar, NullLogger<MainWindowViewModel>.Instance);
		var chart = CreateChartWithPens();

		viewModel.SetChart(chart);

		statusBar.ActiveLayer.Should().Be(chart.Navigation.ActiveLayer);
		viewModel.StatusBar.Should().BeSameAs(statusBar);
	}

	[AvaloniaFact]
	public void StartupFailure_WhenSet_MakesThePanelVisibleAndTheChartNull()
	{
		var failure = new ArchiveFailureView(
			"Startup failed",
			"detail",
			"remedy",
			MessageSeverity.Error);

		using var viewModel = NewViewModel();
		viewModel.StartupFailure = failure;

		viewModel.HasStartupFailure.Should().BeTrue();
		viewModel.ChartViewModel.Should().BeNull();
	}

	// The code-behind route: the About dialog's throw cannot escape an async void handler, so it reports
	// through the view model rather than through a logger the view would have to hold itself.
	[AvaloniaFact]
	public void ReportFailure_PutsTheThrowInThePanel()
	{
		using var panel = new MessagePanelViewModel();
		using var viewModel = new MainWindowViewModel(
			panel, NewStatusBar(panel), NullLogger<MainWindowViewModel>.Instance);

		viewModel.ReportFailure(new InvalidOperationException("the dialog refused"));

		panel.Entries.Should().ContainSingle();
		panel.Entries[0].View.Detail.Should().Contain("the dialog refused");
		panel.IsVisible.Should().BeTrue();
	}
}
