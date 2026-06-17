using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using ScottPlot;
using ScottPlot.Avalonia;

namespace SemiPlot.UI.Chart;

// The only type that touches the AvaPlot control. ScottPlot's built-in mouse processing is disabled
// so scroll and drag are routed onto the navigation controller instead: scroll zooms about the cursor
// anchor, left-drag pans. The controller's window changes drive the bottom (time) axis limits, and a
// redraw is bound to the coalesced RedrawRequested stream.
public partial class TrendChartView : UserControl
{
	private const double ZoomInFactor = 0.8;
	private const double ZoomOutFactor = 1.25;

	private readonly CompositeDisposable _disposables = new();

	private AvaPlot? _plotControl;
	private TrendChartViewModel? _viewModel;
	private Point? _dragOrigin;
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

		if (_plotControl is not null)
		{
			_plotControl.UserInputProcessor.Disable();
			_plotControl.PointerWheelChanged += OnPointerWheelChanged;
			_plotControl.PointerPressed += OnPointerPressed;
			_plotControl.PointerMoved += OnPointerMoved;
			_plotControl.PointerReleased += OnPointerReleased;
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
		ApplyWindow(_viewModel.Navigation.From, _viewModel.Navigation.To);

		_viewModel.Navigation.WindowChanged += OnNavigationWindowChanged;
		_disposables.Add(Disposable.Create(() =>
			_viewModel.Navigation.WindowChanged -= OnNavigationWindowChanged));

		_disposables.Add(_viewModel.RedrawRequested
			.Subscribe(_ => _plotControl.Refresh()));
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

		if (_viewModel.DeltaCursorsEnabled)
		{
			_viewModel.PlaceDeltaCursor(AnchorAt(eventArgs.GetPosition(_plotControl)));
			UpdateDeltaCursorLines();
			_plotControl.Refresh();
			return;
		}

		_dragOrigin = eventArgs.GetPosition(_plotControl);
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
		_dragOrigin = null;
	}

	private void OnPointerExited(object? sender, PointerEventArgs eventArgs)
	{
		_viewModel?.ClearCursor();

		if (_cursorLine is not null)
		{
			_cursorLine.IsVisible = false;
		}

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

		if (_cursorLine is not null)
		{
			_cursorLine.X = LocalTimeAxis.ToAxis(cursorTime);
			_cursorLine.IsVisible = true;
		}

		_plotControl?.Refresh();
	}

	private DateTime AnchorAt(Point position)
	{
		var x = _plotControl!.Plot.GetCoordinates(new Pixel((float)position.X, (float)position.Y)).X;
		return LocalTimeAxis.FromAxis(x);
	}
}
