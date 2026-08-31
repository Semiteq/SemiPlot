using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// The bound and the merge of the fresh tail: the raw rows a coarse window is short of at its right edge,
/// because a coarse layer is flushed on its own cadence and its newest row is up to one point spacing old
/// (docs/architecture/data-integration.md, Fresh tail).
/// <para>
/// No conversion happens on this path.
/// </para>
/// </summary>
internal static class FreshTail
{
	/// <summary>
	/// Each requested pen's seam: the last timestamp the coarse read returned for it, or the window start
	/// when it returned none. The rows arrive <c>ORDER BY id, t</c>, so the last row of a pen's run is its
	/// newest and the running assignment below ends on it.
	/// </summary>
	public static Dictionary<int, DateTime> Seams(
		IReadOnlyList<HistoryRowFold.Row> coarseRows,
		IReadOnlyList<int> penIds,
		DateTime windowStartLocal)
	{
		var seams = new Dictionary<int, DateTime>(penIds.Count);

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
	/// Seams at the window start drop out here: left in, they pull the bound down to the clamp
	/// for rows <see cref="Merge"/> discards.
	/// </para>
	/// </summary>
	public static DateTime? Start(
		AggregationLayer layer,
		IReadOnlyDictionary<int, DateTime> seams,
		DateTime windowEndLocal)
	{
		if (layer == AggregationLayer.Raw || seams.Count == 0)
		{
			return null;
		}

		var spacing = layer.ToPointSpacing();
		var clamped = windowEndLocal - (spacing * 4);
		var earliestSeam = EarliestSeamReachingTheClamp(seams, clamped);

		if (earliestSeam is null)
		{
			return null;
		}

		// A layer fresh within one of its own points is not short of anything a reader can see, so it
		// costs no round trip.
		return windowEndLocal - earliestSeam.Value <= spacing ? null : earliestSeam;
	}

	private static DateTime? EarliestSeamReachingTheClamp(
		IReadOnlyDictionary<int, DateTime> seams,
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
	/// A pen whose seam predates <paramref name="tailStartLocal"/> gets no tail row: a range with no row
	/// is not a gap and would draw one straight segment.
	/// </para>
	/// </summary>
	public static IReadOnlyList<HistoryRowFold.Row> Merge(
		IReadOnlyList<HistoryRowFold.Row> coarseRows,
		IReadOnlyList<HistoryRowFold.Row> tailRows,
		IReadOnlyDictionary<int, DateTime> seams,
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
	private static int NextPenId(
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
