using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using ReactiveUI;

using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.TickGenerators;

using SemiPlot.Core.Data;

using Serilog;

using Cursor = Avalonia.Input.Cursor;

namespace SemiPlot.UI.Chart;

public partial class TrendChartView : UserControl
{
	private const double WheelNarrowFactor = 0.8;
	private const double WheelWidenFactor = 1.25;
	private const double WheelPanFraction = 0.1;
	private const double WheelShiftFraction = 0.1;
	private const double MarkerHitPixels = 4.0;

	private static readonly Cursor _handCursor = new(StandardCursorType.Hand);
	private static readonly Cursor _grabbingCursor = new(StandardCursorType.SizeAll);
	private static readonly Cursor _penShiftCursor = new(StandardCursorType.SizeNorthSouth);
	private static readonly Cursor _markerCursor = new(StandardCursorType.SizeWestEast);

	// ScottPlot's own constructor seeds the render events with an empty delegate; this restores that state
	// when the view drops the last handler.
	private static readonly EventHandler<RenderDetails> _noRenderFinishedHandler = (_, _) => { };

	private readonly PopupFlyoutBase _axisScaleFlyout;
	private readonly Flyout _timeWindowFlyout;
	private readonly TimeWindowPanel _timeWindowPanel;
	private readonly MenuFlyout _chartMenuFlyout;
	private readonly MenuFlyout _markerMenuFlyout;
	private readonly Flyout _markerLabelFlyout;
	private readonly CompositeDisposable _disposables = [];
	private VerticalLine? _deltaFirstLine;
	private VerticalLine? _deltaSecondLine;
	private ChartDrag? _drag;
	private Window? _hostWindow;
	private int _frameSequence;
	private int? _pendingFrame;

	// -1 until the bound plot is painted once, so the first scale revision always repaints.
	private int _paintedAxisCount = -1;

	private TrendChartViewModel? _viewModel;

	public TrendChartView()
	{
		InitializeComponent();
		_axisScaleFlyout = FlyoutBase.GetAttachedFlyout(PlotControl) as PopupFlyoutBase
			?? throw new InvalidOperationException("The chart view declares its axis scale flyout in XAML.");
		_timeWindowFlyout = Resources["TimeWindowFlyout"] as Flyout
			?? throw new InvalidOperationException("The chart view declares its time window flyout in XAML.");
		_timeWindowPanel = _timeWindowFlyout.Content as TimeWindowPanel
			?? throw new InvalidOperationException("The time window flyout holds the time window panel.");
		_chartMenuFlyout = Resources["ChartMenuFlyout"] as MenuFlyout
			?? throw new InvalidOperationException("The chart view declares its chart menu in XAML.");
		_markerMenuFlyout = Resources["MarkerMenuFlyout"] as MenuFlyout
			?? throw new InvalidOperationException("The chart view declares its marker menu in XAML.");
		_markerLabelFlyout = Resources["MarkerLabelFlyout"] as Flyout
			?? throw new InvalidOperationException("The chart view declares its marker label editor in XAML.");
		_markerLabelFlyout.Opened += OnMarkerLabelFlyoutOpened;
		DataContextChanged += OnDataContextChanged;

		PlotControl.UserInputProcessor.Disable();
		// AvaPlot marks every wheel event handled in its class handler, which would keep
		// OnPointerWheelChanged below from ever running.
		PlotControl.HandleMouseWheelEvent = false;
		PlotControl.Cursor = _handCursor;
		PlotControl.PointerWheelChanged += OnPointerWheelChanged;
		PlotControl.PointerPressed += OnPointerPressed;
		PlotControl.PointerMoved += OnPointerMoved;
		// AvaPlot's class handler releases the capture first, which would end the drag before a bubbling handler runs.
		PlotControl.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
		PlotControl.PointerCaptureLost += OnPointerCaptureLost;
		PlotControl.PointerExited += OnPointerExited;
		PlotControl.SizeChanged += OnPlotSizeChanged;
	}

	/// <summary>Animation-frame callbacks that served a redraw request since the view was built.</summary>
	internal int ServedFrameCount { get; private set; }

