using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using ReactiveUI;

using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using ScottPlot.Rendering;
using ScottPlot.TickGenerators;

using Cursor = Avalonia.Input.Cursor;
using Line = Avalonia.Controls.Shapes.Line;

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

	private readonly CompositeDisposable _disposables = new();
	private TextBox? _axisBoundEditor;
	private bool _axisEditEditsMax;
	private Line? _crosshairLine;
	private VerticalLine? _deltaFirstLine;
	private VerticalLine? _deltaSecondLine;
	private Point? _dragOrigin;

	// Render-thread state: read and written by OnPlotRenderFinished, and reset on the UI thread when the
	// bound plot changes so the first frame of the new plot always reports.
	private float _lastRenderedDataAreaWidth = float.NaN;

	private AvaPlot? _plotControl;
	private Border? _readoutBox;
	private TextBlock? _readoutText;
	private TrendChartViewModel? _viewModel;

	public TrendChartView()
	{
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;
	}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
		_plotControl = this.FindControl<AvaPlot>("Plot");
		_axisBoundEditor = this.FindControl<TextBox>("AxisBoundEditor");
		_crosshairLine = this.FindControl<Line>("CrosshairLine");
		_readoutBox = this.FindControl<Border>("ReadoutBox");
		_readoutText = this.FindControl<TextBlock>("ReadoutText");

		if (_axisBoundEditor is not null)
		{
			_axisBoundEditor.KeyDown += OnAxisBoundEditorKeyDown;
			_axisBoundEditor.LostFocus += OnAxisBoundEditorLostFocus;
		}

		if (_plotControl is not null)
		{
			_plotControl.UserInputProcessor.Disable();
			// AvaPlot marks every wheel event handled in its class handler, which would keep
			// OnPointerWheelChanged below from ever running. Disabling the input processor does not
			// cover this: the flag is applied after it, unconditionally.
			_plotControl.HandleMouseWheelEvent = false;
			_plotControl.Cursor = _handCursor;
			_plotControl.PointerWheelChanged += OnPointerWheelChanged;
			_plotControl.PointerPressed += OnPointerPressed;
			_plotControl.PointerMoved += OnPointerMoved;
			_plotControl.PointerReleased += OnPointerReleased;
			_plotControl.PointerCaptureLost += OnPointerCaptureLost;
			_plotControl.PointerExited += OnPointerExited;
			_plotControl.SizeChanged += OnPlotSizeChanged;
		}
	}

	private void OnPlotSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
	{
		RepositionCursorOverlay();
	}

	// The canvas width is reported by the frame that carries it, not read back from Plot.LastRender, which
	// describes the frame already on screen. The plot draws inside an Avalonia custom draw operation, so
	// this runs on the render thread and the report is posted back to the UI thread against the view model
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

	// ScottPlot exposes its render events as plain delegate properties rather than events, so the removal
	// has to be written out to keep the empty-delegate contract instead of leaving null behind.
	private void UnsubscribeRenderFinished(RenderManager renderManager)
	{
		renderManager.RenderFinished =
			(renderManager.RenderFinished - OnPlotRenderFinished) ?? _noRenderFinishedHandler;
	}

	private void OnDataContextChanged(object? sender, EventArgs eventArgs)
	{
		_disposables.Clear();
		_viewModel = DataContext as TrendChartViewModel;

		if (_viewModel is null || _plotControl is null)
		{
			return;
		}

		_plotControl.Reset(_viewModel.Plot);

		_lastRenderedDataAreaWidth = float.NaN;
		var renderManager = _viewModel.Plot.RenderManager;
		renderManager.RenderFinished += OnPlotRenderFinished;
		_disposables.Add(Disposable.Create(() => UnsubscribeRenderFinished(renderManager)));

		ApplyLocalTimeTicks(_viewModel.Plot);
		CreateDeltaCursorLines(_viewModel.Plot);
		ApplyWindow(_viewModel.Navigation.From, _viewModel.Navigation.To);

		_viewModel.Navigation.WindowChanged += OnNavigationWindowChanged;
		_disposables.Add(Disposable.Create(() =>
			_viewModel.Navigation.WindowChanged -= OnNavigationWindowChanged));

		_disposables.Add(_viewModel
			.WhenAnyValue(viewModel => viewModel.IsDeltaModeEnabled)
			.Subscribe(_ => OnDeltaModeChanged()));

		_disposables.Add(_viewModel.RedrawRequested
			.Subscribe(_ =>
			{
				_plotControl.Refresh();
				RepositionCursorOverlay();
			}));
	}

	private void OnDeltaModeChanged()
	{
		UpdateDeltaCursorLines();
		RepositionCursorOverlay();
	}

	// X-limit-only update; repaint is routed through the throttled RedrawRequested seam to avoid a second
	// un-throttled re-render per pan/zoom step.
	private void OnNavigationWindowChanged(object? sender, NavigationWindow window)
	{
		ApplyWindow(window.From, window.To);
	}

	private void ApplyWindow(DateTime from, DateTime to)
	{
		_plotControl?.Plot.Axes.SetLimitsX(LocalTimeAxis.ToAxis(from), LocalTimeAxis.ToAxis(to));
	}

	// The plot control carries HandleMouseWheelEvent = false, so this handler is the only thing marking a
	// wheel event handled. This early return therefore lets the event bubble to an ancestor — inert while
	// nothing above the chart scrolls, and the place to look if the chart is ever put in a ScrollViewer.
	private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs eventArgs)
	{
		if (_viewModel is null || _plotControl is null)
		{
			return;
		}

		var anchor = AnchorAt(eventArgs.GetPosition(_plotControl));
		var factor = eventArgs.Delta.Y > 0 ? ZoomInFactor : ZoomOutFactor;
		_viewModel.Navigation.ZoomAt(factor, anchor);
		eventArgs.Handled = true;
	}

	private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
	{
		if (_viewModel is null || _plotControl is null
							   || !eventArgs.GetCurrentPoint(_plotControl).Properties.IsLeftButtonPressed)
		{
			return;
		}

		var position = eventArgs.GetPosition(_plotControl);
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
				_plotControl.Refresh();

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

		if (ChartAxisRegion.TryCreate(_plotControl!.Plot, axis) is not { } region
			|| !region.Contains((float)position.X, (float)position.Y))
		{
			return null;
		}

		return region;
	}

	private void BeginAxisBoundEdit(ChartAxisRegion region, Point position)
	{
		_axisEditEditsMax = region.IsUpperHalf((float)position.Y);
		ShowAxisBoundEditor(position, region.ValueAt((float)position.Y));
	}

	private void ShowAxisBoundEditor(Point position, double seedValue)
	{
		if (_axisBoundEditor is null)
		{
			return;
		}

		_axisBoundEditor.Text = seedValue.ToString("0.###");
		_axisBoundEditor.Margin = new Thickness(position.X, position.Y, 0.0, 0.0);
		_axisBoundEditor.IsVisible = true;
		_axisBoundEditor.Focus();
		_axisBoundEditor.SelectAll();
	}

	private void HideAxisBoundEditor()
	{
		if (_axisBoundEditor is not null)
		{
			_axisBoundEditor.IsVisible = false;
		}
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

	private void OnAxisBoundEditorLostFocus(object? sender, RoutedEventArgs eventArgs)
	{
		HideAxisBoundEditor();
	}

	private void CommitAxisBoundEditor()
	{
		if (_viewModel is null || _axisBoundEditor is null)
		{
			return;
		}

		if (double.TryParse(_axisBoundEditor.Text, out var typedBound)
			&& _viewModel.ScaleRangeForPen(_viewModel.ActivePenId) is { } currentRange)
		{
			var (min, max) = ChartAxisEdit.SeedManualLimits(typedBound, _axisEditEditsMax, currentRange);
			_viewModel.SetAxisLimits(_viewModel.ActivePenId, min, max);
		}

		HideAxisBoundEditor();
	}

	private void BeginPan(PointerPressedEventArgs eventArgs)
	{
		_dragOrigin = eventArgs.GetPosition(_plotControl!);
		_viewModel!.BeginDrag();
		_plotControl!.Cursor = _grabbingCursor;
		eventArgs.Pointer.Capture(_plotControl);

		HideCursorOverlay();
		_plotControl.Refresh();
	}

	private void OnPointerMoved(object? sender, PointerEventArgs eventArgs)
	{
		if (_viewModel is null || _plotControl is null)
		{
			return;
		}

		var current = eventArgs.GetPosition(_plotControl);

		if (_dragOrigin is { } origin)
		{
			var delta = AnchorAt(origin) - AnchorAt(current);
			_dragOrigin = current;
			_viewModel.Navigation.PanBy(delta);

			return;
		}

		MoveCursorTo(AnchorAt(current));
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

		if (_plotControl is not null)
		{
			_plotControl.Cursor = _handCursor;
		}
	}

	private void OnPointerExited(object? sender, PointerEventArgs eventArgs)
	{
		_viewModel?.ClearCursor();
		HideCursorOverlay();
	}

	// The tick generator is assigned in place rather than via Plot.Axes.DateTimeTicksBottom() (which
	// replaces the axis) so the shared bottom-X axis instance the plottables are pinned to is preserved.
	private static void ApplyLocalTimeTicks(Plot plot)
	{
		plot.Axes.Bottom.TickGenerator = new DateTimeAutomatic();
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

	private void MoveCursorTo(DateTime cursorTime)
	{
		_viewModel?.MoveCursor(cursorTime);
		RepositionCursorOverlay();
	}

	private void RepositionCursorOverlay()
	{
		if (_viewModel is null || _plotControl is null)
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

		var dataRect = _plotControl.Plot.LastRender.DataRect;
		var cursorPixelX = _plotControl.Plot.GetPixel(new Coordinates(LocalTimeAxis.ToAxis(cursorTime), 0.0)).X;
		var placement = ChartCursorOverlay.Project(
			cursorPixelX,
			new DataRectPixels(dataRect.Left, dataRect.Right, dataRect.Top, dataRect.Bottom),
			_plotControl.Plot.ScaleFactor);

		ApplyOverlayPlacement(placement, cursorTime);
	}

	private void ApplyOverlayPlacement(OverlayPlacement placement, DateTime cursorTime)
	{
		if (!placement.IsVisible)
		{
			HideCursorOverlay();

			return;
		}

		if (_crosshairLine is not null)
		{
			_crosshairLine.StartPoint = new Point(placement.LineX, placement.LineTop);
			_crosshairLine.EndPoint = new Point(placement.LineX, placement.LineBottom);
			_crosshairLine.IsVisible = true;
		}

		if (_readoutBox is not null && _readoutText is not null)
		{
			_readoutText.Text = ChartHoverReadout.BuildContent(cursorTime, _viewModel!.CursorValues, _viewModel.Pens);
			Canvas.SetLeft(_readoutBox, placement.LineX);
			Canvas.SetTop(_readoutBox, placement.LineTop);
			_readoutBox.IsVisible = true;
		}
	}

	private void HideCursorOverlay()
	{
		if (_crosshairLine is not null)
		{
			_crosshairLine.IsVisible = false;
		}

		if (_readoutBox is not null)
		{
			_readoutBox.IsVisible = false;
		}
	}

	private DateTime AnchorAt(Point position)
	{
		var x = _plotControl!.Plot.GetCoordinates(new Pixel((float)position.X, (float)position.Y)).X;

		return LocalTimeAxis.FromAxis(x);
	}
}
