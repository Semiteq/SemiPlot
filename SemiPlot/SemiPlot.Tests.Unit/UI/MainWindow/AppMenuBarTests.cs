using System.Reactive.Linq;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using ReactiveUI;

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
		using var viewModel = NewViewModel();
		var menuBar = ShowMenuBar(viewModel);

		var menu = menuBar.FindControl<Menu>("MainMenu");
		menu.Should().NotBeNull();

		var leaves = LeavesOf(menu!.Items.OfType<MenuItem>()).ToList();

		leaves
			.Select(leaf => leaf.Name)
			.Should()
			.Contain(["FileExit", "ViewMessagePanel", "HelpAbout"], "the walk descends into every menu");

		foreach (var leaf in leaves)
		{
			leaf.Command.Should().NotBeNull("'{0}' is a menu leaf and has to act", leaf.Name);
		}
	}

	[AvaloniaFact]
	public void NavigationBarItem_TakesItsCheckStateFromTheCommandAndWritesNothingBack()
	{
		using var viewModel = NewViewModel();
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
		using var viewModel = NewViewModel();
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
		using var viewModel = NewViewModel();
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
		using var viewModel = NewViewModel();
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
		using var viewModel = NewViewModel();
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
		using var viewModel = NewViewModel();
		AboutInfo? requested = null;
		using var subscription = viewModel.AboutRequests.Subscribe(about => requested = about);

		viewModel.ShowAboutCommand.Execute().Subscribe();

		requested.Should().NotBeNull();
		requested!.ApplicationName.Should().Be(SemiPlot.UI.Localization.Resources.WindowTitle);
		requested.Version.Should().NotBeNullOrWhiteSpace();
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
