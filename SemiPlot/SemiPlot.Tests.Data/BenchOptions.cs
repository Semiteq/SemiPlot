using System.Globalization;

using SemiPlot.Tools.ArchiveSeeder;

namespace SemiPlot.Tests.Data;

// The options every generator test starts from. The connection string is never opened — these tests
// reach no database — and --end is fixed, so a run of the same seed produces the same rows.
internal static class BenchOptions
{
	public const string ConnectionString = "Host=localhost;Database=archive;Username=scada_writer";

	// The literal an entry-point test types on a command line, and the DateTime every other test compares
	// against, parsed from it so the two cannot drift apart.
	public const string EndText = "2026-01-02T00:00:00";

	public static readonly DateTime End = DateTime.Parse(EndText, CultureInfo.InvariantCulture);

	public static SeederOptions For(
		int days = SeederOptions.DefaultDays,
		int pens = SeederOptions.DefaultPenCount,
		long seed = SeederOptions.DefaultSeed,
		double changeSeconds = SeederOptions.DefaultChangeSeconds,
		int breaks = SeederOptions.DefaultBreakCount)
	{
		return new SeederOptions(
			ConnectionString,
			End,
			days,
			pens,
			seed,
			changeSeconds,
			breaks,
			AdminConnectionString: null);
	}
}
