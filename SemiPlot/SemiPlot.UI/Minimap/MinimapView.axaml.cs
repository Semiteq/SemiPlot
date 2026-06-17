using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using ReactiveUI;

namespace SemiPlot.UI.Minimap;

// The archive-overview strip: the highlight rectangle is drawn from the view model's window fractions
// scaled by the control width, and a press/drag converts the pointer X into a fraction to navigate.
public partial class MinimapView : UserControl
{
	// A narrow window over a wide extent maps to a sub-pixel fraction; this floor keeps the marker
	// visible so the strip never reads as an empty bar.
	private const double MinimumHighlightWidth = 6.0;
	private const double LabelEdgePadding = 4.0;

	private readonly CompositeDisposable _disposables = new();
	private Canvas? _stripCanvas;
	private Border? _windowHighlight;
	private Rectangle? _baseline;
	private TextBlock? _extentLastLabel;
	private bool _isDragging;

	public MinimapView()
	{
		InitializeComponent();
	}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
		_stripCanvas = this.FindControl<Canvas>("StripCanvas");
		_windowHighlight = this.FindControl<Border>("WindowHighlight");
		_baseline = this.FindControl<Rectangle>("Baseline");
		_extentLastLabel = this.FindControl<TextBlock>("ExtentLastLabel");

		if (_stripCanvas is not null)
		{
			_stripCanvas.PointerPressed += OnPointerPressed;
			_stripCanvas.PointerMoved += OnPointerMoved;
			_stripCanvas.PointerReleased += OnPointerReleased;
			_stripCanvas.PointerCaptureLost += OnPointerCaptureLost;
		}

		this.GetObservable(BoundsProperty).Subscribe(_ => UpdateStrip());

		DataContextChanged += OnDataContextChanged;
	}

	private void OnDataContextChanged(object? sender, EventArgs e)
	{
		_disposables.Clear();

		if (DataContext is not MinimapViewModel viewModel)
		{
			return;
		}

		_disposables.Add(viewModel
			.WhenAnyValue(
				model => model.WindowStartFraction,
				model => model.WindowWidthFraction,
				model => model.HasExtent)
			.Subscribe(_ => UpdateStrip()));
	}

	private void UpdateStrip()
	{
		if (_stripCanvas is null || _windowHighlight is null || DataContext is not MinimapViewModel viewModel)
		{
			return;
		}

		var width = _stripCanvas.Bounds.Width;
		var height = _stripCanvas.Bounds.Height;

		LayoutBaseline(width, height);
		LayoutEndLabel(width, height);

		// No extent yet means the navigation window has no anchor on the strip, so the highlight is hidden
		// rather than defaulting to a misleading full-width selection.
		if (!viewModel.HasExtent)
		{
			_windowHighlight.IsVisible = false;
			return;
		}

		_windowHighlight.IsVisible = true;
		Canvas.SetLeft(_windowHighlight, viewModel.WindowStartFraction * width);
		_windowHighlight.Width = Math.Max(MinimumHighlightWidth, viewModel.WindowWidthFraction * width);
		_windowHighlight.Height = height;
	}

	private void LayoutBaseline(double width, double height)
	{
		if (_baseline is null)
		{
			return;
		}

		_baseline.Width = width;
		Canvas.SetTop(_baseline, height / 2.0);
	}

	// The first-end label is left-anchored in the XAML; the last-end label is right-anchored here because
	// its offset depends on the measured text width.
	private void LayoutEndLabel(double width, double height)
	{
		if (_extentLastLabel is null)
		{
			return;
		}

		_extentLastLabel.Measure(new Size(width, height));
		Canvas.SetLeft(_extentLastLabel, width - _extentLastLabel.DesiredSize.Width - LabelEdgePadding);
	}

	private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
	{
		if (_stripCanvas is null || !args.GetCurrentPoint(_stripCanvas).Properties.IsLeftButtonPressed)
		{
			return;
		}

		_isDragging = true;
		args.Pointer.Capture(_stripCanvas);
		NavigateToPointer(args);
		args.Handled = true;
	}

	private void OnPointerMoved(object? sender, PointerEventArgs args)
	{
		if (_isDragging)
		{
			NavigateToPointer(args);
		}
	}

	private void OnPointerReleased(object? sender, PointerReleasedEventArgs args)
	{
		if (!_isDragging)
		{
			return;
		}

		args.Pointer.Capture(null);
		_isDragging = false;
	}

	// Capture can be lost mid-drag without a PointerReleased (window deactivation); clear the drag flag so
	// the strip does not stay stuck following the pointer.
	private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs args)
	{
		_isDragging = false;
	}

	private void NavigateToPointer(PointerEventArgs args)
	{
		if (_stripCanvas is null || DataContext is not MinimapViewModel viewModel)
		{
			return;
		}

		var width = _stripCanvas.Bounds.Width;
		if (width <= 0.0)
		{
			return;
		}

		var fraction = args.GetPosition(_stripCanvas).X / width;
		viewModel.NavigateToFraction(fraction);
	}
}
