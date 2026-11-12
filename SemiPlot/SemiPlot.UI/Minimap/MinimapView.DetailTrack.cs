using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Minimap;

/// <summary>The detail track over the pager's page: the chart window, the runs, the ticks and the hover.</summary>
public partial class MinimapView
{
	// Floors a sub-pixel window or run so it stays visible.
	private const double MinimumHighlightWidth = 6.0;
	private const double MinimumRunWidth = 2.0;
	private const double WindowBorderWidth = 2.0;
	private const double TickLabelGap = 8.0;

	// A week of two-digit days, so the widest day tick label is among them.
	private static readonly DateTime _probeMonday = new(2000, 1, 24);

	private readonly Dictionary<TrackTickKind, double> _tickLabelSpacing = [];
	private double? _detailHoverFraction;

	private void AttachDetailTrackInput()
	{
		_ = new TrackPointer(StripCanvas, NavigateToStripX, HoverStripAt);
	}

	private CompositeDisposable FollowDetailTrack(MinimapViewModel viewModel)
	{
		return new CompositeDisposable(
			viewModel
				.WhenAnyValue(model => model.HasExtent, model => model.Pager.Page)
				.Subscribe(_ =>
				{
					LayoutPageTicks();
					PlaceDetailHover();
				}),
			viewModel
				.WhenAnyValue(model => model.HasExtent, model => model.Pager.Page, model => model.Pager.Window)
				.Subscribe(_ => PlaceWindowBox()),
			viewModel
				.WhenAnyValue(model => model.HasExtent, model => model.Pager.Page, model => model.OperatorMarkerTimes)
				.Subscribe(_ => PlaceTimeMarkerTicks()),
			viewModel
				.WhenAnyValue(model => model.HasExtent, model => model.Pager.Page, model => model.ShadedRuns)
				.Subscribe(_ => LayoutRuns()));
	}

	private void LayoutDetailTrack()
	{
		LayoutRuns();
		PlaceWindowBox();
		PlaceTimeMarkerTicks();
		LayoutPageTicks();
		PlaceDetailHover();
	}

	/// <summary>The view model while it has an extent, so the detail track has a page to draw.</summary>
	private MinimapViewModel? PagedViewModel()
	{
		return DataContext is MinimapViewModel { HasExtent: true } viewModel ? viewModel : null;
	}

	private void LayoutRuns()
	{
		var (width, height) = (StripCanvas.Bounds.Width, StripCanvas.Bounds.Height);
		RunShading.Width = width;
		RunShading.Height = height;
		RunShading.Shades = PagedViewModel() is { } viewModel
			? MinimapTrackGeometry.RunShades(viewModel.Pager.Page, viewModel.ShadedRuns, width, MinimumRunWidth)
			: [];
	}

	private void PlaceWindowBox()
	{
		var viewModel = PagedViewModel();
		var (windowFrom, windowTo) = viewModel?.Pager.Window ?? default;
		var box = viewModel is null
			? null
			: MinimapTrackGeometry.WindowBox(
				viewModel.Pager.Page, windowFrom, windowTo, StripCanvas.Bounds.Width, MinimumHighlightWidth);
		if (box is not { } shown)
		{
			WindowHighlight.IsVisible = false;

			return;
		}

		WindowHighlight.IsVisible = true;
		Canvas.SetLeft(WindowHighlight, shown.Left);
		WindowHighlight.Width = shown.Width;
		WindowHighlight.Height = StripCanvas.Bounds.Height;
		WindowHighlight.BorderThickness = new Thickness(
			shown.CutsLeft ? 0.0 : WindowBorderWidth,
			WindowBorderWidth,
			shown.CutsRight ? 0.0 : WindowBorderWidth,
			WindowBorderWidth);
	}

