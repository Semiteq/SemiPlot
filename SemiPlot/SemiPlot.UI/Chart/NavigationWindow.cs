using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// RequiresHistoryRequery is false for a sticky live-edge advance: the realtime tail already carries the
// live edge, so the view must NOT re-query a full window of history on every realtime tick.
// IsColumnCountChange marks a canvas resize rather than a window change: the bounds are the ones already
// on screen, so the view model may hold such a re-query until the startup snap has happened.
public sealed record NavigationWindow(
	DateTime From,
	DateTime To,
	AggregationLayer Layer,
	bool RequiresHistoryRequery = true,
	bool IsColumnCountChange = false);
