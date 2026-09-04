using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

using ReactiveUI;

namespace SemiPlot.UI.Minimap;

public partial class MinimapView : UserControl
{
	// Floors a sub-pixel window fraction so the marker stays visible.
	private const double MinimumHighlightWidth = 6.0;
	private const double LabelEdgePadding = 4.0;

	private readonly CompositeDisposable _disposables = [];
	private bool _isDragging;

	public MinimapView()
	{
		InitializeComponent();

		StripCanvas.PointerPressed += OnPointerPressed;
		StripCanvas.PointerMoved += OnPointerMoved;
		StripCanvas.PointerReleased += OnPointerReleased;
		StripCanvas.PointerCaptureLost += OnPointerCaptureLost;

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
		if (DataContext is not MinimapViewModel viewModel)
		{
			return;
		}

		var width = StripCanvas.Bounds.Width;
		var height = StripCanvas.Bounds.Height;

		LayoutBaseline(width, height);
		LayoutEndLabel(width, height);

		if (!viewModel.HasExtent)
		{
			WindowHighlight.IsVisible = false;

			return;
		}

		WindowHighlight.IsVisible = true;
		Canvas.SetLeft(WindowHighlight, viewModel.WindowStartFraction * width);
		WindowHighlight.Width = Math.Max(MinimumHighlightWidth, viewModel.WindowWidthFraction * width);
		WindowHighlight.Height = height;
	}

	private void LayoutBaseline(double width, double height)
	{
		Baseline.Width = width;
		Canvas.SetTop(Baseline, height / 2.0);
	}

	private void LayoutEndLabel(double width, double height)
	{
		ExtentLastLabel.Measure(new Size(width, height));
		Canvas.SetLeft(ExtentLastLabel, width - ExtentLastLabel.DesiredSize.Width - LabelEdgePadding);
	}

	private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
	{
		if (!args.GetCurrentPoint(StripCanvas).Properties.IsLeftButtonPressed)
		{
			return;
		}

		_isDragging = true;
		args.Pointer.Capture(StripCanvas);
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

	// Capture can be lost mid-drag without a PointerReleased (window deactivation); clear the drag flag.
	private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs args)
	{
		_isDragging = false;
	}

	private void NavigateToPointer(PointerEventArgs args)
	{
		if (DataContext is not MinimapViewModel viewModel)
		{
			return;
		}

		var width = StripCanvas.Bounds.Width;
		if (width <= 0.0)
		{
			return;
		}

		var fraction = args.GetPosition(StripCanvas).X / width;
		viewModel.NavigateToFraction(fraction);
	}
}
