using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// docs/architecture/data-integration.md#what-one-history-query-covers
/// <summary>Lays a history read over the envelope in hand of the same pen.</summary>
internal static class EnvelopeSplice
{
	/// <summary>
	/// The read, plus the columns in hand past each side it has no margin on when its range overlaps their span.
	/// </summary>
	public static (PenHistoryEnvelope Envelope, ReadSpan Span) Over(
		PenHistoryEnvelope read,
		PenHistoryEnvelope? inHand,
		FetchRange range,
		ReadSpan? inHandSpan)
	{
		var readSpan = new ReadSpan(range.FromUtc, range.ToUtc);

		if (inHand is null
			|| inHandSpan is not { } spanInHand
			|| range.FromUtc >= spanInHand.ToUtc
			|| range.ToUtc <= spanInHand.FromUtc)
		{
			return (read, readSpan);
		}

		var keepsBefore = range.LeftMarginWindows == 0;
		var keepsAfter = range.RightMarginWindows == 0;
		var span = new ReadSpan(
			keepsBefore && spanInHand.FromUtc < range.FromUtc ? spanInHand.FromUtc : range.FromUtc,
			keepsAfter && spanInHand.ToUtc > range.ToUtc ? spanInHand.ToUtc : range.ToUtc);

		var timestamps = inHand.Timestamps;
		var keptBeforeCount = keepsBefore ? inHand.FirstIndexAtOrAfter(FirstTimeRead(read, range)) : 0;
		var keptAfterStart = keepsAfter
			? inHand.FirstIndexAtOrAfter(FirstTimeAfterTheRead(read, range))
			: timestamps.Count;

		if (keptBeforeCount == 0 && keptAfterStart == timestamps.Count)
		{
			return (read, span);
		}

		var drawn = new PenHistoryEnvelope(
			read.PenId,
			Join(timestamps, read.Timestamps, keptBeforeCount, keptAfterStart),
			Join(inHand.Min, read.Min, keptBeforeCount, keptAfterStart),
			Join(inHand.Max, read.Max, keptBeforeCount, keptAfterStart),
			Join(inHand.Center, read.Center, keptBeforeCount, keptAfterStart));

		return (drawn, span);
	}

	// The seed row lies before the range, so the read starts at whichever of the two comes first.
	private static DateTime FirstTimeRead(PenHistoryEnvelope read, FetchRange range)
	{
		return read.Timestamps.Count > 0 && read.Timestamps[0] < range.FromUtc ? read.Timestamps[0] : range.FromUtc;
	}

	private static DateTime FirstTimeAfterTheRead(PenHistoryEnvelope read, FetchRange range)
	{
		return read.Timestamps.Count > 0 && read.Timestamps[^1] >= range.ToUtc
			? read.Timestamps[^1].AddTicks(1)
			: range.ToUtc;
	}

	private static List<T> Join<T>(
		IReadOnlyList<T> inHand,
		IReadOnlyList<T> read,
		int keptBeforeCount,
		int keptAfterStart)
	{
		return [.. inHand.Take(keptBeforeCount), .. read, .. inHand.Skip(keptAfterStart)];
	}
}
