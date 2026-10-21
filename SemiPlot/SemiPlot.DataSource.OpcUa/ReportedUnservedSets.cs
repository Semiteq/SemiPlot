namespace SemiPlot.DataSource.OpcUa;

/// <summary>
/// The unserved pen sets the process has already reported, shared by every subscription, so a pen-set change or a
/// recreated session that meets the same set writes no second entry. Only the source's loop calls it.
/// </summary>
internal sealed class ReportedUnservedSets
{
	private readonly HashSet<string> _reported = [];

	public bool TryAdd(IReadOnlyList<int> unknownPenIds, IReadOnlyList<int> notNumericPenIds)
	{
		var key = string.Join(',', unknownPenIds) + "|" + string.Join(',', notNumericPenIds);

		return _reported.Add(key);
	}
}
