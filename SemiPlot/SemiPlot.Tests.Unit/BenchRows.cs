using SemiPlot.Tools.ArchiveSeeder;

namespace SemiPlot.Tests.Unit;

// The two groupings every row assertion in this project is stated over, shared so the vendor's own
// rows and the generator's are read the same way.
internal static class BenchRows
{
	// The layer is the one column a coarse row does not copy, so identity is the other four.
	public static (int Id, DateTime Timestamp, double Value, int Quality) Identity(ArchiveRow row)
	{
		return (row.Id, row.Timestamp, row.Value, row.Quality);
	}

	public static IReadOnlyList<IReadOnlyList<ArchiveRow>> ByPen(IReadOnlyList<ArchiveRow> rows)
	{
		return [.. rows.GroupBy(row => row.Id).Select(pen => (IReadOnlyList<ArchiveRow>)[.. pen])];
	}
}
