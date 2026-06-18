namespace SemiPlot.Core.Trends;

public sealed record TrendHistory(
	AggregationLayer Layer,
	IReadOnlyList<PenHistoryEnvelope> Pens);
