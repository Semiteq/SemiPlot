namespace SemiPlot.Tools.ArchiveSeeder;

// A coarse layer is not an aggregate: it holds verbatim copies of raw rows, up to four per period,
// selected by magnitude (docs/architecture/scada-archive.md#layers).
public static class LayerThinner
{
	public const short MinuteLayer = 1;
	public const short HourLayer = 2;
	public const short DayLayer = 3;

	public static readonly IReadOnlyList<short> CoarseLayers = [MinuteLayer, HourLayer, DayLayer];

	// The one place calendar alignment lives. Whether the vendor thins on calendar periods or on flush
	// windows is the first open question of docs/architecture/scada-archive.md#not-established.
	public static DateTime PeriodStart(DateTime timestamp, short layer)
	{
		var period = layer switch
		{
			MinuteLayer => TimeSpan.TicksPerMinute,
			HourLayer => TimeSpan.TicksPerHour,
			DayLayer => TimeSpan.TicksPerDay,
			_ => throw new ArgumentOutOfRangeException(
				nameof(layer),
				layer,
				"Coarse layers are 1 (minute), 2 (hour) and 3 (day).")
		};

		return new DateTime(timestamp.Ticks - (timestamp.Ticks % period), timestamp.Kind);
	}

	public static IReadOnlyList<ArchiveRow> Thin(IEnumerable<ArchiveRow> rawRows, short layer)
	{
		var thinned = new List<ArchiveRow>();

		foreach (var pen in rawRows.GroupBy(row => row.Id))
		{
			var ordered = pen.OrderBy(row => row.Timestamp).ToArray();

			foreach (var period in ordered.GroupBy(row => PeriodStart(row.Timestamp, layer)))
			{
				AppendPeriod(thinned, [.. period], layer);
			}
		}

		return thinned;
	}

	public static IReadOnlyList<ArchiveRow> ThinAll(IReadOnlyCollection<ArchiveRow> rawRows)
	{
		return [.. CoarseLayers.SelectMany(layer => Thin(rawRows, layer))];
	}

	// Ties resolve to the earliest row; the vendor keeps the later one.
	// docs/architecture/scada-archive.md#not-established
	private static void AppendPeriod(List<ArchiveRow> thinned, IReadOnlyList<ArchiveRow> period, short layer)
	{
		var selected = new SortedDictionary<DateTime, ArchiveRow>();

		Take(selected, period[0]);
		Take(selected, period[^1]);
		Take(selected, period.MinBy(row => row.Value)!);
		Take(selected, period.MaxBy(row => row.Value)!);

		// Marker rows are copied into every layer regardless of selection, so a gap boundary survives
		// thinning (docs/architecture/scada-archive.md#quality-and-gaps). They are additional to the four.
		foreach (var marker in period.Where(row => row.Quality != ArchiveRow.OrdinaryQuality))
		{
			Take(selected, marker);
		}

		foreach (var row in selected.Values)
		{
			thinned.Add(row with { Layer = layer });
		}
	}

	// The timestamp identifies the row: (id, l, t) is the primary key, and one period of one pen holds
	// each timestamp once.
	private static void Take(SortedDictionary<DateTime, ArchiveRow> selected, ArchiveRow row)
	{
		selected[row.Timestamp] = row;
	}
}
