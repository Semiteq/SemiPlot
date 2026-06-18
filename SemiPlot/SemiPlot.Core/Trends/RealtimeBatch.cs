namespace SemiPlot.Core.Trends;

public sealed record RealtimeBatch(
	IReadOnlyList<DateTime> Timestamps,
	IReadOnlyList<PenRealtimeValues> Pens);
