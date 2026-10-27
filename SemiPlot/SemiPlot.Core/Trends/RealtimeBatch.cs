namespace SemiPlot.Core.Trends;

/// <summary>
/// One pen's share of a <see cref="RealtimeBatch"/>, on that pen's own timestamps only. A null is a break the
/// live source reported; a pen carries no entry, null or real, at a union timestamp it did not report.
/// </summary>
public sealed record PenRealtimeValues(
	int PenId,
	IReadOnlyList<DateTime> TimestampsUtc,
	IReadOnlyList<double?> Values);

/// <summary>
/// One buffer window of the live edge with at least one answered poll: its newest answer, whether any poll resumed
/// after a failed one, and the ascending union of every pen's own timestamps, empty when no pen changed.
/// </summary>
public sealed record RealtimeBatch(
	IReadOnlyList<DateTime> Timestamps,
	IReadOnlyList<PenRealtimeValues> Pens,
	DateTime AnsweredAtUtc,
	bool ResumesAfterFailure);
