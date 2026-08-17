using SemiPlot.Tools.ArchiveSeeder;

namespace SemiPlot.Tests.Data;

// The options every generator test starts from. The connection string is never opened — these tests
// reach no database — and --end is fixed, so a run of the same seed produces the same rows.
internal static class BenchOptions
{
	public const string ConnectionString = "Host=localhost;Database=archive;Username=scada_writer";

	public static readonly DateTime End = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified);

	public static SeederOptions For(
		int days = SeederOptions.DefaultDays,
		int pens = SeederOptions.DefaultPenCount,
		long seed = SeederOptions.DefaultSeed,
		double changeSeconds = SeederOptions.DefaultChangeSeconds,
		int breaks = SeederOptions.DefaultBreakCount,
		DateTime? end = null)
	{
		return new SeederOptions(
			ConnectionString,
			end ?? End,
			days,
			pens,
			seed,
			changeSeconds,
			breaks,
			AdminConnectionString: null);
	}
}
