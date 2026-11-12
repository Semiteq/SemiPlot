using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

namespace SemiPlot.UI.Minimap;

/// <summary>
/// The detail track's page under the chart window, and the steps that turn it and land the chart on data
/// (docs/architecture/trend-interaction.md#archive-overview-minimap).
/// </summary>
public sealed class MinimapPager : ReactiveObject, IDisposable
{
	private readonly ChartNavigationController _navigation;
	private readonly ArchivePresenceFeed _presence;
	private readonly TimeZoneInfo _archiveZone;
	private readonly CompositeDisposable _disposables = [];
	private readonly ObservableAsPropertyHelper<(DateTime From, DateTime To)> _window;
	private readonly ObservableAsPropertyHelper<MinimapPage> _page;
	private readonly ObservableAsPropertyHelper<MinimapPage?> _previousPage;
	private readonly ObservableAsPropertyHelper<MinimapPage?> _nextPage;

	internal MinimapPager(
		ChartNavigationController navigation,
		ArchivePresenceFeed presence,
		IObservable<(DateTime First, DateTime Last)?> extents,
		TimeZoneInfo archiveZone)
	{
		_navigation = navigation;
		_presence = presence;
		_archiveZone = archiveZone;
		NewestSample = navigation.NewestSample;

		var pagedSpans = presence
			.WhenAnyValue(feed => feed.Spans)
			.Select(spans => (Spans: spans, Hours: MinimapPage.WorkingHours(spans, archiveZone)));

		_disposables.Add(Windows().ToProperty(this, pager => pager.Window, out _window));
		_disposables.Add(Windows()
			.CombineLatest(pagedSpans, this.WhenAnyValue(pager => pager.NewestSample), PageOf)
			.ToProperty(this, pager => pager.Page, out _page));
		_disposables.Add(StepTargets(PageDirection.Previous, extents)
			.ToProperty(this, pager => pager.PreviousPage, out _previousPage));
		_disposables.Add(StepTargets(PageDirection.Next, extents)
			.ToProperty(this, pager => pager.NextPage, out _nextPage));

		_disposables.Add(PreviousPageCommand = ReactiveCommand.Create(
			() => TurnTo(PreviousPage),
			this.WhenAnyValue(pager => pager.PreviousPage).Select(target => target is not null)));
		_disposables.Add(NextPageCommand = ReactiveCommand.Create(
			() => TurnTo(NextPage),
			this.WhenAnyValue(pager => pager.NextPage).Select(target => target is not null)));
	}

	/// <summary>The chart window the detail track boxes.</summary>
	public (DateTime From, DateTime To) Window => _window.Value;

	/// <summary>
	/// The smallest page that holds the chart window up to the newest sample, <see cref="MinimapPage.Holding"/>.
	/// </summary>
	public MinimapPage Page => _page.Value;

	/// <summary>The navigation's newest sample as of the last <see cref="FollowNewestSample"/>.</summary>
	public DateTime? NewestSample
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>The page the previous step lands on; null when no earlier page holds rows.</summary>
	public MinimapPage? PreviousPage => _previousPage.Value;

	/// <summary>The page the next step lands on; null when no later page holds rows.</summary>
	public MinimapPage? NextPage => _nextPage.Value;

	public ReactiveCommand<Unit, Unit> PreviousPageCommand { get; }

	public ReactiveCommand<Unit, Unit> NextPageCommand { get; }

	public void Dispose()
	{
		_disposables.Dispose();
	}

	/// <summary>
	/// Takes the navigation's newest sample; the caller runs it after the day spans follow the sample, so the page
	/// never judges a sample its spans do not hold yet.
	/// </summary>
	public void FollowNewestSample()
	{
		NewestSample = _navigation.NewestSample;
	}

