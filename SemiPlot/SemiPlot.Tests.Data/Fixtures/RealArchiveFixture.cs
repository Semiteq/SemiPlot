using System.Globalization;

using SemiPlot.Tools.ArchiveSeeder;

namespace SemiPlot.Tests.Data.Fixtures;

// Rows lifted out of the customer archive dump and anonymised before committing: the two identifiers
// are synthetic and every timestamp carries one fixed offset, so intervals, values and quality codes
// are the vendor's own. sql/README.md records the extraction, the offset and how to regenerate the
// file. Nothing here needs a database — the CSV is the evidence.
public static class RealArchiveFixture
{
	// The observed poll interval of the archived project (docs/architecture/scada-archive.md#write-behavior).
	public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

	// The minute the extraction was chosen around: it holds a 32/16 marker pair, a burst of changes at
	// the poll interval, and rows in all four layers.
	public static readonly DateTime ChosenMinute = new(2000, 1, 1, 13, 55, 0, DateTimeKind.Unspecified);

	public static IReadOnlyList<ArchiveRow> Rows { get; } = Read();

	public static IReadOnlyList<ArchiveRow> RawRows => Layer(ArchiveRow.RawLayer);

	public static IReadOnlyList<ArchiveRow> Layer(short layer)
	{
		return Rows.Where(row => row.Layer == layer).ToArray();
	}

	public static IReadOnlyList<int> Pens()
	{
		return Rows.Select(row => row.Id).Distinct().Order().ToArray();
	}

	private static IReadOnlyList<ArchiveRow> Read()
	{
		var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "real-archive-rows.csv");

		return File.ReadLines(path)
			.Skip(1)
			.Where(line => line.Length > 0)
			.Select(Parse)
			.ToArray();
	}

	private static ArchiveRow Parse(string line)
	{
		var columns = line.Split(',');

		return new ArchiveRow(
			int.Parse(columns[0], CultureInfo.InvariantCulture),
			short.Parse(columns[1], CultureInfo.InvariantCulture),
			DateTime.ParseExact(columns[2], "yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture),
			double.Parse(columns[3], CultureInfo.InvariantCulture),
			int.Parse(columns[4], CultureInfo.InvariantCulture));
	}
}
