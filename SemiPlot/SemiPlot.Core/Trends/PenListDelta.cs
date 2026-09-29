namespace SemiPlot.Core.Trends;

/// <summary>
/// What changed between two catalogues; <see cref="Current"/> is the newer one.
/// </summary>
public sealed record PenListDelta(
	IReadOnlyList<Pen> Current,
	IReadOnlyList<Pen> Added,
	IReadOnlyList<int> RemovedPenIds,
	IReadOnlyList<PenRevision> Revised)
{
	/// <summary>Nothing added, removed or revised; a new order alone is no change.</summary>
	public bool IsEmpty => Added.Count == 0 && RemovedPenIds.Count == 0 && Revised.Count == 0;

	public bool ChangesPenSet => Added.Count > 0 || RemovedPenIds.Count > 0;

	public static PenListDelta Between(IReadOnlyList<Pen> previous, IReadOnlyList<Pen> current)
	{
		var previousById = previous.ToDictionary(pen => pen.PenId);
		var currentIds = current.Select(pen => pen.PenId).ToHashSet();

		var added = new List<Pen>();
		var revised = new List<PenRevision>();

		foreach (var pen in current)
		{
			if (!previousById.TryGetValue(pen.PenId, out var previousPen))
			{
				added.Add(pen);
			}
			else if (!previousPen.Equals(pen))
			{
				revised.Add(new PenRevision(previousPen, pen));
			}
		}

		var removedPenIds = previous
			.Select(pen => pen.PenId)
			.Where(penId => !currentIds.Contains(penId))
			.ToList();

		return new PenListDelta(current, added, removedPenIds, revised);
	}
}

/// <summary>One pen present in both reads whose stored settings differ.</summary>
public sealed record PenRevision(Pen Previous, Pen Current)
{
	public bool ScaleChanged =>
		!Nullable.Equals(Previous.ScaleMin, Current.ScaleMin) || !Nullable.Equals(Previous.ScaleMax, Current.ScaleMax);
}
