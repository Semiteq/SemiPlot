namespace SemiPlot.Core.Export;

public static class ArchiveRowSlices
{
	public static readonly TimeSpan Width = TimeSpan.FromMinutes(15);

	public static IEnumerable<(DateTime From, DateTime To)> Tile(DateTime from, DateTime to)
	{
		for (var start = from; start < to; start += Width)
		{
			var end = to - start > Width ? start + Width : to;

			yield return (start, end);
		}
	}
}
