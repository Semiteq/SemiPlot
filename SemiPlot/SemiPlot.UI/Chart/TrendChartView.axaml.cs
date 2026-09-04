using System.Globalization;
using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using ReactiveUI;

using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.TickGenerators;

using Cursor = Avalonia.Input.Cursor;

namespace SemiPlot.UI.Chart;

public partial class TrendChartView : UserControl
{
	private const double ZoomInFactor = 0.8;
	private const double ZoomOutFactor = 1.25;

	private static readonly Cursor _handCursor = new(StandardCursorType.Hand);
	private static readonly Cursor _grabbingCursor = new(StandardCursorType.SizeAll);

	// ScottPlot's own constructor seeds the render events with an empty delegate; this restores that state
	// when the view drops the last handler.
	private static readonly EventHandler<RenderDetails> _noRenderFinishedHandler = (_, _) => { };

	private readonly CompositeDisposable _disposables = [];
	private bool _axisEditEditsMax;
	private VerticalLine? _deltaFirstLine;
	private VerticalLine? _deltaSecondLine;
	private Point? _dragOrigin;

	// Render-thread state: read and written by OnPlotRenderFinished, and reset on the UI thread when the
	// bound plot changes so the first frame of the new plot always reports.
	private float _lastRenderedDataAreaWidth = float.NaN;

	private TrendChartViewModel? _viewModel;

	public TrendChartView()
	{
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;

		AxisBoundEditor.KeyDown += OnAxisBoundEditorKeyDown;
		AxisBoundEditor.LostFocus += (_, _) => HideAxisBoundEditor();

		PlotControl.UserInputProcessor.Disable();
		// AvaPlot marks every wheel event handled in its class handler, which would keep
		// OnPointerWheelChanged below from ever running.
		PlotControl.HandleMouseWheelEvent = false;
		PlotControl.Cursor = _handCursor;
		PlotControl.PointerWheelChanged += OnPointerWheelChanged;
		PlotControl.PointerPressed += OnPointerPressed;
		PlotControl.PointerMoved += OnPointerMoved;
		PlotControl.PointerReleased += OnPointerReleased;
		PlotControl.PointerCaptureLost += OnPointerCaptureLost;
		PlotControl.PointerExited += OnPointerExited;
		PlotControl.SizeChanged += (_, _) => RepositionCursorOverlay();
	}

	// This runs on the render thread, so the report is posted back to the UI thread against the view model
	// the frame was drawn for, not whichever one is bound when the post is dispatched.
	private void OnPlotRenderFinished(object? sender, RenderDetails renderDetails)
	{
		var dataAreaWidth = renderDetails.DataRect.Width;
		if (dataAreaWidth.Equals(_lastRenderedDataAreaWidth))
		{
			return;
		}

		_lastRenderedDataAreaWidth = dataAreaWidth;
		var renderedViewModel = _viewModel;
		Dispatcher.UIThread.Post(() => renderedViewModel?.ReportDataAreaWidth(dataAreaWidth));
	}

	private void OnDataContextChanged(object? sender, EventArgs eventArgs)
	{
		_disposables.Clear();
		_viewModel = DataContext as TrendChartViewModel;

		if (_viewModel is null)
		{
			return;
		}

		PlotControl.Reset(_viewModel.Plot);

		_lastRenderedDataAreaWidth = float.NaN;
		var renderManager = _viewModel.Plot.RenderManager;
		renderManager.RenderFinished += OnPlotRenderFinished;

		// RenderFinished is a plain delegate property, so the removal keeps the empty-delegate contract
		// instead of leaving null behind.
		_disposables.Add(Disposable.Create(() => renderManager.RenderFinished =
			(renderManager.RenderFinished - OnPlotRenderFinished) ?? _noRenderFinishedHandler));

		// Assigned in place rather than via Plot.Axes.DateTimeTicksBottom(), which would replace the shared
		// bottom-X axis instance the plottables are pinned to.
		_viewModel.Plot.Axes.Bottom.TickGenerator = new DateTimeAutomatic();
		CreateDeltaCursorLines(_viewModel.Plot);
		ApplyWindow(_viewModel.Navigation.From, _viewModel.Navigation.To);

		_viewModel.Navigation.WindowChanged += OnNavigationWindowChanged;
		_disposables.Add(Disposable.Create(() =>
			_viewModel.Navigation.WindowChanged -= OnNavigationWindowChanged));

		_disposables.Add(_viewModel
			.WhenAnyValue(viewModel => viewModel.IsDeltaModeEnabled)
			.Subscribe(_ =>
			{
				UpdateDeltaCursorLines();
				RepositionCursorOverlay();
			}));

		_disposables.Add(_viewModel.RedrawRequested
			.Subscribe(_ =>
			{
				PlotControl.Refresh();
				RepositionCursorOverlay();
			}));
	}

	// X-limit-only; repaint goes through RedrawRequested.
	private void OnNavigationWindowChanged(object? sender, NavigationWindow window)
	{
		ApplyWindow(window.From, window.To);
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

		var anchor = AnchorAt(eventArgs.GetPosition(PlotControl));
		var factor = eventArgs.Delta.Y > 0 ? ZoomInFactor : ZoomOutFactor;
		_viewModel.Navigation.ZoomAt(factor, anchor);
		eventArgs.Handled = true;
	}

