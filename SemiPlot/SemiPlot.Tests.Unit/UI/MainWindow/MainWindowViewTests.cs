using System.Diagnostics;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.Tests.Unit.UI.Settings;
using SemiPlot.UI.Legend;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Settings;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

/// <summary>The realised window: which rows stand, and which flag each of them follows.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class MainWindowViewTests
{
	private static readonly TimeSpan _dialogTimeout = TimeSpan.FromSeconds(30);

	// Every row of the window, not only the one the failure names: this window has no chart and no services,
	// so a row that defaults to visible renders empty chrome over the one text the operator needs.
	[AvaloniaFact]
	public void MainWindow_WithAStartupFailure_ShowsTheFailureAndNothingElseBelowTheChart()
	{
		var failure = new ArchiveFailureView(
			"No connection to the archive",
			"SemiPlot could not open a connection to 'semiplot' at scada-host:5432.",
			"Check that the PostgreSQL server is running.",
			MessageSeverity.Error);
		using var viewModel = NewViewModel(configDirectory: null);
		viewModel.StartupFailure = failure;
		var window = new SemiPlot.UI.MainWindow.MainWindow { DataContext = viewModel };

		window.Show();
		Dispatcher.UIThread.RunJobs();

		ReadText(window, "StartupFailureTitle").Should().Be(failure.Title);
		ReadText(window, "StartupFailureDetail").Should().Be(failure.Detail);
		ReadText(window, "StartupFailureRemedy").Should().Be(failure.Remedy);
		RowVisibility(window, "StartupFailurePanel").Should().BeTrue();
		RowVisibility(window, "StatusBar").Should().BeFalse("the failure row speaks for the connection");
		RowVisibility(window, "MessagePanel").Should()
			.BeFalse("nothing has reported into this window's panel, so it carries no row");
		EmptyCatalogueMessage(window).IsVisible.Should()
			.BeFalse("there is no chart on this path, so there is no empty catalogue either");
	}

	[AvaloniaFact]
	public void TheStartupFailureWindowWithNoDirectory_CarriesTheSettingsItemDisabled()
	{
		using var viewModel = NewViewModel(configDirectory: null);
		viewModel.StartupFailure = new ArchiveFailureView("t", "d", "r", MessageSeverity.Error);
		var window = new SemiPlot.UI.MainWindow.MainWindow { DataContext = viewModel };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		var item = MenuItemNamed(window, "EditSettings");

		item.Command.Should().BeSameAs(viewModel.ShowSettingsCommand);
		item.IsEffectivelyEnabled.Should().BeFalse("there is no directory to read the settings from");
	}

	[AvaloniaTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task EditSettings_ClickedOnTheRealisedWindow_OpensTheDialogOverIt(bool startupFailure)
	{
		var configDirectory = CopyShippedConfiguration();
		try
		{
			using var viewModel = NewViewModel(configDirectory);
			if (startupFailure)
			{
				viewModel.StartupFailure = new ArchiveFailureView("t", "d", "r", MessageSeverity.Error);
			}

			var window = new SemiPlot.UI.MainWindow.MainWindow { DataContext = viewModel };
			window.Show();
			Dispatcher.UIThread.RunJobs();

			Click(window, MenuItemNamed(window, "EditMenu"));
			var settingsItem = MenuItemNamed(window, "EditSettings");
			Click(
				TopLevel.GetTopLevel(settingsItem)
					?? throw new InvalidOperationException("The Edit menu opened no popup."),
				settingsItem);
			await WaitUntil(() => window.OwnedWindows.OfType<SettingsDialog>().Any());

			var dialog = window.OwnedWindows.OfType<SettingsDialog>().Single();
			var settings = dialog.DataContext.Should().BeOfType<SettingsViewModel>().Which;
			settings.Host.Should().Be("127.0.0.1");
			dialog.FindControl<TextBox>("SettingsHost")!.Text.Should().Be("127.0.0.1");
			viewModel.MessagePanel.Entries.Should().BeEmpty();

			dialog.Close();
			Dispatcher.UIThread.RunJobs();

			window.OwnedWindows.Should().BeEmpty();
			// A disposed command stops following its inputs, and the shipped blank password left it disabled.
			settings.Password = "secret";
			((ICommand)settings.SaveCommand).CanExecute(null).Should()
				.BeFalse("the window disposes the view model its dialog showed");
			window.Close();
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	// The row binds the same flag the View menu writes and reads back, and a failure opens it: an entry that
	// landed off screen would be a failure the operator is never shown.
	[AvaloniaFact]
	public void MessagePanelRow_StartsClosedAndOpensOnTheFirstFailure()
	{
		using var panel = new MessagePanelViewModel();
		using var viewModel = new MainWindowViewModel(
			panel, NewStatusBar(panel), AppContext.BaseDirectory, NullLoggerFactory.Instance);
		var window = new SemiPlot.UI.MainWindow.MainWindow { DataContext = viewModel };
		window.Show();
		var row = window.FindControl<Border>("MessagePanel");

		row.Should().NotBeNull();
		row!.IsVisible.Should().BeFalse("a session that has not failed is not given the row");

		panel.Report(new ArchiveFailureView("Archive unreachable", "detail", "remedy", MessageSeverity.Warning));
		Dispatcher.UIThread.RunJobs();

		row.IsVisible.Should().BeTrue();

		panel.ToggleCommand.Execute().Subscribe();
		Dispatcher.UIThread.RunJobs();

		row.IsVisible.Should().BeFalse("the operator closed the panel");
	}

	// Read off the realised window: a binding to a valid but wrong property compiles and leaves the row
	// standing, which only the row itself can tell.
	[AvaloniaFact]
	public void EveryViewMenuRow_FollowsItsOwnFlagOnTheRealisedWindow()
	{
		using var viewModel = NewViewModel();
		var window = new SemiPlot.UI.MainWindow.MainWindow { DataContext = viewModel };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		var rows = new (string Name, Action Toggle)[]
		{
			("NavigationBar", () => viewModel.ToggleNavigationBarCommand.Execute().Subscribe()),
			("LegendPanel", () => viewModel.ToggleLegendCommand.Execute().Subscribe()),
			("MinimapRow", () => viewModel.ToggleMinimapCommand.Execute().Subscribe())
		};

		foreach (var (name, toggle) in rows)
		{
			var row = window.FindControl<Border>(name);
			row.Should().NotBeNull("'{0}' is a named row of the window", name);
			row!.IsVisible.Should().BeTrue("'{0}' starts on screen", name);

			toggle();
			Dispatcher.UIThread.RunJobs();

			row.IsVisible.Should().BeFalse("'{0}' follows the flag its command wrote", name);

			toggle();
			Dispatcher.UIThread.RunJobs();

			row.IsVisible.Should().BeTrue("'{0}' comes back", name);
		}
	}

	// The width comes from a view model the window does not hold until a chart is built, so the fallback
	// is what a failed startup renders.
	[AvaloniaFact]
	public void LegendPanelWidth_ReadsTheFallbackAndThenFollowsThePanelState()
	{
		using var viewModel = NewViewModel();
		var window = new SemiPlot.UI.MainWindow.MainWindow { DataContext = viewModel };
		var panel = window.FindControl<Border>("LegendPanel");

		window.Show();
		Dispatcher.UIThread.RunJobs();

		panel.Should().NotBeNull("'LegendPanel' is a named row of the window");
		panel!.Width.Should().Be(
			TrendLegendViewModel.ExpandedWidth,
			"no chart is built yet, so the border reads its fallback");
		panel.Bounds.Width.Should().Be(TrendLegendViewModel.ExpandedWidth);
		ResizeHandle(window).IsVisible.Should().BeFalse("a window with no legend has nothing to resize");

		viewModel.SetChart(CreateChartWithPens());
		Dispatcher.UIThread.RunJobs();

		panel.Width.Should().Be(TrendLegendViewModel.ExpandedWidth, "the panel opens expanded");
		ResizeHandle(window).IsVisible.Should().BeTrue();

		viewModel.LegendViewModel!.ToggleExpandedCommand.Execute().Subscribe();
		Dispatcher.UIThread.RunJobs();

		panel.Width.Should().Be(TrendLegendViewModel.CollapsedWidth);
	}

	// The handle sits on the panel's left edge, so a drag to the left widens the panel.
	[AvaloniaFact]
	public void ADragOnTheHandle_ResizesThePanelInBothStatesAndEachStateKeepsItsWidth()
	{
		using var viewModel = NewViewModel();
		var window = new SemiPlot.UI.MainWindow.MainWindow { DataContext = viewModel };
		window.Show();
		viewModel.SetChart(CreateChartWithPens());
		Dispatcher.UIThread.RunJobs();
		var panel = window.FindControl<Border>("LegendPanel")!;
		var handle = ResizeHandle(window);

		Drag(window, handle, -60);

		panel.Bounds.Width.Should().Be(TrendLegendViewModel.ExpandedWidth + 60);

		viewModel.LegendViewModel!.ToggleExpandedCommand.Execute().Subscribe();
		Dispatcher.UIThread.RunJobs();

		panel.Bounds.Width.Should().Be(TrendLegendViewModel.CollapsedWidth);

		Drag(window, handle, 30);

		panel.Bounds.Width.Should().Be(TrendLegendViewModel.CollapsedWidth - 30);

		viewModel.LegendViewModel.ToggleExpandedCommand.Execute().Subscribe();
		Dispatcher.UIThread.RunJobs();

		panel.Bounds.Width.Should().Be(
			TrendLegendViewModel.ExpandedWidth + 60,
			"expanding restores the width the expanded state last had");
	}

	[AvaloniaFact]
	public void ADragOnTheHandle_StopsAtThePanelFloorAndAtTheChartFloor()
	{
		using var viewModel = NewViewModel();
		var window = new SemiPlot.UI.MainWindow.MainWindow { DataContext = viewModel };
		window.Show();
		viewModel.SetChart(CreateChartWithPens());
		Dispatcher.UIThread.RunJobs();
		var panel = window.FindControl<Border>("LegendPanel")!;
		var chart = window.FindControl<Border>("ChartContent")!;
		var handle = ResizeHandle(window);

		Drag(window, handle, 5000);

		panel.Bounds.Width.Should().Be(TrendLegendViewModel.PanelMinWidth);

		Drag(window, handle, -5000);

		chart.Bounds.Width.Should().Be(TrendLegendViewModel.ChartMinWidth);
	}

	[AvaloniaFact]
	public void ShrinkingTheWindowAfterADrag_KeepsTheChartFloorAndGrowingItBackRestoresThePanel()
	{
		using var viewModel = NewViewModel();
		var window = new SemiPlot.UI.MainWindow.MainWindow { DataContext = viewModel };
		window.Show();
		viewModel.SetChart(CreateChartWithPens());
		Dispatcher.UIThread.RunJobs();
		var panel = window.FindControl<Border>("LegendPanel")!;
		var chart = window.FindControl<Border>("ChartContent")!;
		var initialWindowWidth = window.Width;
		Drag(window, ResizeHandle(window), -400);

		window.Width = 800;
		Dispatcher.UIThread.RunJobs();

		chart.Bounds.Width.Should().Be(TrendLegendViewModel.ChartMinWidth);

		window.Width = initialWindowWidth;
		Dispatcher.UIThread.RunJobs();

		panel.Bounds.Width.Should().Be(TrendLegendViewModel.ExpandedWidth + 400);
	}

	[AvaloniaFact]
	public void ALegendAssignedToANarrowWindow_KeepsTheChartFloorBeforeAnyDragOrResize()
	{
		using var viewModel = NewViewModel();
		var window = new SemiPlot.UI.MainWindow.MainWindow { DataContext = viewModel, Width = 500 };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		var chart = window.FindControl<Border>("ChartContent")!;

		viewModel.SetChart(CreateChartWithPens());
		Dispatcher.UIThread.RunJobs();

		chart.Bounds.Width.Should().Be(TrendLegendViewModel.ChartMinWidth);
	}

	[AvaloniaFact]
	public void HidingTheLegend_GivesTheChartTheWholeRow()
	{
		using var viewModel = NewViewModel();
		var window = new SemiPlot.UI.MainWindow.MainWindow { DataContext = viewModel };
		window.Show();
		viewModel.SetChart(CreateChartWithPens());
		Dispatcher.UIThread.RunJobs();
		var chart = window.FindControl<Border>("ChartContent")!;
		var contentGrid = window.FindControl<Grid>("ContentGrid")!;

		viewModel.ToggleLegendCommand.Execute().Subscribe();
		Dispatcher.UIThread.RunJobs();

		ResizeHandle(window).IsVisible.Should().BeFalse();
		chart.Bounds.Width.Should().Be(contentGrid.Bounds.Width);
	}

	private static Thumb ResizeHandle(Window window)
	{
		var handle = window.FindControl<Thumb>("PanelResizeHandle");
		handle.Should().NotBeNull("'PanelResizeHandle' is a named control of the window");

		return handle;
	}

	// Pressed at the handle's centre and moved in two steps: the window hit-tests the press, so a handle
	// that draws nothing never receives it, and the second step shows the deltas add up.
	private static void Drag(Window window, Thumb handle, double horizontalDelta)
	{
		var start = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), window)
			?? throw new InvalidOperationException("The handle is not in the window's visual tree.");
		var halfway = start + new Point(horizontalDelta / 2, 0);
		var end = start + new Point(horizontalDelta, 0);

		window.MouseDown(start, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();
		window.MouseMove(halfway, RawInputModifiers.LeftMouseButton);
		Dispatcher.UIThread.RunJobs();
		window.MouseMove(end, RawInputModifiers.LeftMouseButton);
		Dispatcher.UIThread.RunJobs();
		window.MouseUp(end, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();
	}

	private static MenuItem MenuItemNamed(Window window, string name)
	{
		var item = window.FindControl<AppMenuBar>("MenuBar")!.FindControl<MenuItem>(name);
		item.Should().NotBeNull("'{0}' is a named item of the menu bar", name);

		return item;
	}

	private static void Click(TopLevel topLevel, Control control)
	{
		var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), topLevel)
			?? throw new InvalidOperationException("The control is not in the top level's visual tree.");

		topLevel.MouseDown(center, MouseButton.Left);
		topLevel.MouseUp(center, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();
	}

	private static async Task WaitUntil(Func<bool> condition)
	{
		var clock = Stopwatch.StartNew();

		while (!condition())
		{
			if (clock.Elapsed > _dialogTimeout)
			{
				throw new TimeoutException("The settings dialog did not open.");
			}

			await Task.Delay(10);
			Dispatcher.UIThread.RunJobs();
		}
	}

	private static string CopyShippedConfiguration()
	{
		var configDirectory = Directory.CreateTempSubdirectory("semiplot-main-window-settings-").FullName;
		ShippedConfiguration.CopyTo(configDirectory);

		return configDirectory;
	}

	private static string? ReadText(Window window, string name)
	{
		return window.FindControl<TextBlock>(name)?.Text;
	}

	private static bool RowVisibility(Window window, string name)
	{
		var row = window.FindControl<Border>(name);
		row.Should().NotBeNull("'{0}' is a named row of the window", name);

		return row.IsVisible;
	}

	// The chart view carries its own name scope, so the message is reached through the visual tree.
	private static TextBlock EmptyCatalogueMessage(Window window)
	{
		return window
			.GetVisualDescendants()
			.OfType<TextBlock>()
			.Single(block => block.Name == "EmptyCatalogueMessage");
	}
}
