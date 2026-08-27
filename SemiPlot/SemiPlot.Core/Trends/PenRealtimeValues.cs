namespace SemiPlot.Core.Trends;

/// <summary>
/// One pen's share of a <see cref="RealtimeBatch"/>, on that pen's own timestamps and nothing else.
/// <para>
/// The two lists are the same length and ascending. They are per pen rather than a column over the
/// batch's shared timestamp list because the archive is per-variable and change-based with a deadband:
/// two variables rarely carry the same <c>t</c>, so one buffer window routinely spans timestamps only
/// one pen sampled. A pen carries no entry at a timestamp it did not sample, and a consumer leaves it
/// alone there.
/// </para>
/// <para>
/// <c>double</c> rather than <c>double?</c>: <see cref="Sample"/> carries a non-nullable value, so the
/// live edge has no representation for a break at all. The gap a chart draws is the history path's own
/// reconstruction from the archive's <c>q = 32</c> mark, and nothing on this path may manufacture one.
/// </para>
/// </summary>
public sealed record PenRealtimeValues(
	long PenId,
	IReadOnlyList<DateTime> TimestampsUtc,
	IReadOnlyList<double> Values);
