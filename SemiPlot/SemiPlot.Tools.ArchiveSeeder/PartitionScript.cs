using System.Globalization;

namespace SemiPlot.Tools.ArchiveSeeder;

// One day partition per covered day, named tpYYYYmMMdDD (docs/architecture/scada-archive.md#database-objects);
// a row with no partition lands in tpdefault, a fault signal (#reader-hazards), so the writer creates them
// all before it copies anything.
public static class PartitionScript
{
	// Invariant throughout: a machine running a non-Gregorian calendar would otherwise name the
	// partition after a year the archive never uses.
	private const string NameFormat = "'tp'yyyy'm'MM'd'dd";
	private const string BoundFormat = "yyyy-MM-dd HH:mm:ss";

	public static string PartitionName(DateTime day)
	{
		return day.Date.ToString(NameFormat, CultureInfo.InvariantCulture);
	}

	// A range bound is a literal because PostgreSQL binds no placeholder in DDL. Nothing a caller typed
	// reaches the statement: both the name and the bounds are rendered from a DateTime.
	public static string CreateStatement(DateTime day)
	{
		var start = day.Date;
		var end = start.AddDays(1);
		var lower = start.ToString(BoundFormat, CultureInfo.InvariantCulture);
		var upper = end.ToString(BoundFormat, CultureInfo.InvariantCulture);

		return $"CREATE TABLE IF NOT EXISTS public.{PartitionName(start)} PARTITION OF public.trends "
			+ $"FOR VALUES FROM ('{lower}') TO ('{upper}');";
	}

	// The span is [start, endExclusive). An --end falling exactly on midnight therefore creates no
	// partition for the following day, which is the day that would hold no row anyway.
	public static IReadOnlyList<DateTime> CoveredDays(DateTime start, DateTime endExclusive)
	{
		if (endExclusive <= start)
		{
			throw new ArgumentOutOfRangeException(
				nameof(endExclusive),
				endExclusive,
				$"The exclusive end must follow the start {start:O}.");
		}

		var days = new List<DateTime>();

		for (var day = start.Date; day < endExclusive; day = day.AddDays(1))
		{
			days.Add(day);
		}

		return days;
	}

	public static IReadOnlyList<string> CreateStatements(DateTime start, DateTime endExclusive)
	{
		return CoveredDays(start, endExclusive).Select(CreateStatement).ToArray();
	}
}
