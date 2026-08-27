using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// The bound and the merge of the fresh tail: the raw rows a coarse window is short of at its right edge,
/// because a coarse layer is flushed on its own cadence and its newest row is up to one point spacing old
/// (docs/architecture/data-integration.md, Fresh tail).
/// <para>
/// Every value here is the archive's naive local wall clock, the side
/// <see cref="ArchiveStatements.SparseHistoryWindow"/> binds on and the side
/// <see cref="HistoryRowFold.Row.ArchiveLocal"/> carries, so no conversion happens on this path.
/// </para>
/// </summary>
internal static class FreshTail
{
	/// <summary>
	/// Each requested pen's seam: the last timestamp the coarse read returned for it, or the window start
	/// when it returned none. The rows arrive <c>ORDER BY id, t</c>, so the last row of a pen's run is its
	/// newest and the running assignment below ends on it.
	/// </summary>
	public static Dictionary<long, DateTime> Seams(
		IReadOnlyList<HistoryRowFold.Row> coarseRows,
		IReadOnlyList<int> penIds,
		DateTime windowStartLocal)
	{
		var seams = new Dictionary<long, DateTime>(penIds.Count);

		foreach (var penId in penIds)
		{
			seams[penId] = windowStartLocal;
		}

		foreach (var row in coarseRows)
		{
			seams[row.PenId] = row.ArchiveLocal;
		}

		return seams;
	}

	/// <summary>
	/// The instant a tail read starts at, or <c>null</c> when no tail is read at all.
	/// <para>
	/// The clamp is a cost bound and not a fault threshold. A layer's spacing is a quarter of its period
	/// (<see cref="AggregationLayerExtensions.ToPointSpacing"/>), so at <see cref="AggregationLayer.Day"/>
	/// one period is 24 h, and a coarse layer trailing the raw layer by less than a period is the ordinary
	/// case rather than a fault. All the clamp does is cap how much raw data a single history query may
	/// pull; a layer further behind than that keeps the short right edge it already had.
	/// </para>
	/// <para>
	/// Only a pen whose own seam reaches the clamp decides the bound, because <see cref="Merge"/> keeps a
	/// tail row for exactly those pens and discards the rest. A pen the coarse read answered nothing for
	/// seams at the window start and drops out here: left in, it would pull the bound down to the clamp —
	/// a full layer period of raw rows per pen, 24 h of them at <see cref="AggregationLayer.Day"/> — for
	/// rows the merge would then throw away, and it would hold the shortcut below shut for every pen that
	/// is already fresh.
	/// </para>
	/// </summary>
	public static DateTime? Start(
		AggregationLayer layer,
		IReadOnlyDictionary<long, DateTime> seams,
		DateTime windowEndLocal)
	{
		// Raw is what the tail is read from, so there is nothing coarser for it to be short of.
		if (layer == AggregationLayer.Raw || seams.Count == 0)
		{
			return null;
		}

		var spacing = layer.ToPointSpacing();
		var clamped = windowEndLocal - (spacing * 4);
		var earliestSeam = EarliestSeamReachingTheClamp(seams, clamped);

		// No pen reaches the clamp, so no pen would keep a tail row. Every pen here keeps the short right
		// edge it already had.
		if (earliestSeam is null)
		{
			return null;
		}

		// A layer fresh within one of its own points is not short of anything a reader can see, so it
		// costs no round trip.
		return windowEndLocal - earliestSeam.Value <= spacing ? null : earliestSeam;
	}

	// The result is at or after the clamp by construction, which is what keeps the cost bound.
	private static DateTime? EarliestSeamReachingTheClamp(
		IReadOnlyDictionary<long, DateTime> seams,
		DateTime clampedLocal)
	{
		DateTime? earliest = null;

		foreach (var seam in seams.Values)
		{
			if (seam >= clampedLocal && (earliest is null || seam < earliest))
			{
				earliest = seam;
			}
		}

		return earliest;
	}

	/// <summary>
	/// The coarse rows with each pen's tail rows appended after its own, in ascending pen identifier with
	/// one consecutive run per pen — the ordering <see cref="HistoryRowFold.Fold"/> requires. Rows at or
	/// before a pen's own seam are left to the fold, whose ascending check drops them.
	/// <para>
	/// A pen whose own seam falls before <paramref name="tailStartLocal"/> contributes no tail row at all.
	/// Its coarse rows stop before the tail's own start, so appending tail rows would leave a range no row
	/// covers — and a range carrying no row is not a gap: <see cref="HistoryRowFold"/> emits a null only
	/// where a row asks for one, from a null value or from the archive's <c>q = 32</c> break mark, and
	/// <see cref="MinMaxDecimator"/> turns only that null into the NaN column. The hole would draw as a
	/// single straight interpolated segment across missing time. Such a pen keeps the short right edge it
	/// already had.
	/// </para>
	/// </summary>
	public static IReadOnlyList<HistoryRowFold.Row> Merge(
		IReadOnlyList<HistoryRowFold.Row> coarseRows,
		IReadOnlyList<HistoryRowFold.Row> tailRows,
		IReadOnlyDictionary<long, DateTime> seams,
		DateTime tailStartLocal)
	{
		var merged = new List<HistoryRowFold.Row>(coarseRows.Count + tailRows.Count);
		var coarseIndex = 0;
		var tailIndex = 0;

		while (coarseIndex < coarseRows.Count || tailIndex < tailRows.Count)
		{
			var penId = NextPenId(coarseRows, coarseIndex, tailRows, tailIndex);

			while (coarseIndex < coarseRows.Count && coarseRows[coarseIndex].PenId == penId)
			{
				merged.Add(coarseRows[coarseIndex]);
				coarseIndex++;
			}

			var carriesTail = seams.TryGetValue(penId, out var seam) && seam >= tailStartLocal;

			while (tailIndex < tailRows.Count && tailRows[tailIndex].PenId == penId)
			{
				if (carriesTail)
				{
					merged.Add(tailRows[tailIndex]);
				}

				tailIndex++;
			}
		}

		return merged;
	}

	// Both lists arrive on ascending identifiers, so the smaller head is the next whole run.
	private static long NextPenId(
		IReadOnlyList<HistoryRowFold.Row> coarseRows,
		int coarseIndex,
		IReadOnlyList<HistoryRowFold.Row> tailRows,
		int tailIndex)
	{
		if (coarseIndex >= coarseRows.Count)
		{
			return tailRows[tailIndex].PenId;
		}

		if (tailIndex >= tailRows.Count)
		{
			return coarseRows[coarseIndex].PenId;
		}

		return Math.Min(coarseRows[coarseIndex].PenId, tailRows[tailIndex].PenId);
	}
}
