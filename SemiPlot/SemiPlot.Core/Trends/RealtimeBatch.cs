namespace SemiPlot.Core.Trends;

/// <summary>
/// One buffer window of the live edge. <c>Timestamps</c> is the ascending union of every pen's own
/// timestamps, which is what a consumer advances the live edge from; the values themselves hang off
/// <see cref="PenRealtimeValues"/>, on each pen's own timestamps.
/// </summary>
public sealed record RealtimeBatch(
	IReadOnlyList<DateTime> Timestamps,
	IReadOnlyList<PenRealtimeValues> Pens);
