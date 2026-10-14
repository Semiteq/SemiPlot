namespace SemiPlot.UI.Chart;

// docs/architecture/data-integration.md#what-one-history-query-covers
/// <summary>The chart's one history request decision, over the range in hand and the pens read for it.</summary>
internal sealed class ChartHistoryPlanner(ChartNavigationController navigation)
{
	private readonly ChartNavigationController _navigation = navigation;
	private readonly HashSet<int> _fetchedPenIds = [];
	private int _fetchedColumnCount;

	/// <summary>The range the envelopes in hand were read for; null while none counts as fetched.</summary>
	public FetchRange? RangeInHand { get; private set; }

	public int ReportedColumnTarget { get; set; } = HistoryColumnTarget.MaxColumns;

	public bool HasRead(int penId)
	{
		return _fetchedPenIds.Contains(penId);
	}

	/// <summary>Drops the range in hand and returns it, so nothing counts as fetched until a read lands.</summary>
	public FetchRange? Forget()
	{
		var rangeInHand = RangeInHand;
		RangeInHand = null;

		return rangeInHand;
	}

	public void Land(HistoryRequest request, FetchRange? rangeBefore, IReadOnlyList<int> visiblePenIds)
	{
		var joinsTheRangeInHand = rangeBefore == request.Range;

		if (!joinsTheRangeInHand)
		{
			_fetchedPenIds.Clear();
			_fetchedColumnCount = request.TargetColumnCount;
		}

		_fetchedPenIds.UnionWith(request.PenIds);

		// docs/architecture/data-integration.md#what-one-history-query-covers
		RangeInHand = joinsTheRangeInHand || HoldsEvery(visiblePenIds) ? request.Range : null;
	}

	/// <summary>What the window in view still needs read, or null when it needs nothing.</summary>
	public HistoryRequest? Next(IReadOnlyList<int> visiblePenIds, DateTime windowStart, DateTime windowEnd)
	{
		if (visiblePenIds.Count == 0)
		{
			return null;
		}

		if (RangeCovering(windowStart, windowEnd) is not { } fetched)
		{
			var visible = HistoryPrefetch.Visible(
				windowStart,
				windowEnd,
				_navigation.ActiveLayer,
				_navigation.TargetColumnCount,
				_navigation.FirstSample,
				ReachesTheNewestSample(windowEnd));

			return RequestOver(visiblePenIds, visible);
		}

		IReadOnlyList<int> missingPenIds = [.. visiblePenIds.Where(penId => !_fetchedPenIds.Contains(penId))];

		if (missingPenIds.Count > 0)
		{
			return new HistoryRequest(missingPenIds, fetched, _fetchedColumnCount);
		}

		var holdsTheMargins = fetched.LeftMarginWindows > 0;

		if (holdsTheMargins)
		{
			return null;
		}

		var expanded = Expand(windowStart, windowEnd);
		var addsTimeThatCanHoldRows = expanded.FromUtc != fetched.FromUtc || expanded.ToUtc != fetched.ToUtc;

		return addsTimeThatCanHoldRows ? RequestOver(visiblePenIds, expanded) : null;
	}

	public bool IsWindowFetched(IReadOnlyList<int> visiblePenIds, DateTime windowStart, DateTime windowEnd)
	{
		return RangeCovering(windowStart, windowEnd) is not null && HoldsEvery(visiblePenIds);
	}

	private bool HoldsEvery(IReadOnlyList<int> visiblePenIds)
	{
		return visiblePenIds.All(_fetchedPenIds.Contains);
	}

	private HistoryRequest RequestOver(IReadOnlyList<int> penIds, FetchRange range)
	{
		return new HistoryRequest(penIds, range, ReportedColumnTarget * range.ColumnFactor);
	}

	private FetchRange Expand(DateTime windowStart, DateTime windowEnd)
	{
		return HistoryPrefetch.Expand(
			windowStart,
			windowEnd,
			_navigation.ActiveLayer,
			_navigation.TargetColumnCount,
			_navigation.FirstSample);
	}

	private FetchRange? RangeCovering(DateTime windowStart, DateTime windowEnd)
	{
		return RangeInHand is { } fetched
			&& HistoryPrefetch.Covers(
				fetched,
				windowStart,
				windowEnd,
				_navigation.ActiveLayer,
				_navigation.TargetColumnCount,
				_navigation.FirstSample)
			? fetched
			: null;
	}

	// docs/architecture/data-integration.md#what-one-history-query-covers
	private bool ReachesTheNewestSample(DateTime windowEnd)
	{
		return _navigation.IsSticky || _navigation.NewestSample is not { } newest || windowEnd >= newest;
	}
}