	/// <summary>The marker layer on the bound plot; null while no view model is bound.</summary>
	internal ChartMarkerLines? MarkerLines { get; private set; }

	/// <summary>The active pen's readings at the markers on the bound plot; null while no view model is bound.</summary>
	internal ChartMarkerReadings? MarkerReadings { get; private set; }

	protected override void OnLoaded(RoutedEventArgs e)
	{
		base.OnLoaded(e);

		if (Application.Current is { } application)
		{
			application.ActualThemeVariantChanged += OnActualThemeVariantChanged;
		}

		_hostWindow = TopLevel.GetTopLevel(this) as Window;
		_hostWindow?.Closing += OnHostWindowClosing;

		RepaintChart();
	}

	protected override void OnUnloaded(RoutedEventArgs e)
	{
		ClearPendingFrame();

		if (Application.Current is { } application)
		{
			application.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
		}

		_hostWindow?.Closing -= OnHostWindowClosing;
		_hostWindow = null;

		base.OnUnloaded(e);
	}

	// Avalonia 12.0.5 throws while a closing window detaches an open calendar popup nested in this flyout, so the
	// flyout closes first.
	private void OnHostWindowClosing(object? sender, WindowClosingEventArgs eventArgs)
	{
		_timeWindowFlyout.Hide();
	}

	private void OnActualThemeVariantChanged(object? sender, EventArgs eventArgs)
	{
		RepaintChart();
	}

	private void RepaintChart()
	{
		ApplyChartPalette();
		RequestFrame();
	}

	// docs/architecture/ui-theme.md#the-plot
	private void PaintAxesAddedSinceLastPaint()
	{
		if (_viewModel is null || _viewModel.AxisCount == _paintedAxisCount)
		{
			return;
		}

		ApplyChartPalette();
	}

	private void ApplyChartPalette()
	{
		if (Application.Current is not { } application)
		{
			return;
		}

		ChartPalette.Apply(PlotControl.Plot, application, application.ActualThemeVariant);
		_paintedAxisCount = _viewModel?.AxisCount ?? -1;
		PaintMarkerLines();
	}

	private void PaintMarkerLines()
	{
		if (MarkerLines is null || MarkerReadings is null || Application.Current is not { } application)
		{
			return;
		}

		var palette = ChartPalette.MarkerColors(application, application.ActualThemeVariant);
		MarkerLines.ApplyPalette(palette);
		MarkerReadings.ApplyPalette(palette);
	}

	// docs/architecture/charting.md#module-layout-avalonia-views--view-models--core-models
	private EventHandler<RenderDetails> ReportDataAreaWidthTo(TrendChartViewModel renderedViewModel)
	{
		float? lastPostedWidth = null;

		return (_, renderDetails) =>
		{
			var dataAreaWidth = renderDetails.DataRect.Width;
			if (dataAreaWidth == lastPostedWidth)
			{
				return;
			}

			lastPostedWidth = dataAreaWidth;
			Dispatcher.UIThread.Post(() =>
			{
				renderedViewModel.ReportDataAreaWidth(dataAreaWidth);
				ProjectMarkerLinesAtRenderedWidth(renderedViewModel, dataAreaWidth);
			});
		};
	}

	// docs/architecture/trend-interaction.md#the-time-markers
	private void ProjectMarkerLinesAtRenderedWidth(TrendChartViewModel renderedViewModel, double dataAreaWidth)
	{
		if (MarkerLines is not { Lines.Count: > 0 } markerLines || _viewModel != renderedViewModel)
		{
			return;
		}

		markerLines.Project(renderedViewModel.Navigation.From, renderedViewModel.Navigation.To, dataAreaWidth);
		RequestFrame();
	}

