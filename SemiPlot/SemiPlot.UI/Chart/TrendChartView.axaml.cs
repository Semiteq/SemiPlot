using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using ReactiveUI;

using ScottPlot;
using ScottPlot.Avalonia;

using Cursor = Avalonia.Input.Cursor;

namespace SemiPlot.UI.Chart;

// The only type that touches the AvaPlot control. ScottPlot's built-in mouse processing is disabled
// so scroll and drag are routed onto the navigation controller instead: scroll zooms about the cursor
// anchor, left-drag pans. The controller's window changes drive the bottom (time) axis limits, and a
// redraw is bound to the coalesced RedrawRequested stream.
public partial class TrendChartView : UserControl
{
	private const double ZoomInFactor = 0.8;
	private const double ZoomOutFactor = 1.25;

	private static readonly Cursor _handCursor = new(StandardCursorType.Hand);
	private static readonly Cursor _grabbingCursor = new(StandardCursorType.SizeAll);

	private readonly CompositeDisposable _disposables = new();

	private AvaPlot? _plotControl;
	private TextBox? _axisBoundEditor;
	private TrendChartViewModel? _viewModel;
	private Point? _dragOrigin;
	private bool _axisEditEditsMax;
	private ChartHoverReadout? _hoverReadout;
	private ScottPlot.Plottables.VerticalLine? _cursorLine;
	private ScottPlot.Plottables.VerticalLine? _deltaFirstLine;
	private ScottPlot.Plottables.VerticalLine? _deltaSecondLine;

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

		if (_axisBoundEditor is not null)
		{
			_axisBoundEditor.KeyDown += OnAxisBoundEditorKeyDown;
			_axisBoundEditor.LostFocus += OnAxisBoundEditorLostFocus;
		}

		if (_plotControl is not null)
		{
			_plotControl.UserInputProcessor.Disable();
			_plotControl.Cursor = _handCursor;
			_plotControl.PointerWheelChanged += OnPointerWheelChanged;
			_plotControl.PointerPressed += OnPointerPressed;
			_plotControl.PointerMoved += OnPointerMoved;
			_plotControl.PointerReleased += OnPointerReleased;
			_plotControl.PointerCaptureLost += OnPointerCaptureLost;
			_plotControl.PointerExited += OnPointerExited;
		}
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
		ApplyLocalTimeTicks(_viewModel.Plot);
		CreateCursorLine(_viewModel.Plot);
		CreateDeltaCursorLines(_viewModel.Plot);
		_hoverReadout = new ChartHoverReadout(_viewModel.Plot);
		ApplyWindow(_viewModel.Navigation.From, _viewModel.Navigation.To);

		_viewModel.Navigation.WindowChanged += OnNavigationWindowChanged;
		_disposables.Add(Disposable.Create(() =>
			_viewModel.Navigation.WindowChanged -= OnNavigationWindowChanged));

		_disposables.Add(_viewModel
			.WhenAnyValue(viewModel => viewModel.IsDeltaModeEnabled)
			.Subscribe(_ => OnDeltaModeChanged()));

		_disposables.Add(_viewModel.RedrawRequested
			.Subscribe(_ => _plotControl.Refresh()));
	}

	// Leaving delta mode clears the placed cursors in the view model, so the drawn delta lines are
	// hidden to match; entering mode re-syncs them from whatever state the reader holds.
	private void OnDeltaModeChanged()
	{
		UpdateDeltaCursorLines();
		UpdateHoverReadout();
		_plotControl?.Refresh();
	}

	private void OnNavigationWindowChanged(object? sender, NavigationWindow window)
	{
		ApplyWindow(window.From, window.To);
		_plotControl?.Refresh();
	}

	private void ApplyWindow(DateTime from, DateTime to)
	{
		_plotControl?.Plot.Axes.SetLimitsX(LocalTimeAxis.ToAxis(from), LocalTimeAxis.ToAxis(to));
	}

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

	// Resolves the active pen's Y-axis region only when the press lands inside its panel band; null when
	// the press is over the data area (a pan/delta) or before any render. A press inside the band is an
	// axis-range edit, never a pan or a delta-cursor placement.
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

	// A single click in the upper half edits MAX, the lower half edits MIN.
	private void BeginAxisBoundEdit(ChartAxisRegion region, Point position)
	{
		_axisEditEditsMax = region.IsUpperHalf((float)position.Y);
		ShowAxisBoundEditor(position, region.ValueAt((float)position.Y));
	}

	// Opens the inline numeric editor seeded with the value the operator clicked at on the axis, anchored
	// at the press position; committing feeds PenScaleModel manual limits for the active pen's axis.
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

		if (_cursorLine is not null)
		{
			_cursorLine.IsVisible = false;
		}

		UpdateHoverReadout();
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

	// Pointer capture can be lost mid-drag without a PointerReleased (window deactivation, focus steal).
	// Without this the drag state, grab cursor and hover suppression would stay stuck, so the same cleanup
	// the release path performs is run here.
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

		if (_cursorLine is not null)
		{
			_cursorLine.IsVisible = false;
		}

		UpdateHoverReadout();
		_plotControl?.Refresh();
	}

	// The plotted X coordinates are local-time OADates (LocalTimeAxis), so a DateTime tick generator on
	// the existing bottom axis renders human-readable local-time labels. The generator is assigned in
	// place rather than via Plot.Axes.DateTimeTicksBottom() (which replaces the axis) so the shared
	// bottom-X axis instance the plottables are pinned to is preserved.
	private static void ApplyLocalTimeTicks(ScottPlot.Plot plot)
	{
		plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.DateTimeAutomatic();
	}

	private void CreateCursorLine(ScottPlot.Plot plot)
	{
		_cursorLine = plot.Add.VerticalLine(0.0);
		_cursorLine.Axes.XAxis = plot.Axes.Bottom;
		_cursorLine.IsVisible = false;
	}

	private void CreateDeltaCursorLines(ScottPlot.Plot plot)
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

	private static void PlaceDeltaLine(ScottPlot.Plottables.VerticalLine? line, DateTime? cursorTime)
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

		// The plain hover line is suppressed in delta mode (and while dragging) so only the delta cursors
		// are shown; matching the on-chart readout suppression in UpdateHoverReadout.
		var showHoverLine = _viewModel is { IsDragging: false, IsDeltaModeEnabled: false };
		if (_cursorLine is not null)
		{
			_cursorLine.X = LocalTimeAxis.ToAxis(cursorTime);
			_cursorLine.IsVisible = showHoverLine;
		}

		UpdateHoverReadout();
		_plotControl?.Refresh();
	}

	// Re-feeds the on-chart all-pens readout from the view model's synchronously-computed cursor state.
	// It is suppressed while a hand-pan drag is in progress or delta mode is active, so only the plain
	// hover X-trace surfaces it.
	private void UpdateHoverReadout()
	{
		if (_viewModel is null || _hoverReadout is null)
		{
			return;
		}

		var suppress = _viewModel.IsDragging || _viewModel.IsDeltaModeEnabled;
		_hoverReadout.Update(_viewModel.CursorTime, _viewModel.CursorValues, _viewModel.Pens, suppress);
	}

	private DateTime AnchorAt(Point position)
	{
		var x = _plotControl!.Plot.GetCoordinates(new Pixel((float)position.X, (float)position.Y)).X;
		return LocalTimeAxis.FromAxis(x);
	}
}