	private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
	{
		if (_viewModel is null || !eventArgs.GetCurrentPoint(PlotControl).Properties.IsLeftButtonPressed)
		{
			return;
		}

		var position = eventArgs.GetPosition(PlotControl);
		var region = ResolveAxisRegion(position);
		var action = ChartPressRouter.Route(region is not null, eventArgs.ClickCount, _viewModel.ActiveLeftButtonTool);

		switch (action)
		{
			case ChartPressAction.AutoscaleAxis:
				HideAxisBoundEditor();
				_viewModel.AutoscaleAxis(_viewModel.ActivePenId);
				eventArgs.Handled = true;

				break;

			case ChartPressAction.EditAxisBound:
				BeginAxisBoundEdit(region!, position);
				eventArgs.Handled = true;

				break;

			case ChartPressAction.PlaceDeltaCursor:
				_viewModel.PlaceDeltaCursor(AnchorAt(position));
				UpdateDeltaCursorLines();
				PlotControl.Refresh();

				break;

			default:
				BeginPan(eventArgs);

				break;
		}
	}

	private ChartAxisRegion? ResolveAxisRegion(Point position)
	{
		if (_viewModel!.ActivePenAxis is not { } axis)
		{
			return null;
		}

		if (ChartAxisRegion.TryCreate(PlotControl.Plot, axis) is not { } region
			|| !region.Contains((float)position.X, (float)position.Y))
		{
			return null;
		}

		return region;
	}

	private void BeginAxisBoundEdit(ChartAxisRegion region, Point position)
	{
		_axisEditEditsMax = region.IsUpperHalf((float)position.Y);

		AxisBoundEditor.Text = region.ValueAt((float)position.Y).ToString("0.###", CultureInfo.CurrentCulture);
		AxisBoundEditor.Margin = new Thickness(position.X, position.Y, 0.0, 0.0);
		AxisBoundEditor.IsVisible = true;
		AxisBoundEditor.Focus();
		AxisBoundEditor.SelectAll();
	}

	private void HideAxisBoundEditor()
	{
		AxisBoundEditor.IsVisible = false;
	}

	private void OnAxisBoundEditorKeyDown(object? sender, KeyEventArgs eventArgs)
	{
		if (eventArgs.Key == Key.Enter)
		{
			CommitAxisBoundEditor();
			eventArgs.Handled = true;

			return;
		}

		if (eventArgs.Key == Key.Escape)
		{
			HideAxisBoundEditor();
			eventArgs.Handled = true;
		}
	}

	private void CommitAxisBoundEditor()
	{
		if (_viewModel is null)
		{
			return;
		}

		if (double.TryParse(
				AxisBoundEditor.Text,
				NumberStyles.Float,
				CultureInfo.CurrentCulture,
				out var typedBound)
			&& _viewModel.ScaleRangeForPen(_viewModel.ActivePenId) is { } currentRange)
		{
			var (min, max) = ChartAxisEdit.SeedManualLimits(typedBound, _axisEditEditsMax, currentRange);
			_viewModel.SetAxisLimits(_viewModel.ActivePenId, min, max);
		}

		HideAxisBoundEditor();
	}

	private void BeginPan(PointerPressedEventArgs eventArgs)
	{
		_dragOrigin = eventArgs.GetPosition(PlotControl);
		_viewModel!.BeginDrag();
		PlotControl.Cursor = _grabbingCursor;
		eventArgs.Pointer.Capture(PlotControl);

		HideCursorOverlay();
		PlotControl.Refresh();
	}

	private void OnPointerMoved(object? sender, PointerEventArgs eventArgs)
	{
		if (_viewModel is null)
		{
			return;
		}

		var current = eventArgs.GetPosition(PlotControl);

		if (_dragOrigin is { } origin)
		{
			var delta = AnchorAt(origin) - AnchorAt(current);
			_dragOrigin = current;
			_viewModel.Navigation.PanBy(delta);

			return;
		}

		_viewModel.MoveCursor(AnchorAt(current));
		RepositionCursorOverlay();
	}

	private void OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
	{
		if (_dragOrigin is null)
		{
			return;
		}

		eventArgs.Pointer.Capture(null);
		EndDrag();
	}

	// Capture can be lost mid-drag without a PointerReleased (window deactivation, focus steal); mirror the
	// release path's cleanup so drag state, grab cursor and hover suppression do not stay stuck.
	private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs eventArgs)
	{
		if (_dragOrigin is null)
		{
			return;
		}

		EndDrag();
	}

	private void EndDrag()
	{
		_dragOrigin = null;
		_viewModel?.EndDrag();
		PlotControl.Cursor = _handCursor;
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

	private DateTime AnchorAt(Point position)
	{
		var x = PlotControl.Plot.GetCoordinates(new Pixel((float)position.X, (float)position.Y)).X;

		return LocalTimeAxis.FromAxis(x);
	}
}
