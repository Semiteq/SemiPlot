using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;

using ReactiveUI;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Navigation;

namespace SemiPlot.UI.Minimap;

/// <summary>The day calendar over the extent, its labels, its hover and its press.</summary>
public partial class DayCalendarView : UserControl
{
	private readonly CompositeDisposable _disposables = [];
	private DayCalendar? _calendar;
	private IReadOnlyDictionary<DateOnly, int> _markerCountsByDay = new Dictionary<DateOnly, int>();
	// Measured from the row's right end, where the cells hang, so a resize keeps the ring on its cell.
	private double? _hoverFromRight;

	public DayCalendarView()
	{
		InitializeComponent();

		_ = new TrackPointer(CalendarCellRow, NavigateToX, HoverAt);

		DataContextChanged += OnDataContextChanged;
	}

	private void OnDataContextChanged(object? sender, EventArgs e)
	{
		_disposables.Clear();

		if (DataContext is MinimapViewModel viewModel)
		{
			_disposables.Add(FollowCalendar(viewModel));
		}
	}

	/// <summary>Assigns the cells only when they change, so a live sample redraws them across a step only.</summary>
	private CompositeDisposable FollowCalendar(MinimapViewModel viewModel)
	{
		var calendars = viewModel
			.WhenAnyValue(model => model.HasExtent, model => model.ExtentFirst, model => model.ExtentLast)
			.CombineLatest(
				CalendarCellRow.GetObservable(BoundsProperty),
				(_, bounds) => viewModel.Calendar(bounds.Width))
			.DistinctUntilChanged()
			.Replay(1)
			.RefCount();
		var pager = viewModel.Pager;
		var spans = viewModel.PresenceFeed.WhenAnyValue(feed => feed.Spans);
		var hours = pager
			.WhenAnyValue(model => model.Page)
			.Select(page => page.Hours)
			.DistinctUntilChanged();

		return new CompositeDisposable(
			calendars.Subscribe(ApplyCalendar),
			calendars
				.CombineLatest(spans, hours)
				.Select(change => change.First is { } calendar
					? calendar.Cells(change.Second, change.Third, viewModel.ArchiveZone)
					: [])
				.DistinctUntilChanged(SequenceComparer<CalendarCell>.Instance)
				.Subscribe(cells => CalendarCells.Cells = cells),
			calendars
				.CombineLatest(pager.WhenAnyValue(model => model.Page))
				.Subscribe(change => CalendarCells.PageOutline = change.First?.PageOutline(change.Second)),
			calendars
				.CombineLatest(viewModel.WhenAnyValue(model => model.OperatorMarkerTimes))
				.Subscribe(change => CalendarCells.MarkerTicks =
					change.First?.MarkerTicks(change.Second, viewModel.ArchiveZone) ?? []),
			viewModel
				.WhenAnyValue(model => model.OperatorMarkerTimes)
				.Subscribe(times =>
				{
					_markerCountsByDay = DayCalendar.MarkerCountsByDay(times, viewModel.ArchiveZone);
					PlaceHover();
				}),
			spans.Subscribe(_ => PlaceHover()));
	}

	private void ApplyCalendar(DayCalendar? calendar)
	{
		_calendar = calendar;
		CalendarCells.MonthSeparators = calendar?.MonthSeparators() ?? [];
		LayoutLabels(calendar);
		PlaceHover();
	}

	/// <summary>Measures every candidate on the row, so the row's text style applies, and keeps those that fit.</summary>
	private void LayoutLabels(DayCalendar? shown)
	{
		CalendarLabels.Children.Clear();
		if (shown is not { } calendar)
		{
			return;
		}

		var candidates = DayCalendarLabels.Candidates(calendar);
		var blocks = candidates.Select(label => new TextBlock { Text = MinimapLabels.Calendar(label) }).ToList();
		CalendarLabels.Children.AddRange(blocks);
		var widths = blocks.Select(TrackLabels.MeasuredWidth).ToList();
		var fitted = DayCalendarLabels.Fitted(candidates, widths, calendar.Right);
		CalendarLabels.Children.Clear();
		foreach (var label in fitted)
		{
			Canvas.SetLeft(blocks[label.Index], label.Left);
			CalendarLabels.Children.Add(blocks[label.Index]);
		}
	}

	/// <summary>Rings the cell under the pointer and names its day, rows and markers in the row's tooltip.</summary>
	private void PlaceHover()
	{
		if (_hoverFromRight is not { } fromRight
			|| _calendar is not { } calendar
			|| DataContext is not MinimapViewModel viewModel
			|| calendar.DayAt(calendar.Right - fromRight) is not { } day)
		{
			CalendarCells.HoveredCell = null;
			ToolTip.SetTip(CalendarCellRow, null);

			return;
		}

		CalendarCells.HoveredCell = calendar.Cell(calendar.IndexOf(day));
		var span = viewModel.PresenceFeed.Spans.FirstOrDefault(candidate => candidate.Day == day);
		var tip = MinimapLabels.DayTip(day, span, _markerCountsByDay.GetValueOrDefault(day), viewModel.ArchiveZone);
		if (!Equals(ToolTip.GetTip(CalendarCellRow), tip))
		{
			ToolTip.SetTip(CalendarCellRow, tip);
		}
	}

	private void HoverAt(double? x)
	{
		_hoverFromRight = CalendarCellRow.Bounds.Width - x;
		PlaceHover();
	}

	private void NavigateToX(double x)
	{
		if (DataContext is MinimapViewModel viewModel && _calendar?.DayAt(x) is { } day)
		{
			viewModel.Pager.NavigateToArchiveDay(day);
		}
	}
}