	private void OnDataContextChanged(object? sender, EventArgs eventArgs)
	{
		_disposables.Clear();
		MarkerLines = null;
		MarkerReadings = null;
		ClearPendingFrame();
		_paintedAxisCount = -1;
		_viewModel = DataContext as TrendChartViewModel;
		AxisScale.DataContext = _viewModel?.AxisScale;
		_timeWindowPanel.DataContext = _viewModel?.TimeWindow;

		if (_viewModel is null)
		{
			return;
		}

		PlotControl.Reset(_viewModel.Plot);
		_disposables.Add(_viewModel.AxisScale.CloseRequests
			.Subscribe(
				_ => _axisScaleFlyout.Hide(),
				ReportFailureOf(nameof(AxisScalePanelViewModel.CloseRequests))));
		_disposables.Add(_viewModel.TimeWindow.CloseRequests
			.Subscribe(
				_ => _timeWindowFlyout.Hide(),
				ReportFailureOf(nameof(TimeWindowPanelViewModel.CloseRequests))));

		var renderManager = _viewModel.Plot.RenderManager;
		var reportDataAreaWidth = ReportDataAreaWidthTo(_viewModel);
		renderManager.RenderFinished += reportDataAreaWidth;

		// RenderFinished is a plain delegate property, so the removal keeps the empty-delegate contract
		// instead of leaving null behind.
		_disposables.Add(Disposable.Create(() => renderManager.RenderFinished =
			(renderManager.RenderFinished - reportDataAreaWidth) ?? _noRenderFinishedHandler));

		// Assigned in place rather than via Plot.Axes.DateTimeTicksBottom(), which would replace the shared
		// bottom-X axis instance the plottables are pinned to.
		_viewModel.Plot.Axes.Bottom.TickGenerator = new DateTimeAutomatic();
		CreateDeltaCursorLines(_viewModel.Plot);
		ApplyWindow(_viewModel.Navigation.From, _viewModel.Navigation.To);
		CreateMarkerLines(_viewModel);

		_viewModel.Navigation.WindowChanged += OnNavigationWindowChanged;
		_disposables.Add(Disposable.Create(() =>
			_viewModel.Navigation.WindowChanged -= OnNavigationWindowChanged));

		_disposables.Add(_viewModel
			.WhenAnyValue(viewModel => viewModel.IsDeltaModeEnabled)
			.Subscribe(
				_ =>
				{
					UpdateDeltaCursorLines();
					RepositionCursorOverlay();
				},
				ReportFailureOf(nameof(TrendChartViewModel.IsDeltaModeEnabled))));

		_disposables.Add(_viewModel
			.WhenAnyValue(viewModel => viewModel.ScalesRevision)
			.Subscribe(
				_ => PaintAxesAddedSinceLastPaint(),
				ReportFailureOf(nameof(TrendChartViewModel.ScalesRevision))));

		_disposables.Add(_viewModel.RedrawRequested
			.Subscribe(
				_ => RequestFrame(),
				ReportFailureOf(nameof(TrendChartViewModel.RedrawRequested)),
				ClearPendingFrame));
	}

	// docs/architecture/charting.md#the-frame-paced-redraw
	private void RequestFrame()
	{
		if (_pendingFrame is not null || TopLevel.GetTopLevel(this) is not { } topLevel)
		{
			return;
		}

		var frame = ++_frameSequence;
		topLevel.RequestAnimationFrame(_ => ServeFrame(frame));
		_pendingFrame = frame;
	}

	private void ServeFrame(int frame)
	{
		if (_pendingFrame != frame)
		{
			return;
		}

		_pendingFrame = null;

		if (_viewModel is null || TopLevel.GetTopLevel(this) is null)
		{
			return;
		}

		UpdateMarkerReadings(_viewModel);
		PlotControl.InvalidateVisual();
		RepositionCursorOverlay();
		ServedFrameCount++;
	}

	private void ClearPendingFrame()
	{
		_pendingFrame = null;
	}

	// ChartPalette.Resolve throws on a dropped key. A pipeline that ends stops repainting the chart, so the
	// operator is told through the panel as well as the log.
	private Action<Exception> ReportFailureOf(string pipeline)
	{
		return failure =>
		{
			Log.Error(failure, "The chart view's {Pipeline} subscription ended on a failure", pipeline);
			_viewModel?.ReportFailure(failure);
		};
	}

