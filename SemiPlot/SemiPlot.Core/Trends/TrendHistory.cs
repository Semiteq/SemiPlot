namespace SemiPlot.Core.Trends;

// A completed history query: the aggregation layer the envelopes were built at, plus one decimated
// min/max envelope per requested pen.
public sealed record TrendHistory(
	AggregationLayer Layer,
	IReadOnlyList<PenHistoryEnvelope> Pens);