	/// <summary>One full-height tick per operator marker on the page; recipe markers shade their runs.</summary>
	private void PlaceTimeMarkerTicks()
	{
		var (width, height) = (StripCanvas.Bounds.Width, StripCanvas.Bounds.Height);
		var ticks = new GeometryGroup();
		if (PagedViewModel() is { } viewModel)
		{
			var page = viewModel.Pager.Page;
			foreach (var markerTime in viewModel.OperatorMarkerTimes)
			{
				if (MinimapGeometry.FractionAt(page.FromUtc, page.ToUtc, markerTime) is { } fraction)
				{
					var x = fraction * width;
					ticks.Children.Add(new LineGeometry(new Point(x, 0.0), new Point(x, height)));
				}
			}
		}

		TimeMarkerTicks.Data = ticks;
	}

	private void LayoutPageTicks()
	{
		var width = StripCanvas.Bounds.Width;
		if (PagedViewModel() is not { } viewModel || width <= 0.0)
		{
			TickLabels.Children.Clear();

			return;
		}

		var page = viewModel.Pager.Page;
		var kind = page.Unit == MinimapPageUnit.Day ? TrackTickKind.Hour : TrackTickKind.Day;
		var ticks = MinimapTrackGeometry.PageTicks(page, width, TickLabelSpacing(kind), viewModel.ArchiveZone);
		TrackLabels.Layout(TickLabels, ticks.Select(tick => (tick.X, MinimapLabels.Tick(tick))), width);
	}

	/// <summary>The widest label of the kind plus a gap, measured once.</summary>
	private double TickLabelSpacing(TrackTickKind kind)
	{
		if (!_tickLabelSpacing.TryGetValue(kind, out var spacing))
		{
			var count = kind == TrackTickKind.Hour ? DayHours.HoursPerDay : MinimapPage.DaysPerWeek;
			var texts = Enumerable.Range(0, count).Select(index => MinimapLabels.Tick(new TrackTick(
				0.0, kind == TrackTickKind.Hour ? _probeMonday.AddHours(index) : _probeMonday.AddDays(index), kind)));
			spacing = TrackLabels.Widest(TickLabels, texts) + TickLabelGap;
			_tickLabelSpacing[kind] = spacing;
		}

		return spacing;
	}

	private void PlaceDetailHover()
	{
		var width = StripCanvas.Bounds.Width;
		if (_detailHoverFraction is not { } fraction || PagedViewModel() is not { } viewModel || width <= 0.0)
		{
			HoverLine.IsVisible = false;
			HoverTimeLabel.IsVisible = false;
			TrackLabels.Cover(TickLabels, 0.0, 0.0);

			return;
		}

		var pointerX = fraction * width;
		HoverLine.IsVisible = true;
		Canvas.SetLeft(HoverLine, MinimapGeometry.SpanLeftWithin(pointerX, HoverLine.Width, width));
		HoverLine.Height = StripCanvas.Bounds.Height;

		var page = viewModel.Pager.Page;
		var hoverUtc = MinimapGeometry.TimeAtFraction(page.FromUtc, page.ToUtc, fraction);
		HoverTimeLabel.Text = MinimapLabels.HoverTime(
			ArchiveClock.WallClockOf(hoverUtc, viewModel.ArchiveZone), page.Unit);
		HoverTimeLabel.IsVisible = true;
		var labelWidth = TrackLabels.MeasuredWidth(HoverTimeLabel);
		var left = MinimapGeometry.SpanLeftWithin(pointerX - (labelWidth / 2.0), labelWidth, width);
		Canvas.SetLeft(HoverTimeLabel, left);
		TrackLabels.Cover(TickLabels, left, labelWidth);
	}

	private void HoverStripAt(double? x)
	{
		var width = StripCanvas.Bounds.Width;
		_detailHoverFraction = x is { } pointerX && width > 0.0 ? pointerX / width : null;
		PlaceDetailHover();
	}

	/// <summary>Centres the chart window at the pointer's fraction of the page.</summary>
	private void NavigateToStripX(double x)
	{
		var width = StripCanvas.Bounds.Width;
		if (PagedViewModel() is { } viewModel && width > 0.0)
		{
			viewModel.Pager.NavigateToPageFraction(x / width);
		}
	}
}
