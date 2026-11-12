using SemiPlot.Tools.ArchiveSeeder;

namespace SemiPlot.Tests.Unit;

// The groupings and lattice readings every row assertion in this project is stated over, shared so the
// vendor's own rows and the generator's are read the same way.
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

	// The index of the change whose value a row carries: its own for a change row, the one before for an
	// anchor, rounded down to the pen's stride (docs/architecture/bench.md#what-the-generator-emits).
	public static long HeldIndex(ArchiveRow row, SyntheticPen pen, long intervalTicks)
	{
		var ticks = row.Timestamp.Ticks;
		var index = ticks % intervalTicks == 0
			? ticks / intervalTicks
			: ((ticks + RawLayerGenerator.PollInterval.Ticks) / intervalTicks) - 1;

		return index - (index % pen.ChangeStride(intervalTicks));
	}

	public static double Walk(long seed, SyntheticPen pen, long index)
	{
		return pen.LogScaleOnStart
			? SyntheticValueWalk.LogValue(seed, pen.PenId, index, pen.MinValue, pen.MaxValue)
			: SyntheticValueWalk.Value(seed, pen.PenId, index, pen.MinValue, pen.MaxValue);
	}
}
