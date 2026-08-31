using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// RequiresHistoryRequery is false for a sticky live-edge advance: the realtime tail already carries the
// live edge, so the view must NOT re-query a full window of history on every realtime tick.
public sealed record NavigationWindow(
	DateTime From,
	DateTime To,
	AggregationLayer Layer,
	bool RequiresHistoryRequery = true);