	/// <summary>
	/// Lands the chart on the calendar day when it holds rows, else on the day whose span lies nearer its noon,
	/// once the spans are read.
	/// </summary>
	public void NavigateToArchiveDay(DateOnly day)
	{
		var spans = _presence.Spans;
		if (spans.Count == 0 && _presence.IsExtentReadPending)
		{
			return;
		}

		var noonUtc = ArchiveClock.ToUtc(ArchiveClock.Midnight(day).AddHours(DayHours.HoursPerDay / 2), _archiveZone);
		var dayTimeUtc = MinimapPage.SnapToData(noonUtc, spans, _archiveZone)?.FirstUtc ?? noonUtc;
		TurnTo(MinimapPage.Snap(MinimapPageUnit.Day, dayTimeUtc, Page.Hours, spans, _archiveZone));
	}

	/// <summary>Centres the chart window at a page fraction clamped to [0, 1], its start kept on the page.</summary>
	public void NavigateToPageFraction(double fraction)
	{
		var page = Page;
		var width = _navigation.To - _navigation.From;
		var targetUtc = MinimapGeometry.TimeAtFraction(page.FromUtc, page.ToUtc, fraction);
		var fromUtc = targetUtc - (width / 2.0);
		fromUtc = fromUtc < page.FromUtc ? page.FromUtc : fromUtc;
		_navigation.SetWindow(fromUtc, fromUtc + width);
	}

	private MinimapPage PageOf(
		(DateTime From, DateTime To) window,
		(IReadOnlyList<ArchiveDaySpan> Spans, DayHours Hours) paged,
		DateTime? newestSample)
	{
		return MinimapPage.Holding(window.From, window.To, newestSample, paged.Hours, paged.Spans, _archiveZone);
	}

	private IObservable<(DateTime From, DateTime To)> Windows()
	{
		return Observable.Defer(() => Observable
			.FromEventPattern<NavigationWindow>(
				handler => _navigation.WindowChanged += handler,
				handler => _navigation.WindowChanged -= handler)
			.Select(change => (change.EventArgs.From, change.EventArgs.To))
			.StartWith((_navigation.From, _navigation.To)));
	}

	private IObservable<MinimapPage?> StepTargets(
		PageDirection direction,
		IObservable<(DateTime First, DateTime Last)?> extents)
	{
		return this
			.WhenAnyValue(pager => pager.Page)
			.CombineLatest(
				_presence.WhenAnyValue(
					feed => feed.Spans,
					feed => feed.IsExtentReadPending,
					(spans, isReadPending) => (Spans: spans, IsReadPending: isReadPending)),
				extents,
				(page, presence, extent) =>
					StepTarget(page, direction, presence.Spans, presence.IsReadPending, extent));
	}

	/// <summary>The nearest page with rows; without spans, the calendar neighbour once the first read lands.</summary>
	private MinimapPage? StepTarget(
		MinimapPage page,
		PageDirection direction,
		IReadOnlyList<ArchiveDaySpan> spans,
		bool isReadPending,
		(DateTime First, DateTime Last)? extent)
	{
		if (extent is not { } bounds || (spans.Count == 0 && isReadPending))
		{
			return null;
		}

		if (spans.Count > 0)
		{
			return MinimapPage.Step(page, direction, spans, _archiveZone);
		}

		// With no spans every page is a whole calendar day, week or month, so its edges lie inside the neighbours.
		var insideNeighbourUtc = direction == PageDirection.Next
			? page.ToUtc
			: page.FromUtc - TimeSpan.FromTicks(1);
		var neighbour = MinimapPage.Snap(page.Unit, insideNeighbourUtc, page.Hours, spans, _archiveZone);
		var meetsExtent = neighbour.FromUtc <= bounds.Last && neighbour.ToUtc > bounds.First;

		return meetsExtent ? neighbour : null;
	}

	private void TurnTo(MinimapPage? target)
	{
		if (target is { } page)
		{
			var (fromUtc, toUtc) =
				MinimapPage.Land(_navigation.From, _navigation.To, page, _presence.Spans, _archiveZone);
			_navigation.SetWindow(fromUtc, toUtc);
		}
	}
}