	// X-limit-only; repaint goes through RedrawRequested.
	private void OnNavigationWindowChanged(object? sender, NavigationWindow window)
	{
		ApplyWindow(window.From, window.To);
		ProjectMarkerLines(PlotControl.Bounds.Width);
	}

	private void OnPlotSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
	{
		RepositionCursorOverlay();
		ProjectMarkerLines(eventArgs.NewSize.Width);
	}

	private void CreateMarkerLines(TrendChartViewModel viewModel)
	{
		MarkerLines = new ChartMarkerLines(viewModel.Plot);
		_disposables.Add(Disposable.Create(MarkerLines.RemoveLines));
		var markerReadings = new ChartMarkerReadings();
		MarkerReadings = markerReadings;
		lock (viewModel.Plot.Sync)
		{
			viewModel.Plot.Add.Plottable(markerReadings);
		}

		_disposables.Add(Disposable.Create(() =>
		{
			lock (viewModel.Plot.Sync)
			{
				viewModel.Plot.Remove(markerReadings);
			}
		}));
		PaintMarkerLines();
		SyncMarkerLines(viewModel);

		_disposables.Add(viewModel.Markers.Changed
			.Subscribe(
				_ =>
				{
					SyncMarkerLines(viewModel);
					RequestFrame();
				},
				ReportFailureOf(nameof(ChartMarkers.Changed))));
	}

	private void SyncMarkerLines(TrendChartViewModel viewModel)
	{
		MarkerLines?.Sync(viewModel.Markers.ShownMarkers);
		ProjectMarkerLines(PlotControl.Bounds.Width);
	}

	// docs/architecture/trend-interaction.md#the-time-markers
	private void UpdateMarkerReadings(TrendChartViewModel viewModel)
	{
		var navigation = viewModel.Navigation;
		MarkerReadings?.Update(
			viewModel.Markers.ShownMarkers,
			viewModel.FindPen(viewModel.ActivePenId),
			viewModel.ActivePenReadingAt,
			navigation.From,
			navigation.To);
	}

	private void ProjectMarkerLines(double plotWidth)
	{
		if (MarkerLines is null || _viewModel is null)
		{
			return;
		}

		var navigation = _viewModel.Navigation;
		MarkerLines.Project(navigation.From, navigation.To, EstimatedDataAreaWidth(plotWidth));
	}

	private double EstimatedDataAreaWidth(double plotWidth)
	{
		var lastRender = PlotControl.Plot.LastRender;

		return plotWidth - (lastRender.FigureRect.Width - lastRender.DataRect.Width);
	}

	private void ApplyWindow(DateTime from, DateTime to)
	{
		PlotControl.Plot.Axes.SetLimitsX(LocalTimeAxis.ToAxis(from), LocalTimeAxis.ToAxis(to));
	}

