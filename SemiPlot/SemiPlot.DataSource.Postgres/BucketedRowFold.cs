using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Postgres;

// The server already reduced the window to one row per column, so nothing here decimates a second time.
internal static class BucketedRowFold
{
	/// <summary>
	/// One bucket of <see cref="ArchiveStatements.BucketedRawWindow"/>. A null the statement carries arrives
	/// as <see cref="double.NaN"/>.
	/// </summary>
	public readonly record struct Row(
		int PenId,
		DateTime ArchiveLocal,
		double Value,
		double Min,
		double Max,
		bool EndsBreak);

	/// <summary>
	/// Unenforced precondition: <c>rows</c> arrive <c>ORDER BY id, t</c>, grouped by consecutive identifier
	/// with no client-side sort or dedup; losing that ordering yields two envelopes for one pen.
	/// </summary>
	public static IReadOnlyList<PenHistoryEnvelope> Fold(IReadOnlyList<Row> rows, ArchiveTimeConverter timeConverter)
	{
		var envelopes = new List<PenHistoryEnvelope>();
		var index = 0;

		while (index < rows.Count)
		{
			var penId = rows[index].PenId;
			var timestamps = new List<DateTime>();
			var min = new List<double>();
			var max = new List<double>();
			var center = new List<double>();

			// The comparand resets per pen; hoisting it would drop every later pen's head row.
			while (index < rows.Count && rows[index].PenId == penId)
			{
				var row = rows[index];
				var utc = timeConverter.ToUtc(row.ArchiveLocal);

				// ToUtc is not monotonic across DST; a row that does not advance is dropped
				// (docs/architecture/data-integration.md, Time boundary).
				if (timestamps.Count == 0 || utc > timestamps[^1])
				{
					timestamps.Add(utc);
					min.Add(row.Min);
					max.Add(row.Max);
					center.Add(row.Value);

					// Anchor one tick after the marker so the line reaches the last recorded sample.
					if (row.EndsBreak)
					{
						timestamps.Add(utc.AddTicks(1));
						min.Add(double.NaN);
						max.Add(double.NaN);
						center.Add(double.NaN);
					}
				}

				index++;
			}

			envelopes.Add(new PenHistoryEnvelope(penId, timestamps, min, max, center));
		}

		return envelopes;
	}
}
