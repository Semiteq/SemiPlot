namespace SemiPlot.Tools.ArchiveSeeder;

/// <summary>
/// The archive's edge in a follow run: each window starts where the previous one ended, and the edge never
/// moves back, because a window behind it would regenerate rows the archive already holds.
/// </summary>
public sealed class FollowEdge(DateTime edge)
{
	private DateTime _edge = edge;

	/// <summary>The window (edge, end], advancing the edge; null, edge kept, when end is not past it.</summary>
	public (DateTime After, DateTime To)? Advance(DateTime end)
	{
		if (end <= _edge)
		{
			return null;
		}

		var window = (_edge, end);
		_edge = end;

		return window;
	}
}
