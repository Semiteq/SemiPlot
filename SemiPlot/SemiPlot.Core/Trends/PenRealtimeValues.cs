namespace SemiPlot.Core.Trends;

/// <summary>
/// One pen's share of a <see cref="RealtimeBatch"/>, on that pen's own timestamps and nothing else;
/// <c>double</c>, not <c>double?</c>, because a null here would draw a break the archive never recorded.
/// </summary>
public sealed record PenRealtimeValues(
	int PenId,
	IReadOnlyList<DateTime> TimestampsUtc,
	IReadOnlyList<double> Values);
