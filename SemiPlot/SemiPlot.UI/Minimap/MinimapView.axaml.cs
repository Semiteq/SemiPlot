using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

using ReactiveUI;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Minimap;

public partial class MinimapView : UserControl
{
	// Floors a sub-pixel window fraction so the marker stays visible.
	private const double MinimumHighlightWidth = 6.0;
	private const double EndLabelCoverDistance = 6.0;

	private readonly CompositeDisposable _disposables = [];
	private bool _isDragging;

	public MinimapView()
	{
		InitializeComponent();

		StripCanvas.PointerPressed += OnPointerPressed;
		StripCanvas.PointerMoved += OnPointerMoved;
		StripCanvas.PointerReleased += OnPointerReleased;
		StripCanvas.PointerCaptureLost += OnPointerCaptureLost;
		StripCanvas.PointerExited += OnPointerExited;

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
				model => model.HasExtent,
				model => model.ExtentFirst,
				model => model.ExtentLast,
				model => model.BandFeed.Band)
			.Subscribe(_ => LayoutBand()));
		_disposables.Add(viewModel
			.WhenAnyValue(model => model.WindowStartFraction, model => model.WindowWidthFraction, model => model.HasExtent)
			.Subscribe(_ => PlaceMarker()));
		_disposables.Add(viewModel
			.WhenAnyValue(model => model.HoverFraction, model => model.HoverLabel)
			.Subscribe(_ => PlaceHover()));
	}

	private void UpdateStrip()
	{
		LayoutBand();
		PlaceMarker();
		PlaceHover();
	}

	private void LayoutBand()
	{
		if (DataContext is not MinimapViewModel viewModel)
		{
			return;
		}

		var (width, height) = (StripCanvas.Bounds.Width, StripCanvas.Bounds.Height);
		BandLayer.Width = width;
		BandLayer.Height = height;
		BandLayer.Figures = viewModel is { HasExtent: true, BandFeed.Band: { } band }
			? MinimapGeometry.BandFigures(band, viewModel.ExtentFirst, viewModel.ExtentLast, width, height)
			: [];
	}

	private void PlaceMarker()
	{
		if (DataContext is not MinimapViewModel viewModel)
		{
			return;
		}

		if (!viewModel.HasExtent)
		{
			WindowHighlight.IsVisible = false;

			return;
		}

		var stripWidth = StripCanvas.Bounds.Width;
		var (left, width) = MinimapGeometry.MarkerSpan(
			viewModel.WindowStartFraction, viewModel.WindowWidthFraction, stripWidth, MinimumHighlightWidth);
		WindowHighlight.IsVisible = true;
		Canvas.SetLeft(WindowHighlight, left);
		WindowHighlight.Width = width;
		WindowHighlight.Height = StripCanvas.Bounds.Height;
	}

	private void PlaceHover()
	{
		if (DataContext is not MinimapViewModel viewModel)
		{
			return;
		}

		if (viewModel.HoverFraction is not { } fraction)
		{
			HideHover();

			return;
		}

		var stripWidth = StripCanvas.Bounds.Width;
		var pointerX = fraction * stripWidth;
		HoverLine.IsVisible = true;
		Canvas.SetLeft(HoverLine, MinimapGeometry.SpanLeftWithin(pointerX, HoverLine.Width, stripWidth));
		HoverLine.Height = StripCanvas.Bounds.Height;

		PlaceHoverTime(viewModel.HoverLabel, pointerX + StripCanvas.Bounds.X - LabelRow.Bounds.X);
	}

	// docs/architecture/trend-interaction.md#archive-overview-minimap
	private void PlaceHoverTime(string text, double rowPointerX)
	{
		HoverTimeLabel.Text = text;
		HoverTimeLabel.IsVisible = true;

		var placement = MinimapGeometry.PlaceHoverTime(
			rowPointerX,
			MeasuredWidth(HoverTimeLabel),
			LabelRow.Bounds.Width,
			MeasuredWidth(ExtentFirstLabel),
			MeasuredWidth(ExtentLastLabel),
			EndLabelCoverDistance);
		Canvas.SetLeft(HoverTimeLabel, placement.Left);
		ExtentFirstLabel.Opacity = placement.CoversFirst ? 0.0 : 1.0;
		ExtentLastLabel.Opacity = placement.CoversLast ? 0.0 : 1.0;
	}

	private static double MeasuredWidth(Control control)
	{
		control.Measure(Size.Infinity);

		return control.DesiredSize.Width;
	}

	private void HideHover()
	{
		HoverLine.IsVisible = false;
		HoverTimeLabel.IsVisible = false;
		ExtentFirstLabel.Opacity = 1.0;
		ExtentLastLabel.Opacity = 1.0;
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

		HoverAtPointer(args);
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

	// docs/architecture/trend-interaction.md#archive-overview-minimap
	private void OnPointerExited(object? sender, PointerEventArgs args)
	{
		if (!_isDragging && DataContext is MinimapViewModel viewModel)
		{
			viewModel.ClearHover();
		}
	}

	private void HoverAtPointer(PointerEventArgs args)
	{
		if (DataContext is not MinimapViewModel viewModel)
		{
			return;
		}

		var position = args.GetPosition(StripCanvas);
		if (new Rect(StripCanvas.Bounds.Size).Contains(position) && StripFraction(position) is { } fraction)
		{
			viewModel.HoverAt(fraction);
		}
		else
		{
			viewModel.ClearHover();
		}
	}

	private void NavigateToPointer(PointerEventArgs args)
	{
		if (DataContext is MinimapViewModel viewModel && StripFraction(args.GetPosition(StripCanvas)) is { } fraction)
		{
			viewModel.NavigateToFraction(fraction);
		}
	}

	private double? StripFraction(Point position)
	{
		var width = StripCanvas.Bounds.Width;

		return width > 0.0 ? position.X / width : null;
	}
}
