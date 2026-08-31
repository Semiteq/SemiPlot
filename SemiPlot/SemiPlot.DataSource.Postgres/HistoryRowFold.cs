using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Postgres;

internal static class HistoryRowFold
{
	// The archive marks the last sample before a break q = 32 and the first sample after it q = 16
	// (docs/architecture/scada-archive.md#quality-and-gaps). Only the opening mark takes a branch here:
	// the resumption is what the decimator already produces on the far side of a null.
	// Exact equality on purpose: only {0, 16, 32} have been measured.
	private const int LastBeforeBreakQuality = 32;

	/// <summary>
	/// One row of the windowed read; a null <c>Value</c> is a gap. <c>Quality</c> is the archive's
	/// <c>q</c> column, carried on every row; <c>32</c> marks the last sample before a break.
	/// </summary>
	public readonly record struct Row(int PenId, DateTime ArchiveLocal, double? Value, int Quality);

	/// <summary>
	/// Unenforced precondition: <paramref name="rows"/> arrive in the order
	/// <see cref="ArchiveStatements.SparseHistoryWindow"/> produces them, <c>ORDER BY id, t</c>, so each
	/// pen's rows form one consecutive ascending run. Runs are grouped by consecutive identifier and
	/// nothing sorts or de-duplicates client-side.
	/// <para>Losing the total ordering yields two envelopes for one pen.</para>
	/// </summary>
	public static IReadOnlyList<PenHistoryEnvelope> Fold(
		IReadOnlyList<Row> rows,
		ArchiveTimeConverter timeConverter,
		int targetColumnCount)
	{
		var envelopes = new List<PenHistoryEnvelope>();
		var index = 0;

		while (index < rows.Count)
		{
			var penId = rows[index].PenId;
			var timestamps = new List<DateTime>();
			var values = new List<double?>();

			// The comparand resets per pen; hoisting it would drop every later pen's head row.
			while (index < rows.Count && rows[index].PenId == penId)
			{
				var utc = timeConverter.ToUtc(rows[index].ArchiveLocal);

				// ToUtc is not monotonic across DST; a row that does not advance is dropped
				// (docs/architecture/data-integration.md, Time boundary).
				if (timestamps.Count == 0 || utc > timestamps[^1])
				{
					timestamps.Add(utc);
					values.Add(rows[index].Value);

					// Anchor one tick after the marker so the line reaches the last recorded sample.
					if (rows[index].Quality == LastBeforeBreakQuality)
					{
						timestamps.Add(utc.AddTicks(1));
						values.Add(null);
					}
				}

				index++;
			}

			envelopes.Add(MinMaxDecimator.Decimate(penId, timestamps, values, targetColumnCount));
		}

		return envelopes;
	}
}
