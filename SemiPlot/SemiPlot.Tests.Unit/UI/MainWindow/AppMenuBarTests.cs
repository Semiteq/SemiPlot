using System.Reactive.Linq;
using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using ReactiveUI;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Localization;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

using MainWindowView = SemiPlot.UI.MainWindow.MainWindow;
using RxUnit = System.Reactive.Unit;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class AppMenuBarTests
{
	// The submenu containers materialise only once a menu opens, so the walk reads the declared items.
	[AvaloniaFact]
	public void EveryMenuLeaf_CarriesACommand()
	{
		using var stand = NewWindowStand();
		var viewModel = stand.ViewModel;
		var menuBar = ShowMenuBar(viewModel);

		var menu = menuBar.FindControl<Menu>("MainMenu");
		menu.Should().NotBeNull();

		var leaves = LeavesOf(menu!.Items.OfType<MenuItem>()).ToList();

		leaves
			.Select(leaf => leaf.Name)
			.Should()
			.Contain(
				[
					"FileNewWindow", "FileExit", "EditSettings", "EditPensAndGroups", "ViewAutoscale",
					"ViewInitialScale", "ViewMessagePanel", "HelpAbout"
				],
				"the walk descends into every menu");

		foreach (var leaf in leaves)
		{
			leaf.Command.Should().NotBeNull("'{0}' is a menu leaf and has to act", leaf.Name);
		}
	}

	[AvaloniaFact]
	public void FileMenu_HoldsTheNewWindowItemThenTheExitItemEachBoundToItsCommand()
	{
		using var stand = NewWindowStand();
		var viewModel = stand.ViewModel;
		var menuBar = ShowMenuBar(viewModel);

		var fileMenu = menuBar.FindControl<MenuItem>("FileMenu");
		fileMenu.Should().NotBeNull();

		var items = fileMenu!.Items.OfType<MenuItem>().ToList();
		items.Select(item => item.Name).Should().Equal("FileNewWindow", "FileExit");
		items[0].Command.Should().BeSameAs(viewModel.NewWindowCommand);
		items[1].Command.Should().BeSameAs(viewModel.ExitCommand);
	}

	[AvaloniaFact]
	public void EditMenu_HoldsTheSettingsItemThenThePenEditorItemEachBoundToItsCommand()
	{
		using var stand = NewWindowStand();
		var viewModel = stand.ViewModel;
		var menuBar = ShowMenuBar(viewModel);

		var editMenu = menuBar.FindControl<MenuItem>("EditMenu");
		editMenu.Should().NotBeNull();

		var items = editMenu!.Items.OfType<MenuItem>().ToList();
		items.Select(item => item.Name).Should().Equal("EditSettings", "EditPensAndGroups");
		items[0].Command.Should().BeSameAs(viewModel.ShowSettingsCommand);
		items[1].Command.Should().BeSameAs(viewModel.ShowPenEditorCommand);
	}

	[AvaloniaFact]
	public void NavigationBarItem_TakesItsCheckStateFromTheCommandAndWritesNothingBack()
	{
		using var stand = NewWindowStand();
		var viewModel = stand.ViewModel;
		var menuBar = ShowMenuBar(viewModel);

		AssertTheCommandIsTheOnlyWriter(
			menuBar,
			"ViewNavigationBar",
			viewModel.ToggleNavigationBarCommand,
			() => viewModel.IsNavigationBarVisible);
	}

	[AvaloniaFact]
	public void LegendItem_TakesItsCheckStateFromTheCommandAndWritesNothingBack()
	{
		using var stand = NewWindowStand();
		var viewModel = stand.ViewModel;
		var menuBar = ShowMenuBar(viewModel);

		AssertTheCommandIsTheOnlyWriter(
			menuBar,
			"ViewLegend",
			viewModel.ToggleLegendCommand,
			() => viewModel.IsLegendVisible);
	}

	[AvaloniaFact]
	public void MinimapItem_TakesItsCheckStateFromTheCommandAndWritesNothingBack()
	{
		using var stand = NewWindowStand();
		var viewModel = stand.ViewModel;
		var menuBar = ShowMenuBar(viewModel);

		AssertTheCommandIsTheOnlyWriter(
			menuBar,
			"ViewMinimap",
			viewModel.ToggleMinimapCommand,
			() => viewModel.IsMinimapVisible);
	}

	// The item writes IsVisible and reads IsVisible back, through the real window so the row it claims to
	// describe is the one asserted on: a readback of a derived property would let one click hide the panel
	// with the item still unticked and nothing on screen having moved.
	[AvaloniaFact]
	public void MessagePanelItem_ChecksExactlyWhenTheRowIsOnScreen()
	{
		using var stand = NewWindowStand();
		var viewModel = stand.ViewModel;
		var window = new MainWindowView { DataContext = viewModel };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		var item = window.FindControl<AppMenuBar>("MenuBar")!.FindControl<MenuItem>("ViewMessagePanel");
		var row = window.FindControl<Border>("MessagePanel");

		item.Should().NotBeNull();
		row.Should().NotBeNull();
		item!.IsChecked.Should().BeFalse("a session that has not failed is not given the row");
		row!.IsVisible.Should().BeFalse();

		viewModel.MessagePanel.ToggleCommand.Execute().Subscribe();
		Dispatcher.UIThread.RunJobs();

		viewModel.MessagePanel.IsVisible.Should().BeTrue();
		row.IsVisible.Should().BeTrue("one click on an empty panel still moves what is on screen");
		item.IsChecked.Should().BeTrue();

		viewModel.MessagePanel.ToggleCommand.Execute().Subscribe();
		viewModel.MessagePanel.Report(new ArchiveFailureView("t", "d", "r", MessageSeverity.Error));
		Dispatcher.UIThread.RunJobs();

		row.IsVisible.Should().BeTrue("a failure opens the row it has to land on");
		item.IsChecked.Should().BeTrue("the check state still matches the row");

		item.IsChecked = false;
		Dispatcher.UIThread.RunJobs();

		viewModel.MessagePanel.IsVisible.Should().BeTrue("the item is not a writer of the panel's state");
	}

	// Through the real window, so the OnLoaded wiring from ExitRequests to Close() is what the test reads.
	[AvaloniaFact]
	public void ExitItem_ClosesTheWindow()
	{
		using var stand = NewWindowStand();
		var viewModel = stand.ViewModel;
		var window = new MainWindowView { DataContext = viewModel };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		window.IsVisible.Should().BeTrue();

		viewModel.ExitCommand.Execute().Subscribe();
		Dispatcher.UIThread.RunJobs();

		window.IsVisible.Should().BeFalse("ExitRequests reaches the window's Close");
	}

	[AvaloniaFact]
	public void AboutItem_AsksForADialogCarryingTheRunsIdentity()
	{
		using var stand = NewWindowStand();
		var viewModel = stand.ViewModel;
		AboutInfo? requested = null;
		using var subscription = viewModel.AboutRequests.Subscribe(about => requested = about);

		viewModel.ShowAboutCommand.Execute().Subscribe();

		requested.Should().NotBeNull();
		requested!.ApplicationName.Should().Be(Resources.WindowTitle);
		requested.Version.Should().NotBeNullOrWhiteSpace();
	}

	[AvaloniaFact]
	public void AutoscaleItem_RevertsTheActivePensAxisToAuto()
	{
		var stored = StoredPairPens(5.0, 50.0);
		using var stand = NewWindowStand(stored);
		var viewModel = stand.ViewModel;
		var menuBar = ShowMenuBar(viewModel);
		var chart = viewModel.ChartViewModel;
		chart.SetAxisLimits(chart.ActivePenId, 10.0, 90.0);

		menuBar.FindControl<MenuItem>("ViewAutoscale")!.Command.Should().BeSameAs(viewModel.AutoscaleCommand);
		viewModel.AutoscaleCommand.Execute().Subscribe();
		Dispatcher.UIThread.RunJobs();

		chart.ScaleSettings[chart.ActivePenId].Mode.Should().Be(ScaleMode.Auto);
	}

	[AvaloniaFact]
	public void InitialScaleItem_SetsTheActivePensAxisToItsStoredPair()
	{
		var stored = StoredPairPens(5.0, 50.0);
		using var stand = NewWindowStand(stored);
		var viewModel = stand.ViewModel;
		var menuBar = ShowMenuBar(viewModel);
		var chart = viewModel.ChartViewModel;
		chart.SetAxisLimits(chart.ActivePenId, 10.0, 90.0);

		menuBar.FindControl<MenuItem>("ViewInitialScale")!.Command.Should().BeSameAs(viewModel.InitialScaleCommand);
		viewModel.InitialScaleCommand.Execute().Subscribe();
		Dispatcher.UIThread.RunJobs();

		chart.ScaleSettings[chart.ActivePenId].Should().Be(new PenScaleSettings(chart.ActivePenId)
		{
			Mode = ScaleMode.Manual,
			ManualMin = 5.0,
			ManualMax = 50.0
		});
	}

	[AvaloniaFact]
	public void ViewMenu_HoldsThePenScaleSubmenuBetweenTheTogglesAndTheMessagePanel()
	{
		using var stand = NewWindowStand([]);
		var viewModel = stand.ViewModel;
		var menuBar = ShowMenuBar(viewModel);

		var viewMenu = menuBar.FindControl<MenuItem>("ViewMenu");
		viewMenu.Should().NotBeNull();

		viewMenu!.Items.OfType<Control>()
			.Select(item => item is Separator ? "-" : item.Name)
			.Should()
			.Equal(
				"ViewNavigationBar", "ViewLegend", "ViewMinimap", "-", "ViewPenScale", "-", "ViewMessagePanel");
		viewMenu.Items.OfType<MenuItem>().Single(item => item.Name == "ViewPenScale").Items.OfType<MenuItem>()
			.Select(item => item.Name)
			.Should()
			.Equal("ViewAutoscale", "ViewInitialScale");
	}

	[AvaloniaFact]
	public void PenScaleSubmenu_NamesTheActivePenInItsHeader()
	{
		using var stand = NewWindowStand();
		var viewModel = stand.ViewModel;
		var menuBar = ShowMenuBar(viewModel);
		var chart = viewModel.ChartViewModel;
		var submenu = menuBar.FindControl<MenuItem>("ViewPenScale")!;

		submenu.Header.Should().Be(Resources.FormatMenuViewPenScaleFormat("Pen 1"));

		chart.SetActivePen(2);
		Dispatcher.UIThread.RunJobs();

		submenu.Header.Should().Be(Resources.FormatMenuViewPenScaleFormat("Pen 2"));
	}

	[AvaloniaFact]
	public void PenScaleSubmenu_WithNoActivePen_ReadsPenScale()
	{
		using var stand = NewWindowStand([]);
		var menuBar = ShowMenuBar(stand.ViewModel);

		menuBar.FindControl<MenuItem>("ViewPenScale")!.Header.Should().Be(Resources.MenuViewPenScale);
	}

	[AvaloniaFact]
	public void PenScaleSubmenu_NamesNoPenWhileNoPensAxisIsDrawn()
	{
		using var stand = NewWindowStand();
		var chart = stand.ViewModel.ChartViewModel;
		var menuBar = ShowMenuBar(stand.ViewModel);
		var submenu = menuBar.FindControl<MenuItem>("ViewPenScale")!;

		chart.SetPenVisibility(1, false);
		chart.SetPenVisibility(2, false);
		Dispatcher.UIThread.RunJobs();

		submenu.Header.Should().Be(Resources.MenuViewPenScale);

		chart.SetPenVisibility(2, true);
		Dispatcher.UIThread.RunJobs();

		submenu.Header.Should().Be(Resources.FormatMenuViewPenScaleFormat("Pen 2"));
	}

	[AvaloniaFact]
	public void PenScaleSubmenu_FollowsARenamedPen()
	{
		using var stand = NewWindowStand();
		var chart = stand.ViewModel.ChartViewModel;
		var menuBar = ShowMenuBar(stand.ViewModel);

		chart.ApplyCatalogue([.. chart.Catalogue.Select(pen => pen with { Name = pen.Name + " renamed" })]);
		Dispatcher.UIThread.RunJobs();

		menuBar.FindControl<MenuItem>("ViewPenScale")!.Header.Should().Be(
			Resources.FormatMenuViewPenScaleFormat("Pen 1 renamed"));
	}

	[AvaloniaFact]
	public void TheAxisCommands_AreAlwaysExecutable()
	{
		using var stand = NewWindowStand([]);
		var viewModel = stand.ViewModel;

		((ICommand)viewModel.AutoscaleCommand).CanExecute(null).Should().BeTrue();
		((ICommand)viewModel.InitialScaleCommand).CanExecute(null).Should().BeTrue();
	}

	private static List<Pen> StoredPairPens(double min, double max)
	{
		return
		[
			new Pen(1, "Pen 1", ["Group A"], "#ff0000", ScaleMinOnStart: min, ScaleMaxOnStart: max),
			new Pen(2, "Pen 2", ["Group A"], "#00ff00", ScaleMinOnStart: min, ScaleMaxOnStart: max)
		];
	}

	private static void AssertTheCommandIsTheOnlyWriter(
		AppMenuBar menuBar,
		string itemName,
		ReactiveCommand<RxUnit, RxUnit> command,
		Func<bool> flag)
	{
		var item = menuBar.FindControl<MenuItem>(itemName);
		item.Should().NotBeNull();
		item.IsChecked.Should().BeTrue("the item starts on the flag");

		command.Execute().Subscribe();
		Dispatcher.UIThread.RunJobs();

		flag().Should().BeFalse();
		item.IsChecked.Should().BeFalse("the binding follows the flag the command wrote");

		item.IsChecked = true;
		Dispatcher.UIThread.RunJobs();

		flag().Should().BeFalse("the item is not a writer of the flag");
	}

	private static IEnumerable<MenuItem> LeavesOf(IEnumerable<MenuItem> items)
	{
		foreach (var item in items)
		{
			var children = item.Items.OfType<MenuItem>().ToList();

			if (children.Count == 0)
			{
				yield return item;

				continue;
			}

			foreach (var leaf in LeavesOf(children))
			{
				yield return leaf;
			}
		}
	}

	private static AppMenuBar ShowMenuBar(MainWindowViewModel viewModel)
	{
		var menuBar = new AppMenuBar();
		var window = new Window { Content = menuBar, DataContext = viewModel };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		return menuBar;
	}
}
