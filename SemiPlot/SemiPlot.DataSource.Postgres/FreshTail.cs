using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// The bound and the merge of the fresh tail: the raw rows a coarse window is short of at its right edge.
/// No conversion happens on this path (docs/architecture/data-integration.md, Fresh tail).
/// </summary>
internal static class FreshTail
{
	/// <summary>
	/// Each requested pen's seam: the last timestamp the coarse read returned for it, or the window start
	/// when it returned none. The rows arrive <c>ORDER BY id, t</c>, so the last row of a pen's run is its
	/// newest and the running assignment below ends on it.
	/// </summary>
	public static Dictionary<int, DateTime> Seams(
		IReadOnlyList<BucketedRowFold.Row> coarseRows,
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
	/// The instant a coarse layer's tail read starts at, or <c>null</c> when no tail is read at all.
	/// </summary>
	public static DateTime? Start(
		AggregationLayer layer,
		IReadOnlyDictionary<int, DateTime> seams,
		DateTime tailEdgeLocal)
	{
		if (seams.Count == 0)
		{
			return null;
		}

		var spacing = layer.ToPointSpacing();
		var clamped = tailEdgeLocal - (spacing * 4);
		var earliestSeam = EarliestSeamReachingTheClamp(seams, clamped);

		if (earliestSeam is null)
		{
			return null;
		}

		var isFreshWithinOnePoint = tailEdgeLocal - earliestSeam.Value <= spacing;

		return isFreshWithinOnePoint ? null : earliestSeam;
	}

	private static DateTime? EarliestSeamReachingTheClamp(
		IReadOnlyDictionary<int, DateTime> seams,
		DateTime clampedLocal)
	{
		DateTime? earliest = null;

		foreach (var seam in seams.Values)
		{
			// A seam at the window start drops out here: left in, it pulls the bound down for rows Merge discards.
			if (seam >= clampedLocal && (earliest is null || seam < earliest))
			{
				earliest = seam;
			}
		}

		return earliest;
	}

	/// <summary>
	/// The coarse rows with each pen's tail rows appended after its own, in ascending pen identifier with one
	/// consecutive run per pen, the ordering <see cref="BucketedRowFold.Fold"/> requires. A pen whose seam
	/// predates <paramref name="tailStartLocal"/> gets no tail row: a range with no row is not a gap.
	/// </summary>
	public static IReadOnlyList<BucketedRowFold.Row> Merge(
		IReadOnlyList<BucketedRowFold.Row> coarseRows,
		IReadOnlyList<BucketedRowFold.Row> tailRows,
		IReadOnlyDictionary<int, DateTime> seams,
		DateTime tailStartLocal)
	{
		var merged = new List<BucketedRowFold.Row>(coarseRows.Count + tailRows.Count);
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
		IReadOnlyList<BucketedRowFold.Row> coarseRows,
		int coarseIndex,
		IReadOnlyList<BucketedRowFold.Row> tailRows,
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
