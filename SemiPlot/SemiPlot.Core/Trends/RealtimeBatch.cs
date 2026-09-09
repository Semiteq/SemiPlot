namespace SemiPlot.Core.Trends;

/// <summary>
/// One pen's share of a <see cref="RealtimeBatch"/>, on that pen's own timestamps and nothing else;
/// <c>double</c>, not <c>double?</c>, because a null here would draw a break the archive never recorded.
/// </summary>
public sealed record PenRealtimeValues(
	int PenId,
	IReadOnlyList<DateTime> TimestampsUtc,
	IReadOnlyList<double> Values);

/// <summary>
/// One buffer window of the live edge. <paramref name="Timestamps"/> is the ascending union of every pen's own
/// timestamps, which is what a consumer advances the live edge from; the values themselves hang off
/// <see cref="PenRealtimeValues"/>, on each pen's own timestamps.
/// </summary>
public sealed record RealtimeBatch(
	IReadOnlyList<DateTime> Timestamps,
	IReadOnlyList<PenRealtimeValues> Pens);
