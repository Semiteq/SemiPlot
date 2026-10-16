using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using ScottPlot;
using ScottPlot.Plottables;

using SemiPlot.Core.Data;
using SemiPlot.Tests.Unit.UI.Markers;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.ChartViewTestBuilder;

using Point = Avalonia.Point;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartMarkerViewTests : IDisposable
{
	private const double DragDistancePixels = 120.0;
	private const double WithinHitPixels = 2.0;
	private const double AwayFromMarkerPixels = 40.0;
	private const double SpareLabelPixels = 3.0;
	private static readonly TimeSpan _pixelMomentTolerance = TimeSpan.FromMilliseconds(1.0);

	private readonly FakeMarkerChannel _channel = new();
	private readonly TrendChartViewModel _viewModel;
	private readonly ShownChart _shown;

	public ChartMarkerViewTests()
	{
		_viewModel = CreateLoadedViewModel(_channel);
		_shown = ShowChart(_viewModel);
		DriveOneFrame();
	}

	private ChartMarkerLines Layer => _shown.View.MarkerLines!;

	private Flyout LabelEditor => (Flyout)_shown.View.Resources["MarkerLabelFlyout"]!;

	private TextBox LabelBox => ((Control)LabelEditor.Content!).GetLogicalDescendants().OfType<TextBox>().Single();

	private TopLevel LabelEditorTopLevel => TopLevel.GetTopLevel(LabelBox)!;

	public void Dispose()
	{
		_shown.Dispose();
		_viewModel.Dispose();
	}

	[AvaloniaFact]
	public void ARead_ShowsOneLinePerMarkerAtItsMoment()
	{
		var first = new StoredMarker(1, MomentAt(0.25), "Etch start");
		var second = new StoredMarker(2, MomentAt(0.75), null);

		_channel.Push(first, second);

		Layer.Lines.Should().HaveCount(2);
		Layer.Lines[1].X.Should().Be(LocalTimeAxis.ToAxis(first.TimeUtc));
		Layer.Lines[2].X.Should().Be(LocalTimeAxis.ToAxis(second.TimeUtc));
		Layer.Lines[1].IsVisible.Should().BeTrue();
		Layer.Lines[2].IsVisible.Should().BeTrue();
	}

	[AvaloniaFact]
	public void AMove_PutsThatMarkersLineAtTheNewMoment()
	{
		var marker = new StoredMarker(1, MomentAt(0.25), "Etch start");
		_channel.Push(marker);
		var newMoment = MomentAt(0.5);

		_viewModel.Markers.BeginMove(marker);
		_viewModel.Markers.Move(newMoment);

		Layer.Lines[1].X.Should().Be(LocalTimeAxis.ToAxis(newMoment));
	}

	[AvaloniaFact]
	public async Task ARename_ShowsTheNewLabel()
	{
		var marker = new StoredMarker(1, MomentAt(0.25), "Etch start");
		_channel.Push(marker);

		await _viewModel.Markers.RenameAsync(marker, "Etch end");

		Layer.Lines[1].LabelText.Should().StartWith("Etch end \u00B7 ");
	}

	[AvaloniaFact]
	public void ALabel_ShowsTheMarkersLocalTimeAfterItsText_AndAnUnlabelledOneTheTimeAlone()
	{
		var labelled = new StoredMarker(1, MomentAt(0.25), "Etch start");
		var unlabelled = new StoredMarker(2, MomentAt(0.75), null);

		_channel.Push(labelled, unlabelled);

		Layer.Lines[1].LabelText.Should().Be($"Etch start \u00B7 {LocalTime(labelled)}");
		Layer.Lines[2].LabelText.Should().Be(LocalTime(unlabelled));
	}

	[AvaloniaFact]
	public void ARebind_LeavesOneReadingsLayerOnThePlot()
	{
		var oldReadings = _shown.View.MarkerReadings!;

		_shown.View.DataContext = null;
		_shown.View.DataContext = _viewModel;

		var layers = _viewModel.Plot.GetPlottables<ChartMarkerReadings>().ToList();
		layers.Should().ContainSingle().Which.Should().BeSameAs(_shown.View.MarkerReadings);
		layers.Should().NotContain(oldReadings);
	}

	[AvaloniaFact]
	public void ARebindToTheSameViewModel_LeavesOneLinePerMarkerOnThePlot()
	{
		_channel.Push(
			new StoredMarker(1, MomentAt(0.25), "Etch start"),
			new StoredMarker(2, MomentAt(0.75), null));
		var oldLines = Layer.Lines.Values.ToList();

		_shown.View.DataContext = null;
		_shown.View.DataContext = _viewModel;

		Layer.Lines.Should().HaveCount(2);
		var plotLines = _viewModel.Plot.GetPlottables<VerticalLine>().ToList();
		plotLines.Should().NotContain(oldLines, "the old layer takes its lines off the plot, the unlabelled one too");
		plotLines.Should().Contain(Layer.Lines.Values);
	}

	[AvaloniaFact]
	public void AMarkerOutsideTheWindow_IsInvisible()
	{
		_channel.Push(
			new StoredMarker(1, MomentAt(-0.5), "Before"),
			new StoredMarker(2, MomentAt(0.5), "Inside"));

		Layer.Lines[1].IsVisible.Should().BeFalse();
		Layer.Lines[2].IsVisible.Should().BeTrue();
	}

	[AvaloniaFact]
	public void ALabelTooCloseToItsNeighbour_IsHiddenAndShowsAfterAResizeThatMakesRoom()
	{
		// "Etch start" and its time are estimated at about 180 px: 0.13 of the window is about 104 px at 900 px and
		// 220 px at 1800 px.
		_channel.Push(
			new StoredMarker(1, MomentAt(0.2), "Etch start"),
			new StoredMarker(2, MomentAt(0.33), "Etch end"));

		Layer.Lines[1].LabelStyle.IsVisible.Should().BeFalse();
		Layer.Lines[2].LabelStyle.IsVisible.Should().BeTrue("the last visible marker has no neighbour to overlap");

		_shown.Window.Width = 1800;
		Dispatcher.UIThread.RunJobs();

		Layer.Lines[1].LabelStyle.IsVisible.Should().BeTrue();
	}

	[AvaloniaFact]
	public void AfterAThemeVariantSwitch_EveryMarkerDrawsInTheNewVariant()
	{
		using var scope = ThemeProbe.ApplyVariant(ThemeVariant.Light);
		Dispatcher.UIThread.RunJobs();
		_channel.Push(new StoredMarker(1, MomentAt(0.25), "Etch start"));
		Layer.Lines[1].LineColor.Should().Be(ThemeProbe.PlotColour("AppMarkerLineBrush", ThemeVariant.Light));

		Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
		Dispatcher.UIThread.RunJobs();
		_channel.Push(
			new StoredMarker(1, MomentAt(0.25), "Etch start"),
			new StoredMarker(2, MomentAt(0.75), "Etch end"));

		foreach (var line in Layer.Lines.Values)
		{
			line.LineColor.Should().Be(ThemeProbe.PlotColour("AppMarkerLineBrush", ThemeVariant.Dark));
			line.LabelFontColor.Should().Be(ThemeProbe.PlotColour("AppMarkerLabelBrush", ThemeVariant.Dark));
		}
	}

	[AvaloniaFact]
	public void ADragPastTheSlop_IssuesOneMoveWithTheDroppedMoment()
	{
		var marker = new StoredMarker(1, MomentAt(0.25), "Etch start");
		_channel.Push(marker);
		var fromBefore = _viewModel.Navigation.From;
		var pressAt = MarkerPoint(marker);
		var dropAt = new Point(pressAt.X + DragDistancePixels, pressAt.Y);

		_shown.Window.MouseDown(ToWindow(pressAt), MouseButton.Left);
		_shown.Window.MouseMove(ToWindow(new Point(pressAt.X + (DragDistancePixels / 2.0), pressAt.Y)));
		_shown.Window.MouseMove(ToWindow(dropAt));
		_shown.Window.MouseUp(ToWindow(dropAt), MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		var droppedMoment = MomentAtPixel(dropAt);
		var move = _channel.Writes.Should().ContainSingle()
			.Which.Should().BeOfType<FakeMarkerWrite.Move>().Subject;
		move.TimeUtc.Should().BeCloseTo(droppedMoment, _pixelMomentTolerance);
		move.TimeUtc.Kind.Should().Be(DateTimeKind.Utc, "Npgsql binds only a UTC moment to timestamptz");
		_viewModel.Markers.Markers.Should().ContainSingle()
			.Which.TimeUtc.Should().BeCloseTo(
				droppedMoment,
				_pixelMomentTolerance,
				"the move landed and the marker stays");
		_viewModel.Navigation.From.Should().Be(fromBefore, "a marker drag does not pan");
		_viewModel.IsDragging.Should().BeFalse();
	}

	[AvaloniaFact]
	public void AClickOnAMarkerInsideTheSlop_IssuesNoMoveAndPicksThePenUnderIt()
	{
		_viewModel.SetAxisLimits(1, 0.0, 10.0);
		_viewModel.SetAxisLimits(2, 0.0, 100.0);
		Render();
		var marker = new StoredMarker(1, ChartTestBuilder.From.AddMinutes(30.0), "Etch start");
		_channel.Push(marker);
		var dataRect = _viewModel.Plot.RenderManager.LastRender.Layout.DataRect;
		var onSecondPensLine = new Point(
			MarkerPoint(marker).X,
			_viewModel.FindPen(2)!.Line.Axes.YAxis.GetPixel(50.0, dataRect));
		_viewModel.ActivePenId.Should().Be(1);

		HeadlessInput.Click(_shown.Window, _shown.PlotControl, onSecondPensLine);

		_channel.Writes.Should().BeEmpty();
		_viewModel.ActivePenId.Should().Be(2, "a release inside the slop picks a pen as a pan press does");
		Layer.Lines[1].X.Should().Be(LocalTimeAxis.ToAxis(marker.TimeUtc));
	}

	[AvaloniaFact]
	public void HoveringAMarker_SnapsTheCursorToItsMomentAndShowsTheMarkerCursor()
	{
		var marker = new StoredMarker(1, MomentAt(0.5), "Etch start");
		_channel.Push(marker);
		var onMarker = MarkerPoint(marker);
		var handCursor = _shown.PlotControl.Cursor;

		_shown.Window.MouseMove(ToWindow(new Point(onMarker.X + WithinHitPixels, onMarker.Y)));
		var markerCursor = _shown.PlotControl.Cursor;
		var cursorTimeOnMarker = _viewModel.CursorTime;
		_shown.Window.MouseMove(ToWindow(new Point(onMarker.X + AwayFromMarkerPixels, onMarker.Y)));

		cursorTimeOnMarker.Should().Be(marker.TimeUtc);
		markerCursor!.ToString().Should().Be(nameof(StandardCursorType.SizeWestEast));
		_viewModel.CursorTime.Should().NotBe(marker.TimeUtc, "away from the marker the cursor follows the pointer");
		_shown.PlotControl.Cursor.Should().BeSameAs(handCursor);
	}

	[AvaloniaFact]
	public void HoveringAMarkerWithCtrlHeld_KeepsTheHand()
	{
		var marker = new StoredMarker(1, MomentAt(0.5), "Etch start");
		_channel.Push(marker);
		var handCursor = _shown.PlotControl.Cursor;

		_shown.Window.MouseMove(ToWindow(MarkerPoint(marker)), RawInputModifiers.Control);

		_shown.PlotControl.Cursor.Should().BeSameAs(handCursor, "a press with Ctrl held shifts the pen");
		_viewModel.CursorTime.Should().Be(marker.TimeUtc, "the cursor still snaps to the marker");
	}

	[AvaloniaFact]
	public void HoveringAMarkerInDeltaMode_KeepsTheHand()
	{
		var marker = new StoredMarker(1, MomentAt(0.5), "Etch start");
		_channel.Push(marker);
		var handCursor = _shown.PlotControl.Cursor;
		_viewModel.SetDeltaModeEnabled(true);

		_shown.Window.MouseMove(ToWindow(MarkerPoint(marker)));

		_shown.PlotControl.Cursor.Should().BeSameAs(handCursor, "a press in delta mode places a delta cursor");
	}

	[AvaloniaFact]
	public void APanThatTakesTheMarkerOutOfTheWindow_HidesItsLine()
	{
		_channel.Push(new StoredMarker(1, MomentAt(0.25), "Etch start"));
		Layer.Lines[1].IsVisible.Should().BeTrue();
		var fromBefore = _viewModel.Navigation.From;

		_viewModel.Navigation.PanBy((_viewModel.Navigation.To - _viewModel.Navigation.From) * 0.5);

		_viewModel.Navigation.From.Should().BeAfter(fromBefore);
		Layer.Lines[1].IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ALabelThatStopsFittingWhenThePenAxisAppears_IsHiddenAfterTheRender()
	{
		_viewModel.SetPenVisibility(1, false);
		_viewModel.SetPenVisibility(2, false);
		Render();
		Dispatcher.UIThread.RunJobs();
		var first = new StoredMarker(1, MomentAt(0.2), "Etch start");
		var second = MarkerPastTheNarrowestFittingGap(first, SpareLabelPixels);
		_channel.Push(first, second);
		Layer.Lines[1].LabelStyle.IsVisible.Should().BeTrue("the label fits with 3 px to spare");
		var widthBefore = _viewModel.Plot.LastRender.DataRect.Width;

		_viewModel.SetPenVisibility(1, true);
		_viewModel.SetPenVisibility(2, true);
		Render();
		Dispatcher.UIThread.RunJobs();

		_viewModel.Plot.LastRender.DataRect.Width.Should().BeLessThan(widthBefore, "the drawn pen axis takes room");
		Layer.Lines[1].LabelStyle.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ACaptureLostMidMarkerDrag_StoresTheLastMoment()
	{
		var marker = new StoredMarker(1, MomentAt(0.25), "Etch start");
		_channel.Push(marker);
		var pressedPointer = CapturePointerOfNextPress(_shown.Window);
		var pressAt = MarkerPoint(marker);
		var dragTo = new Point(pressAt.X + DragDistancePixels, pressAt.Y);

		_shown.Window.MouseDown(ToWindow(pressAt), MouseButton.Left);
		_shown.Window.MouseMove(ToWindow(dragTo));
		pressedPointer().Capture(null);
		Dispatcher.UIThread.RunJobs();

		_channel.Writes.Should().ContainSingle()
			.Which.Should().BeOfType<FakeMarkerWrite.Move>()
			.Which.TimeUtc.Should().BeCloseTo(MomentAtPixel(dragTo), _pixelMomentTolerance);
		_viewModel.IsDragging.Should().BeFalse();
	}

	[AvaloniaFact]
	public void AMarkerDragAcrossADataContextChange_EndsOnTheViewModelItBeganOn()
	{
		var marker = new StoredMarker(1, MomentAt(0.25), "Etch start");
		_channel.Push(marker);
		var pressAt = MarkerPoint(marker);
		var dropAt = new Point(pressAt.X + DragDistancePixels, pressAt.Y);
		var droppedMoment = MomentAtPixel(dropAt);

		_shown.Window.MouseDown(ToWindow(pressAt), MouseButton.Left);
		_shown.Window.MouseMove(ToWindow(dropAt));
		_shown.View.DataContext = null;
		_shown.Window.MouseUp(ToWindow(dropAt), MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		_channel.Writes.Should().ContainSingle()
			.Which.Should().BeOfType<FakeMarkerWrite.Move>()
			.Which.TimeUtc.Should().BeCloseTo(droppedMoment, _pixelMomentTolerance);
		_viewModel.IsDragging.Should().BeFalse();
		_channel.Push(marker);
		_viewModel.Markers.Markers.Should().Equal([marker], "the landed move released its hold");
	}

	[AvaloniaFact]
	public void ARightClickOutsideTheDataArea_OpensNoMenu()
	{
		RightClick(TimeAxisPoint(_viewModel));

		MenuFlyout("ChartMenuFlyout").IsOpen.Should().BeFalse();
		MenuFlyout("MarkerMenuFlyout").IsOpen.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ARightReleaseDuringALeftDrag_OpensNoMenu()
	{
		var pressAt = DataAreaCentreAt(MomentAt(0.5));
		var dragTo = new Point(pressAt.X - DragDistancePixels, pressAt.Y);

		_shown.Window.MouseDown(ToWindow(pressAt), MouseButton.Left);
		_shown.Window.MouseMove(ToWindow(dragTo));
		_shown.Window.MouseDown(ToWindow(dragTo), MouseButton.Right);
		_shown.Window.MouseUp(ToWindow(dragTo), MouseButton.Right);

		MenuFlyout("ChartMenuFlyout").IsOpen.Should().BeFalse();
		MenuFlyout("MarkerMenuFlyout").IsOpen.Should().BeFalse();
		_shown.Window.MouseUp(ToWindow(dragTo), MouseButton.Left);
	}

	[AvaloniaFact]
	public void ARightPressWithTheLeftHeldDuringAMarkerDrag_KeepsTheDragAndReleasesTheHold()
	{
		var marker = new StoredMarker(1, MomentAt(0.25), "Etch start");
		_channel.Push(marker);
		var pressAt = MarkerPoint(marker);
		var chordAt = new Point(pressAt.X + (DragDistancePixels / 2.0), pressAt.Y);
		var dropAt = new Point(pressAt.X + DragDistancePixels, pressAt.Y);

		_shown.Window.MouseDown(ToWindow(pressAt), MouseButton.Left);
		_shown.Window.MouseMove(ToWindow(chordAt));
		_shown.Window.MouseDown(ToWindow(chordAt), MouseButton.Right, RawInputModifiers.LeftMouseButton);
		_shown.Window.MouseUp(ToWindow(chordAt), MouseButton.Right, RawInputModifiers.LeftMouseButton);
		_shown.Window.MouseMove(ToWindow(dropAt), RawInputModifiers.LeftMouseButton);
		_shown.Window.MouseUp(ToWindow(dropAt), MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		MenuFlyout("ChartMenuFlyout").IsOpen.Should().BeFalse();
		MenuFlyout("MarkerMenuFlyout").IsOpen.Should().BeFalse();

		_channel.Writes.Should().ContainSingle()
			.Which.Should().BeOfType<FakeMarkerWrite.Move>()
			.Which.TimeUtc.Should().BeCloseTo(MomentAtPixel(dropAt), _pixelMomentTolerance);
		_viewModel.IsDragging.Should().BeFalse();
		var movedElsewhere = marker with { TimeUtc = MomentAt(0.75) };
		_channel.Push(movedElsewhere);
		_viewModel.Markers.Markers.Should().Equal([movedElsewhere], "the drag released its hold on the marker");
	}

	[AvaloniaFact]
	public void ASecondTouchDuringAMarkerDrag_NeitherMovesNorEndsTheDrag()
	{
		var marker = new StoredMarker(1, MomentAt(0.25), "Etch start");
		_channel.Push(marker);
		var pressAt = MarkerPoint(marker);
		var midAt = new Point(pressAt.X + (DragDistancePixels / 2.0), pressAt.Y);
		var dropAt = new Point(pressAt.X + DragDistancePixels, pressAt.Y);
		var secondAt = DataAreaCentreAt(MomentAt(0.75));
		var secondTo = new Point(secondAt.X + DragDistancePixels, secondAt.Y);
		var windowFrom = _viewModel.Navigation.From;
		var firstFinger = new TouchContact(_shown.Window, _shown.PlotControl);
		var secondFinger = new TouchContact(_shown.Window, _shown.PlotControl);

		firstFinger.Press(ToWindow(pressAt));
		firstFinger.Move(ToWindow(midAt));
		secondFinger.Press(ToWindow(secondAt));
		secondFinger.Move(ToWindow(secondTo));
		secondFinger.Lift(ToWindow(secondTo));
		_viewModel.IsDragging.Should().BeTrue("the second finger's lift does not end the first finger's drag");
		firstFinger.Move(ToWindow(dropAt));
		firstFinger.Lift(ToWindow(dropAt));
		Dispatcher.UIThread.RunJobs();

		_channel.Writes.Should().ContainSingle()
			.Which.Should().BeOfType<FakeMarkerWrite.Move>()
			.Which.TimeUtc.Should().BeCloseTo(MomentAtPixel(dropAt), _pixelMomentTolerance);
		_viewModel.Navigation.From.Should().Be(windowFrom, "the second finger never pans the window");
		_viewModel.IsDragging.Should().BeFalse();
		var movedElsewhere = marker with { TimeUtc = MomentAt(0.75) };
		_channel.Push(movedElsewhere);
		_viewModel.Markers.Markers.Should().Equal([movedElsewhere], "the drag released its hold on the marker");
	}

	[AvaloniaFact]
	public void AClickOnTheTimeAxis_NeverHitsTheMarkerAboveIt_AndOpensTheTimeWindowPanel()
	{
		var timeAxisPoint = TimeAxisPoint(_viewModel);
		var marker = new StoredMarker(1, MomentAtPixel(timeAxisPoint), "Etch start");
		_channel.Push(marker);

		HeadlessInput.Click(_shown.Window, _shown.PlotControl, timeAxisPoint);

		_shown.TimeWindowFlyout.IsOpen.Should().BeTrue();
		_channel.Writes.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void APointerMoveBeforeTheFirstRender_ThrowsNothing()
	{
		var channel = new FakeMarkerChannel();
		using var viewModel = CreateLoadedViewModel(channel);
		var view = new TrendChartView
		{
			DataContext = viewModel
		};
		var window = ViewProbe.Realise(new Window
		{
			Width = 900,
			Height = 600,
			Content = view
		});
		try
		{
			channel.Push(new StoredMarker(1, viewModel.Navigation.From, "Etch start"));
			viewModel.Plot.LastRender.DataRect.Width.Should().Be(0f, "the plot has not rendered yet");
			var plotControl = ViewProbe.Named<Control>(view, "PlotControl");
			var center = new Point(plotControl.Bounds.Width / 2.0, plotControl.Bounds.Height / 2.0);

			var move = () => window.MouseMove(plotControl.TranslatePoint(center, window)!.Value);

			move.Should().NotThrow();
		}
		finally
		{
			ViewProbe.Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void ADoubleClickInTheDataArea_AddsANumberedMarkerAtTheClickedMoment()
	{
		var clickedAt = DataAreaCentreAt(MomentAt(0.5));

		HeadlessInput.Click(_shown.Window, _shown.PlotControl, clickedAt);
		HeadlessInput.Click(_shown.Window, _shown.PlotControl, clickedAt);

		var add = _channel.Writes.Should().ContainSingle()
			.Which.Should().BeOfType<FakeMarkerWrite.Add>().Subject;
		add.Label.Should().Be("#1", "a new marker is numbered");
		add.TimeUtc.Should().BeCloseTo(MomentAtPixel(clickedAt), _pixelMomentTolerance);
		add.TimeUtc.Kind.Should().Be(DateTimeKind.Utc, "Npgsql binds only a UTC moment to timestamptz");
		_viewModel.Markers.Markers.Should().ContainSingle().Which.Label.Should().Be("#1");
		Layer.Lines.Should().ContainSingle();
		LabelEditor.IsOpen.Should().BeFalse("a double click opens no editor");
		_viewModel.IsDragging.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ADoubleClickOnAMarker_DeletesIt()
	{
		var marker = new StoredMarker(1, MomentAt(0.5), "Etch start");
		_channel.Push(marker);

		HeadlessInput.Click(_shown.Window, _shown.PlotControl, MarkerPoint(marker));
		HeadlessInput.Click(_shown.Window, _shown.PlotControl, MarkerPoint(marker));

		_channel.Writes.Should().ContainSingle()
			.Which.Should().BeOfType<FakeMarkerWrite.Delete>()
			.Which.Marker.Should().Be(marker);
		_viewModel.Markers.Markers.Should().BeEmpty();
		Layer.Lines.Should().BeEmpty();
		_viewModel.IsDragging.Should().BeFalse();
	}

	[AvaloniaFact]
	public void AddMarkerHere_AddsANumberedMarkerAtTheClickedMoment()
	{
		var clickedAt = DataAreaCentreAt(MomentAt(0.5));

		RightClick(clickedAt);
		PickMenuItem("ChartMenuFlyout", Resources.MarkerMenuAdd);

		var add = _channel.Writes.Should().ContainSingle()
			.Which.Should().BeOfType<FakeMarkerWrite.Add>().Subject;
		add.Label.Should().Be("#1");
		add.TimeUtc.Should().BeCloseTo(MomentAtPixel(clickedAt), _pixelMomentTolerance);
		_viewModel.Markers.Markers.Should().ContainSingle();
		LabelEditor.IsOpen.Should().BeFalse();
	}

	[AvaloniaFact]
	public void RenameOnAMarker_WithANewLabelAndOk_RelabelsIt()
	{
		var marker = new StoredMarker(1, MomentAt(0.25), null);
		_channel.Push(marker);
		OpenLabelEditorToRename(marker);
		LabelBox.Text.Should().BeEmpty();
		HeadlessInput.Type(LabelEditorTopLevel, LabelBox, "Etch start");

		HeadlessInput.Click(LabelEditorTopLevel, LabelEditorButton("MarkerLabelOk"));

		_channel.Writes.Should().ContainSingle()
			.Which.Should().BeOfType<FakeMarkerWrite.Rename>()
			.Which.Label.Should().Be("Etch start");
		Layer.Lines[1].LabelText.Should().StartWith("Etch start \u00B7 ");
		LabelEditor.IsOpen.Should().BeFalse();
	}

	[AvaloniaFact]
	public void RenameOnAMarker_ThenCancel_KeepsTheOldLabel()
	{
		var marker = new StoredMarker(1, MomentAt(0.25), "Etch start");
		_channel.Push(marker);
		OpenLabelEditorToRename(marker);
		HeadlessInput.Clear(LabelEditorTopLevel, LabelBox);
		HeadlessInput.Type(LabelEditorTopLevel, LabelBox, "Etch end");

		HeadlessInput.Click(LabelEditorTopLevel, LabelEditorButton("MarkerLabelCancel"));

		LabelEditor.IsOpen.Should().BeFalse();
		_channel.Writes.Should().BeEmpty();
		Layer.Lines[1].LabelText.Should().StartWith("Etch start \u00B7 ");
	}

	[AvaloniaFact]
	public void DeleteOnAMarker_RemovesIt()
	{
		var marker = new StoredMarker(1, MomentAt(0.25), "Etch start");
		_channel.Push(marker);

		RightClick(MarkerPoint(marker));
		PickMenuItem("MarkerMenuFlyout", Resources.MarkerMenuDelete);

		_channel.Writes.Should().ContainSingle()
			.Which.Should().BeOfType<FakeMarkerWrite.Delete>()
			.Which.Marker.Should().Be(marker);
		_viewModel.Markers.Markers.Should().BeEmpty();
		Layer.Lines.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void RenameOnAMarker_WithANewLabelAndEnter_RelabelsIt()
	{
		var marker = new StoredMarker(1, MomentAt(0.25), "Etch start");
		_channel.Push(marker);

		OpenLabelEditorToRename(marker);
		LabelBox.Text.Should().Be("Etch start", "the editor opens with the current label");
		HeadlessInput.Clear(LabelEditorTopLevel, LabelBox);
		HeadlessInput.Type(LabelEditorTopLevel, LabelBox, "Etch end");
		HeadlessInput.Press(LabelEditorTopLevel, PhysicalKey.Enter);

		_channel.Writes.Should().ContainSingle()
			.Which.Should().BeOfType<FakeMarkerWrite.Rename>()
			.Which.Label.Should().Be("Etch end");
		Layer.Lines[1].LabelText.Should().StartWith("Etch end \u00B7 ");
	}

	[AvaloniaTheory]
	[InlineData("")]
	[InlineData("   ")]
	public void RenameOnAMarker_WithAnEmptyLabelAndEnter_StoresNoLabel(string typed)
	{
		var marker = new StoredMarker(1, MomentAt(0.25), "Etch start");
		_channel.Push(marker);

		OpenLabelEditorToRename(marker);
		HeadlessInput.Clear(LabelEditorTopLevel, LabelBox);
		if (typed.Length > 0)
		{
			HeadlessInput.Type(LabelEditorTopLevel, LabelBox, typed);
		}

		HeadlessInput.Press(LabelEditorTopLevel, PhysicalKey.Enter);

		_channel.Writes.Should().ContainSingle()
			.Which.Should().BeOfType<FakeMarkerWrite.Rename>()
			.Which.Label.Should().BeNull();
		_viewModel.Markers.Markers.Should().ContainSingle().Which.Label.Should().BeNull();
	}

	[AvaloniaFact]
	public void RenameOnAMarker_ThenEscape_KeepsTheOldLabel()
	{
		var marker = new StoredMarker(1, MomentAt(0.25), "Etch start");
		_channel.Push(marker);

		OpenLabelEditorToRename(marker);
		HeadlessInput.Clear(LabelEditorTopLevel, LabelBox);
		HeadlessInput.Type(LabelEditorTopLevel, LabelBox, "Etch end");
		HeadlessInput.Press(LabelEditorTopLevel, PhysicalKey.Escape);

		LabelEditor.IsOpen.Should().BeFalse();
		_channel.Writes.Should().BeEmpty();
		Layer.Lines[1].LabelText.Should().StartWith("Etch start \u00B7 ");
	}

	private void OpenLabelEditorToRename(StoredMarker marker)
	{
		RightClick(MarkerPoint(marker));
		PickMenuItem("MarkerMenuFlyout", Resources.MarkerMenuRename);
		LabelEditor.IsOpen.Should().BeTrue();
		LabelBox.IsFocused.Should().BeTrue("the editor opens with the keyboard on its field");
	}

	private void RightClick(Point plotPoint)
	{
		HeadlessInput.Click(_shown.Window, _shown.PlotControl, plotPoint, MouseButton.Right);
	}

	private Button LabelEditorButton(string name)
	{
		return ((Control)LabelEditor.Content!)
			.GetLogicalDescendants()
			.OfType<Button>()
			.Single(button => button.Name == name);
	}

	private MenuFlyout MenuFlyout(string menuKey)
	{
		return (MenuFlyout)_shown.View.Resources[menuKey]!;
	}

	private void PickMenuItem(string menuKey, string header)
	{
		var menu = MenuFlyout(menuKey);
		menu.IsOpen.Should().BeTrue("a right click in the data area opens this menu");
		var item = menu.Popup.Child!
			.GetVisualDescendants()
			.OfType<MenuItem>()
			.Single(menuItem => Equals(menuItem.Header, header));

		HeadlessInput.Click(TopLevel.GetTopLevel(item)!, item);
	}

	// The layer itself finds the gap: a label hides while the next marker is closer than its estimated width.
	private StoredMarker MarkerPastTheNarrowestFittingGap(StoredMarker first, double sparePixels)
	{
		var firstPoint = MarkerPoint(first);

		for (var gap = 1.0; gap < _shown.PlotControl.Bounds.Width; gap++)
		{
			_channel.Push(first, SecondMarkerAt(firstPoint, gap));
			if (Layer.Lines[first.Id].LabelStyle.IsVisible)
			{
				return SecondMarkerAt(firstPoint, gap + sparePixels);
			}
		}

		throw new InvalidOperationException("The first marker's label fits before no point of the data area.");
	}

	private StoredMarker SecondMarkerAt(Point firstPoint, double gap)
	{
		return new StoredMarker(2, MomentAtPixel(new Point(firstPoint.X + gap, firstPoint.Y)), "Etch end");
	}

	private static string LocalTime(StoredMarker marker)
	{
		return marker.TimeUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
	}

	private DateTime MomentAt(double fractionOfWindow)
	{
		var navigation = _viewModel.Navigation;

		return navigation.From + ((navigation.To - navigation.From) * fractionOfWindow);
	}

	private void Render()
	{
		_viewModel.Plot.RenderInMemory((int)_shown.PlotControl.Bounds.Width, (int)_shown.PlotControl.Bounds.Height);
	}

	private Point MarkerPoint(StoredMarker marker)
	{
		return DataAreaCentreAt(marker.TimeUtc);
	}

	private Point DataAreaCentreAt(DateTime moment)
	{
		var dataRect = _viewModel.Plot.RenderManager.LastRender.Layout.DataRect;
		var x = _viewModel.Plot.GetPixel(new Coordinates(LocalTimeAxis.ToAxis(moment), 0.0)).X;

		return new Point(x, (dataRect.Top + dataRect.Bottom) / 2.0);
	}

	private DateTime MomentAtPixel(Point plotPoint)
	{
		return LocalTimeAxis.FromAxis(
			_viewModel.Plot.GetCoordinates(new Pixel((float)plotPoint.X, (float)plotPoint.Y)).X);
	}

	private Point ToWindow(Point plotPoint)
	{
		return ChartViewTestBuilder.ToWindow(_shown.PlotControl, _shown.Window, plotPoint);
	}

	/// <summary>
	/// One finger on the target, raising what Avalonia's TouchDevice raises for one contact; the headless platform
	/// injects no touch, and the reference assemblies hide TouchDevice.
	/// </summary>
	private sealed class TouchContact(Window window, Control target)
	{
		private readonly Pointer _pointer = new(Pointer.GetNextFreeId(), PointerType.Touch, isPrimary: false);

		public void Press(Point windowPoint)
		{
			_pointer.Capture(target);
			target.RaiseEvent(new PointerPressedEventArgs(
				target,
				_pointer,
				window,
				windowPoint,
				0,
				new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
				KeyModifiers.None));
			Dispatcher.UIThread.RunJobs();
		}

		public void Move(Point windowPoint)
		{
			target.RaiseEvent(new PointerEventArgs(
				InputElement.PointerMovedEvent,
				target,
				_pointer,
				window,
				windowPoint,
				0,
				new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
				KeyModifiers.None));
			Dispatcher.UIThread.RunJobs();
		}

		public void Lift(Point windowPoint)
		{
			target.RaiseEvent(new PointerReleasedEventArgs(
				target,
				_pointer,
				window,
				windowPoint,
				0,
				new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
				KeyModifiers.None,
				MouseButton.Left));
			_pointer.Capture(null);
			Dispatcher.UIThread.RunJobs();
		}
	}
}
