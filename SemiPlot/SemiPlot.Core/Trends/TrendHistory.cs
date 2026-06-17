namespace SemiPlot.Core.Trends;

// A completed history query: the aggregation layer plus one decimated envelope per requested pen.
public sealed record TrendHistory(
	AggregationLayer Layer,
	IReadOnlyList<PenHistoryEnvelope> Pens);
