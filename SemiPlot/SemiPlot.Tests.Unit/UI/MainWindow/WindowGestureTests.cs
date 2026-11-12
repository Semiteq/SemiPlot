using System.Reactive.Linq;
using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using FluentResults;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.UI.Chart;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Navigation;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

using MainWindowView = SemiPlot.UI.MainWindow.MainWindow;
using RxUnit = System.Reactive.Unit;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

/// <summary>The window's navigation gestures and the Go menu's marker list, on the realised window.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class WindowGestureTests : IDisposable
{
	// The chart window sits mid-archive, one hour wide, so every step has a target on both sides.
	private static readonly DateTime _windowFromUtc = new(2025, 12, 28, 12, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _centreUtc = _windowFromUtc.AddMinutes(30);
	private static readonly StoredMarker _earlierRunStart = Recipe(1, -180, MarkerKind.RecipeStart);
	private static readonly StoredMarker _earlierRunEnd = Recipe(2, -170, MarkerKind.RecipeEnd);
	private static readonly StoredMarker _laterRunStart = Recipe(3, 10, MarkerKind.RecipeStart);
	private static readonly StoredMarker _laterRunEnd = Recipe(4, 20, MarkerKind.RecipeEnd);
	private static readonly StoredMarker _earlierMarker = new(5, _centreUtc.AddMinutes(-90), "Purge");
	private static readonly StoredMarker _laterMarker = new(6, _centreUtc.AddMinutes(15), "Etch");

	private readonly WindowStand _stand;
	private readonly MainWindowView _window;

	public WindowGestureTests()
	{
		_stand = NewWindowStand();
		_stand.MarkerStore.ReadResult = Result.Ok<IReadOnlyList<StoredMarker>>(
			[_earlierRunStart, _earlierRunEnd, _earlierMarker, _laterRunStart, _laterMarker, _laterRunEnd]);
		_stand.Scheduler.AdvanceBy(TimeSpan.FromSeconds(5).Ticks);
		_window = ViewProbe.Realise(new MainWindowView { DataContext = _stand.ViewModel });
		Navigation.SetWindow(_windowFromUtc, _windowFromUtc.AddHours(1));
		Dispatcher.UIThread.RunJobs();
	}

	private MainWindowViewModel ViewModel => _stand.ViewModel;

	private ChartNavigationController Navigation => ViewModel.ChartViewModel.Navigation;

	private DateTime CentreUtc => Navigation.From + TimeSpan.FromTicks((Navigation.To - Navigation.From).Ticks / 2);

	public void Dispose()
	{
		ViewProbe.Dismiss(_window);
		_stand.Dispose();
	}

	[AvaloniaTheory]
	[InlineData("PreviousPage")]
	[InlineData("NextPage")]
	[InlineData("PreviousRun")]
	[InlineData("NextRun")]
	[InlineData("LatestRun")]
	[InlineData("PreviousMarker")]
	[InlineData("NextMarker")]
	public void EachStepGesture_RunsItsCommand_WithTheBarAndTheMinimapHidden(string step)
	{
		HideTheBarAndTheMinimap();
		var (key, modifiers, command) = GestureOf(step);
		((ICommand)command).CanExecute(null).Should().BeTrue("the stand gives '{0}' a target", step);
		var runs = 0;
		using var counted = command.Subscribe(_ => runs++);
		var before = (Navigation.From, Navigation.To);

		HeadlessInput.Press(_window, key, modifiers);

		runs.Should().Be(1, "the gesture runs the command the Go menu shows it beside");
		(Navigation.From, Navigation.To).Should().NotBe(before, "the step moves the chart window");
		ShouldShowItsTarget(step);
	}

	[AvaloniaTheory]
	[InlineData("PreviousPage")]
	[InlineData("NextPage")]
	[InlineData("PreviousRun")]
	[InlineData("NextRun")]
	[InlineData("LatestRun")]
	[InlineData("PreviousMarker")]
	[InlineData("NextMarker")]
	public void EachGoStepItem_RunsItsCommandWhenClicked_WithTheBarAndTheMinimapHidden(string step)
	{
		HideTheBarAndTheMinimap();
		var (_, _, command) = GestureOf(step);
		var runs = 0;
		using var counted = command.Subscribe(_ => runs++);
		var before = (Navigation.From, Navigation.To);

		ClickMenuItem(_window, "GoMenu", "Go" + step);
		Dispatcher.UIThread.RunJobs();

		runs.Should().Be(1, "the Go item runs its step with the bar and the minimap hidden");
		(Navigation.From, Navigation.To).Should().NotBe(before, "the step moves the chart window");
		ShouldShowItsTarget(step);
	}

	// The operator's keyboard usually rests on the chart or on a control of the bar or the sidebar.
	[AvaloniaTheory]
	[InlineData("PlotControl")]
	[InlineData("Button")]
	[InlineData("CheckBox")]
	public void AStepGesture_RunsWhileAControlOfTheWindowHasTheKeyboard(string focused)
	{
		var target = focused switch
		{
			"PlotControl" => ViewProbe.Descendant<Control>(_window, "PlotControl"),
			"Button" => ViewProbe
				.Descendants<Button>(ViewProbe.Named<NavigationBarView>(_window, "NavigationBarContent"))
				.First(),
			_ => ViewProbe.Descendants<CheckBox>(ViewProbe.Named<Border>(_window, "LegendPanel"))
				.First(box => box.IsEffectivelyVisible && box.IsEffectivelyEnabled)
		};
		target.Focus();
		Dispatcher.UIThread.RunJobs();
		target.IsFocused.Should().BeTrue();
		var (key, modifiers, command) = GestureOf("NextRun");
		var runs = 0;
		using var counted = command.Subscribe(_ => runs++);

		HeadlessInput.Press(_window, key, modifiers);

		runs.Should().Be(1, "a focused '{0}' takes no step key", focused);
		ShouldShowItsTarget("NextRun");
	}

	[AvaloniaTheory]
	[InlineData(true, false)]
	[InlineData(false, false)]
	[InlineData(true, true)]
	[InlineData(false, true)]
	public void TheMarkerList_OpensFromTheGoMenuOrItsGesture_AndAnItemCentresItsMarker(bool barShown, bool byGesture)
	{
		if (!barShown)
		{
			HideTheBarAndTheMinimap();
		}

		var bar = ViewProbe.Named<NavigationBarView>(_window, "NavigationBarContent");
		var markersButton = bar.FindControl<DropDownButton>("MarkersButton")!;
		var list = (Flyout)markersButton.Flyout!;

		if (byGesture)
		{
			HeadlessInput.Press(_window, PhysicalKey.M, RawInputModifiers.Control);
		}
		else
		{
			ClickMenuItem(_window, "GoMenu", "GoMarkerList");
		}

		list.IsOpen.Should().BeTrue();
		list.Target.Should().BeSameAs(
			barShown ? markersButton : ViewProbe.Named<AppMenuBar>(_window, "MenuBar").GoMenuHeader,
			"the list opens under the Markers button, or under the Go menu while the bar is hidden");
		var rows = EntryRowsOf(list);
		rows.Select(row => ((NavigationListItem)row.DataContext!).Entry.Key)
			.Should().Equal([_laterMarker.Id, _earlierMarker.Id], "the list keeps the bar's rows at either anchor");

		HeadlessInput.Click(TopLevel.GetTopLevel(rows[1])!, rows[1]);

		CentreUtc.Should().Be(_earlierMarker.TimeUtc, "the item centres its marker");
		list.IsOpen.Should().BeFalse();
	}

	[AvaloniaFact]
	public void WithNoOperatorMarker_TheMarkerListCannotOpen()
	{
		using var stand = NewWindowStand();
		var window = ViewProbe.Realise(new MainWindowView { DataContext = stand.ViewModel });
		var item = MenuItemNamed(window, "GoMarkerList");

		((ICommand)stand.ViewModel.ShowMarkerListCommand).CanExecute(null).Should().BeFalse();
		HeadlessInput.Press(window, PhysicalKey.M, RawInputModifiers.Control);

		item.Command.Should().BeSameAs(stand.ViewModel.ShowMarkerListCommand);
		var list = ViewProbe.Named<NavigationBarView>(window, "NavigationBarContent")
			.FindControl<DropDownButton>("MarkersButton")!.Flyout!;
		list.IsOpen.Should().BeFalse();
		ViewProbe.Dismiss(window);
	}

	[AvaloniaTheory]
	[InlineData(true, false)]
	[InlineData(false, false)]
	[InlineData(true, true)]
	[InlineData(false, true)]
	public void TheRecipeRunList_OpensFromTheGoMenuOrItsGesture_AndAnItemFramesItsRun(bool barShown, bool byGesture)
	{
		if (!barShown)
		{
			HideTheBarAndTheMinimap();
		}

		var bar = ViewProbe.Named<NavigationBarView>(_window, "NavigationBarContent");
		var runsButton = bar.FindControl<DropDownButton>("RecipeRunsButton")!;
		var list = (Flyout)runsButton.Flyout!;

		if (byGesture)
		{
			HeadlessInput.Press(_window, PhysicalKey.R, RawInputModifiers.Control);
		}
		else
		{
			ClickMenuItem(_window, "GoMenu", "GoRecipeRunList");
		}

		list.IsOpen.Should().BeTrue();
		list.Target.Should().BeSameAs(
			barShown ? runsButton : ViewProbe.Named<AppMenuBar>(_window, "MenuBar").GoMenuHeader,
			"the list opens under the Recipe run button, or under the Go menu while the bar is hidden");
		var rows = EntryRowsOf(list);
		rows.Select(row => ((NavigationListItem)row.DataContext!).Entry.TimeUtc)
			.Should().Equal([_laterRunStart.TimeUtc, _earlierRunStart.TimeUtc], "the newest run stands first");

		HeadlessInput.Click(TopLevel.GetTopLevel(rows[1])!, rows[1]);

		ShouldFrame(_earlierRunStart, _earlierRunEnd);
		list.IsOpen.Should().BeFalse();
	}

	[AvaloniaFact]
	public void WithNoRecipeRun_TheRunListCannotOpen()
	{
		using var stand = NewWindowStand();
		var window = ViewProbe.Realise(new MainWindowView { DataContext = stand.ViewModel });
		var item = MenuItemNamed(window, "GoRecipeRunList");

		((ICommand)stand.ViewModel.ShowRecipeRunListCommand).CanExecute(null).Should().BeFalse();
		HeadlessInput.Press(window, PhysicalKey.R, RawInputModifiers.Control);

		item.Command.Should().BeSameAs(stand.ViewModel.ShowRecipeRunListCommand);
		item.IsEffectivelyEnabled.Should().BeFalse();
		var list = ViewProbe.Named<NavigationBarView>(window, "NavigationBarContent")
			.FindControl<DropDownButton>("RecipeRunsButton")!.Flyout!;
		list.IsOpen.Should().BeFalse();
		ViewProbe.Dismiss(window);
	}

	// A text box keeps every caret key the gestures share with it; the same key outside the box steps.
	[AvaloniaTheory]
	[InlineData("MarkerLabelFlyout", "PreviousRun")]
	[InlineData("MarkerLabelFlyout", "NextRun")]
	[InlineData("MarkerLabelFlyout", "LatestRun")]
	[InlineData("MarkerLabelFlyout", "PreviousMarker")]
	[InlineData("MarkerLabelFlyout", "NextMarker")]
	[InlineData("TimeWindowFlyout", "PreviousRun")]
	[InlineData("TimeWindowFlyout", "NextRun")]
	[InlineData("MarkerListFlyout", "PreviousRun")]
	[InlineData("MarkerListFlyout", "NextRun")]
	[InlineData("MarkerListFlyout", "PreviousMarker")]
	public void ACaretGestureInAFocusedTextBox_MovesTheCaretAndStepsNothing(string flyoutKey, string step)
	{
		var (key, modifiers, command) = GestureOf(step);
		((ICommand)command).CanExecute(null).Should().BeTrue("the stand gives '{0}' a target", step);
		var runs = 0;
		using var counted = command.Subscribe(_ => runs++);
		var (flyout, box) = OpenTextBoxIn(flyoutKey);
		var caretAtStart = key == PhysicalKey.ArrowLeft ? box.Text!.Length : 0;
		box.CaretIndex = caretAtStart;
		Dispatcher.UIThread.RunJobs();
		var before = (Navigation.From, Navigation.To);

		HeadlessInput.Press(TopLevel.GetTopLevel(box)!, key, modifiers);

		box.SelectionEnd.Should().NotBe(caretAtStart, "the text box moves its caret, or the selection end with Shift");
		runs.Should().Be(0);
		(Navigation.From, Navigation.To).Should().Be(before);

		flyout.Hide();
		_window.Focus();
		Dispatcher.UIThread.RunJobs();
		HeadlessInput.Press(_window, key, modifiers);

		runs.Should().Be(1, "outside the text box the same key steps");
	}

	// A key the text box has no use for still never reaches the window from the box.
	[AvaloniaTheory]
	[InlineData("PreviousPage")]
	[InlineData("NextPage")]
	[InlineData("MarkerList")]
	[InlineData("RecipeRunList")]
	public void AGestureTypedIntoATextBox_ThatTheBoxDoesNotUse_StepsNothing(string step)
	{
		var (key, modifiers, command) = GestureOf(step);
		((ICommand)command).CanExecute(null).Should().BeTrue("the stand gives '{0}' a target", step);
		var runs = 0;
		using var counted = command.Subscribe(_ => runs++);
		var (flyout, box) = OpenTextBoxIn("MarkerLabelFlyout");
		var before = (Navigation.From, Navigation.To);

		HeadlessInput.Press(TopLevel.GetTopLevel(box)!, key, modifiers);

		runs.Should().Be(0);
		(Navigation.From, Navigation.To).Should().Be(before);

		flyout.Hide();
		_window.Focus();
		Dispatcher.UIThread.RunJobs();
		HeadlessInput.Press(_window, key, modifiers);

		runs.Should().Be(1, "outside the text box the same key acts");
	}

	private static List<Border> EntryRowsOf(Flyout list)
	{
		return [.. list.Popup.Child!.GetVisualDescendants().OfType<Border>().Where(row => row.Name == "EntryRow")];
	}

	private static StoredMarker Recipe(long id, int minutesFromCentre, MarkerKind kind)
	{
		return new StoredMarker(id, _centreUtc.AddMinutes(minutesFromCentre), null, kind, true);
	}

	private (PhysicalKey Key, RawInputModifiers Modifiers, ReactiveCommand<RxUnit, RxUnit> Command) GestureOf(
		string step)
	{
		var pager = ViewModel.MinimapViewModel.Pager;
		var bar = ViewModel.NavigationBarViewModel;
		const RawInputModifiers ControlShift = RawInputModifiers.Control | RawInputModifiers.Shift;

		return step switch
		{
			"PreviousPage" => (PhysicalKey.ArrowLeft, RawInputModifiers.Alt, pager.PreviousPageCommand),
			"NextPage" => (PhysicalKey.ArrowRight, RawInputModifiers.Alt, pager.NextPageCommand),
			"PreviousRun" => (PhysicalKey.ArrowLeft, RawInputModifiers.Control, bar.PreviousRunCommand),
			"NextRun" => (PhysicalKey.ArrowRight, RawInputModifiers.Control, bar.NextRunCommand),
			"LatestRun" => (PhysicalKey.End, RawInputModifiers.Control, bar.LatestRunCommand),
			"PreviousMarker" => (PhysicalKey.ArrowLeft, ControlShift, bar.PreviousMarkerCommand),
			"NextMarker" => (PhysicalKey.ArrowRight, ControlShift, bar.NextMarkerCommand),
			"MarkerList" => (PhysicalKey.M, RawInputModifiers.Control, ViewModel.ShowMarkerListCommand),
			"RecipeRunList" => (PhysicalKey.R, RawInputModifiers.Control, ViewModel.ShowRecipeRunListCommand),
			_ => throw new ArgumentOutOfRangeException(nameof(step), step, "No such step.")
		};
	}

	private void ShouldShowItsTarget(string step)
	{
		switch (step)
		{
			case "PreviousPage":
				Navigation.From.Date.Should().Be(_windowFromUtc.Date.AddDays(-1));
				break;
			case "NextPage":
				Navigation.From.Date.Should().Be(_windowFromUtc.Date.AddDays(1));
				break;
			case "PreviousRun":
				ShouldFrame(_earlierRunStart, _earlierRunEnd);
				break;
			case "NextRun":
			case "LatestRun":
				ShouldFrame(_laterRunStart, _laterRunEnd);
				break;
			case "PreviousMarker":
				CentreUtc.Should().Be(_earlierMarker.TimeUtc);
				break;
			default:
				CentreUtc.Should().Be(_laterMarker.TimeUtc);
				break;
		}
	}

	private void ShouldFrame(StoredMarker start, StoredMarker end)
	{
		Navigation.From.Should().BeBefore(start.TimeUtc);
		Navigation.To.Should().BeAfter(end.TimeUtc);
		(Navigation.To - Navigation.From).Should()
			.BeLessThan(TimeSpan.FromHours(1), "the run is framed, not panned to");
	}

	private void HideTheBarAndTheMinimap()
	{
		ViewModel.ToggleNavigationBarCommand.Execute().Subscribe();
		ViewModel.ToggleMinimapCommand.Execute().Subscribe();
		Dispatcher.UIThread.RunJobs();
		ViewProbe.Named<Border>(_window, "NavigationBar").IsVisible.Should().BeFalse();
		ViewProbe.Named<Border>(_window, "MinimapRow").IsVisible.Should().BeFalse();
	}

	/// <summary>
	/// Shows one of the chart's text flyouts the way the chart does and gives its first box the keyboard.
	/// </summary>
	private (Flyout Flyout, TextBox Box) OpenTextBoxIn(string flyoutKey)
	{
		var chartView = ViewProbe.Descendants<TrendChartView>(_window).Single();
		var markersButton = ViewProbe.Named<NavigationBarView>(_window, "NavigationBarContent")
			.FindControl<DropDownButton>("MarkersButton")!;
		var flyout = flyoutKey == "MarkerListFlyout"
			? (Flyout)markersButton.Flyout!
			: (Flyout)chartView.Resources[flyoutKey]!;
		if (flyoutKey == "TimeWindowFlyout")
		{
			ViewModel.ChartViewModel.TimeWindow.Seed();
		}

		if (flyoutKey == "MarkerListFlyout")
		{
			flyout.ShowAt(markersButton);
		}
		else
		{
			flyout.ShowAt(ViewProbe.Descendant<Control>(chartView, "PlotControl"), true);
		}

		Dispatcher.UIThread.RunJobs();
		var box = ((Control)flyout.Content!).GetLogicalDescendants().OfType<TextBox>().First();
		box.Text = flyoutKey == "TimeWindowFlyout" ? "08:30:00" : "Etch start";
		box.Focus();
		box.ClearSelection();
		Dispatcher.UIThread.RunJobs();
		box.IsFocused.Should().BeTrue();

		return (flyout, box);
	}
}