	private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs eventArgs)
	{
		if (_viewModel is null)
		{
			return;
		}

		var position = eventArgs.GetPosition(PlotControl);
		var gesture = ChartWheelRouter.Route(IsOverActiveAxis(position), eventArgs.KeyModifiers, eventArgs.Delta);

		switch (gesture.Action)
		{
			case ChartWheelAction.ZoomTime:
				_viewModel.Navigation.ZoomAt(SpanFactorFor(gesture.Notch), AnchorAt(position));

				break;

			case ChartWheelAction.PanTime:
				PanTimeByNotch(gesture.Notch);

				break;

			case ChartWheelAction.ShiftActivePen:
				_viewModel.ShiftActivePen(WheelShiftFraction * gesture.Notch);

				break;

			case ChartWheelAction.ScaleActivePen:
				_viewModel.ScaleActivePen(SpanFactorFor(gesture.Notch), AnchorFractionAt(position));

				break;

			default:
				return;
		}

		eventArgs.Handled = true;
	}

	private static double SpanFactorFor(int notch)
	{
		return notch > 0 ? WheelNarrowFactor : WheelWidenFactor;
	}

	// Wheel down pans toward newer data.
	private void PanTimeByNotch(int notch)
	{
		var navigation = _viewModel!.Navigation;
		var width = navigation.To - navigation.From;
		navigation.PanBy(width * (WheelPanFraction * -notch));
	}

	// The axis region already bounds y to the data rect.
	private double AnchorFractionAt(Point position)
	{
		var dataRect = PlotControl.Plot.LastRender.DataRect;

		return (dataRect.Bottom - position.Y) / dataRect.Height;
	}

	private async void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
	{
		if (_viewModel is not { } viewModel
			|| _drag is not null
			|| !eventArgs.GetCurrentPoint(PlotControl).Properties.IsLeftButtonPressed)
		{
			return;
		}

		try
		{
			var position = eventArgs.GetPosition(PlotControl);
			var marker = MarkerAt(position);
			var action = ChartPressRouter.Route(
				PressRegionAt(position, marker),
				eventArgs.KeyModifiers,
				viewModel.ActiveLeftButtonTool,
				eventArgs.ClickCount);

			switch (action)
			{
				case ChartPressAction.EditAxisScale:
					OpenAxisScalePanel();
					eventArgs.Handled = true;

					break;

				case ChartPressAction.PlaceDeltaCursor:
					viewModel.PlaceDeltaCursor(AnchorAt(position));
					UpdateDeltaCursorLines();
					RequestFrame();

					break;

				case ChartPressAction.AddMarker:
					eventArgs.Handled = true;
					await viewModel.Markers.AddAtAsync(AnchorAt(position));

					break;

				case ChartPressAction.DeleteMarker when marker is not null:
					eventArgs.Handled = true;
					await viewModel.Markers.DeleteAsync(marker);

					break;

				default:
					BeginDrag(eventArgs, action, action == ChartPressAction.DragMarker ? marker : null);

					break;
			}
		}
		catch (Exception failure)
		{
			Log.Error(failure, "The chart view failed to handle a press");
			viewModel.ReportFailure(failure);
		}
	}

	private bool IsOverActiveAxis(Point position)
	{
		return _viewModel!.ActivePenAxis is { } axis
			&& ChartAxisRegion.TryCreate(PlotControl.Plot, axis) is { } region
			&& region.Contains((float)position.X, (float)position.Y);
	}

	private ChartPressRegion PressRegionAt(Point position, StoredMarker? marker)
	{
		if (IsOverActiveAxis(position))
		{
			return ChartPressRegion.ActivePenAxis;
		}

		if (ChartAxisRegion.TryCreateTimeAxis(PlotControl.Plot) is { } timeAxis
			&& timeAxis.Contains((float)position.X, (float)position.Y))
		{
			return ChartPressRegion.TimeAxis;
		}

		return marker is null ? ChartPressRegion.DataArea : MarkerRegion(marker);
	}

	private static ChartPressRegion MarkerRegion(StoredMarker marker)
	{
		return marker.IsLocked ? ChartPressRegion.LockedMarker : ChartPressRegion.Marker;
	}

	private StoredMarker? MarkerAt(Point position)
	{
		var dataRect = PlotControl.Plot.LastRender.DataRect;
		if (dataRect.Width <= 0f || !dataRect.Contains((float)position.X, (float)position.Y))
		{
			return null;
		}

		var time = AnchorAt(position);
		var tolerance = AnchorAt(new Point(position.X + MarkerHitPixels, position.Y)) - time;

		return _viewModel!.Markers.NearestWithin(time, tolerance);
	}

	private void OpenAxisScalePanel()
	{
		if (_viewModel!.AxisScale.Seed())
		{
			_axisScaleFlyout.ShowAt(PlotControl, true);
		}
	}

	private void OpenTimeWindowPanel(TrendChartViewModel viewModel)
	{
		viewModel.TimeWindow.Seed();
		_timeWindowFlyout.ShowAt(PlotControl, true);
	}

	private void BeginDrag(PointerPressedEventArgs eventArgs, ChartPressAction action, StoredMarker? marker)
	{
		_drag = new ChartDrag(
			_viewModel!,
			eventArgs.Pointer,
			eventArgs.GetPosition(PlotControl),
			action,
			IsStarted: false,
			marker);
		_viewModel!.BeginDrag();
		PlotControl.Cursor = action switch
		{
			ChartPressAction.ShiftActivePen => _penShiftCursor,
			ChartPressAction.DragMarker => _markerCursor,
			_ => _grabbingCursor
		};
		eventArgs.Pointer.Capture(PlotControl);

		HideCursorOverlay();
		RequestFrame();
	}

	private void OnPointerMoved(object? sender, PointerEventArgs eventArgs)
	{
		if (_viewModel is null)
		{
			return;
		}

		var current = eventArgs.GetPosition(PlotControl);

		if (_drag is { } drag)
		{
			if (eventArgs.Pointer != drag.Pointer
				|| (!drag.IsStarted && !ChartPressRouter.IsPastClickSlop(drag.Origin, current)))
			{
				return;
			}

			_drag = drag with { Origin = current, IsStarted = true };
			ContinueDrag(drag, current);

			return;
		}

		Hover(current, eventArgs.KeyModifiers);
	}

	private void Hover(Point current, KeyModifiers modifiers)
	{
		if (PlotControl.Plot.LastRender.DataRect.Width <= 0f)
		{
			return;
		}

		var marker = MarkerAt(current);
		var wouldDragMarker = marker is not null
			&& ChartPressRouter.Route(
				MarkerRegion(marker),
				modifiers,
				_viewModel!.ActiveLeftButtonTool,
				clickCount: 1) == ChartPressAction.DragMarker;
		PlotControl.Cursor = wouldDragMarker ? _markerCursor : _handCursor;

		_viewModel!.MoveCursor(marker?.TimeUtc ?? AnchorAt(current));
		RepositionCursorOverlay();
	}

	// The drag passed in is the one before this move, so IsStarted is false exactly on the first move past the slop.
	private void ContinueDrag(ChartDrag drag, Point current)
	{
		if (drag.Action == ChartPressAction.ShiftActivePen)
		{
			var dataAreaHeight = PlotControl.Plot.LastRender.DataRect.Height;
			drag.ViewModel.ShiftActivePen((drag.Origin.Y - current.Y) / dataAreaHeight);

			return;
		}

		if (drag is { Action: ChartPressAction.DragMarker, Marker: { } marker })
		{
			if (!drag.IsStarted)
			{
				drag.ViewModel.Markers.BeginMove(marker);
			}

			drag.ViewModel.Markers.Move(AnchorAt(current));

			return;
		}

		drag.ViewModel.Navigation.PanBy(AnchorAt(drag.Origin) - AnchorAt(current));
	}

	private void OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
	{
		if (eventArgs.InitialPressMouseButton == MouseButton.Right && _drag is null)
		{
			OpenChartMenu(eventArgs.GetPosition(PlotControl));

			return;
		}

		if (_drag is not { } drag
			|| eventArgs.Pointer != drag.Pointer
			|| eventArgs.InitialPressMouseButton != MouseButton.Left)
		{
			return;
		}

		// The view holds the capture here, so releasing it raises PointerCaptureLost, which ends the drag.
		eventArgs.Pointer.Capture(null);

		if (drag.IsStarted || drag.ViewModel != _viewModel)
		{
			return;
		}

		if (drag.Action is ChartPressAction.PanOrPickPen or ChartPressAction.DragMarker)
		{
			drag.ViewModel.PickPenAt(ToPixel(drag.Origin));
		}
		else if (drag.Action == ChartPressAction.PanOrEditTimeWindow)
		{
			OpenTimeWindowPanel(drag.ViewModel);
		}
	}

	// Capture can be lost mid-drag without a PointerReleased (window deactivation, focus steal); mirror the
	// release path's cleanup so drag state, grab cursor and hover suppression do not stay stuck.
	private async void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs eventArgs)
	{
		if (_drag is not { } drag || eventArgs.Pointer != drag.Pointer)
		{
			return;
		}

		try
		{
			await EndDragAsync(drag);
		}
		catch (Exception failure)
		{
			Log.Error(failure, "The chart view failed to end a drag");
			drag.ViewModel.ReportFailure(failure);
		}
	}

	// docs/architecture/trend-interaction.md#the-time-markers
	private Task EndDragAsync(ChartDrag drag)
	{
		_drag = null;
		PlotControl.Cursor = _handCursor;
		drag.ViewModel.EndDrag();

		return drag is { Action: ChartPressAction.DragMarker, IsStarted: true }
			? drag.ViewModel.Markers.EndMoveAsync()
			: Task.CompletedTask;
	}

	private void OnPointerExited(object? sender, PointerEventArgs eventArgs)
	{
		_viewModel?.ClearCursor();
		HideCursorOverlay();
	}

	private void CreateDeltaCursorLines(Plot plot)
	{
		_deltaFirstLine = plot.Add.VerticalLine(0.0);
		_deltaFirstLine.Axes.XAxis = plot.Axes.Bottom;
		_deltaFirstLine.IsVisible = false;

		_deltaSecondLine = plot.Add.VerticalLine(0.0);
		_deltaSecondLine.Axes.XAxis = plot.Axes.Bottom;
		_deltaSecondLine.IsVisible = false;
	}

	private void UpdateDeltaCursorLines()
	{
		PlaceDeltaLine(_deltaFirstLine, _viewModel?.DeltaFirstCursor);
		PlaceDeltaLine(_deltaSecondLine, _viewModel?.DeltaSecondCursor);
	}

	private static void PlaceDeltaLine(VerticalLine? line, DateTime? cursorTime)
	{
		if (line is null)
		{
			return;
		}

		if (cursorTime is { } time)
		{
			line.X = LocalTimeAxis.ToAxis(time);
			line.IsVisible = true;

			return;
		}

		line.IsVisible = false;
	}

	private void RepositionCursorOverlay()
	{
		if (_viewModel is null)
		{
			HideCursorOverlay();

			return;
		}

		var suppress = _viewModel.IsDragging || _viewModel.IsDeltaModeEnabled;
		if (suppress || _viewModel.CursorTime is not { } cursorTime)
		{
			HideCursorOverlay();

			return;
		}

		var dataRect = PlotControl.Plot.LastRender.DataRect;
		var cursorPixelX = PlotControl.Plot.GetPixel(new Coordinates(LocalTimeAxis.ToAxis(cursorTime), 0.0)).X;
		var placement = ChartCursorOverlay.Project(
			cursorPixelX,
			new DataRectPixels(dataRect.Left, dataRect.Right, dataRect.Top, dataRect.Bottom),
			PlotControl.Plot.ScaleFactor);

		ApplyOverlayPlacement(placement, cursorTime);
	}

	private void ApplyOverlayPlacement(OverlayPlacement placement, DateTime cursorTime)
	{
		if (!placement.IsVisible)
		{
			HideCursorOverlay();

			return;
		}

		CrosshairLine.StartPoint = new Point(placement.LineX, placement.LineTop);
		CrosshairLine.EndPoint = new Point(placement.LineX, placement.LineBottom);
		CrosshairLine.IsVisible = true;

		ReadoutText.Text = ChartHoverReadout.BuildContent(cursorTime, _viewModel!.CursorValues, _viewModel.Pens);
		Canvas.SetLeft(ReadoutBox, placement.LineX);
		Canvas.SetTop(ReadoutBox, placement.LineTop);
		ReadoutBox.IsVisible = true;
	}

	private void HideCursorOverlay()
	{
		CrosshairLine.IsVisible = false;
		ReadoutBox.IsVisible = false;
	}

	private static Pixel ToPixel(Point point)
	{
		return new Pixel((float)point.X, (float)point.Y);
	}

	private DateTime AnchorAt(Point position)
	{
		var x = PlotControl.Plot.GetCoordinates(ToPixel(position)).X;

		return LocalTimeAxis.FromAxis(x);
	}

	/// <summary>docs/architecture/trend-interaction.md#navigation--cursors</summary>
	private readonly record struct ChartDrag(
		TrendChartViewModel ViewModel,
		IPointer Pointer,
		Point Origin,
		ChartPressAction Action,
		bool IsStarted,
		StoredMarker? Marker);
}
