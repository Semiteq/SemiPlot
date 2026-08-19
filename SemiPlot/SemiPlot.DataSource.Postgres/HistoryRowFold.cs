using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Postgres;

internal static class HistoryRowFold
{
	/// <summary>
	/// One row of the windowed read; a null <c>Value</c> is a gap.
	/// </summary>
	public readonly record struct Row(long PenId, DateTime ArchiveLocal, double? Value);

	/// <summary>
	/// Unenforced precondition: <paramref name="rows"/> arrive in the order
	/// <see cref="ArchiveStatements.SparseHistoryWindow"/> produces them, <c>ORDER BY id, t</c>, so each
	/// pen's rows form one consecutive run. Runs are grouped by consecutive identifier and nothing sorts
	/// or de-duplicates client-side. A future union, or a second statement feeding the same fold, breaks
	/// that silently: a pen appearing in two runs yields two envelopes for one pen, which no consumer
	/// rejects.
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

			// The previous kept timestamp is the tail of this pen's own list, so it resets at each pen. A
			// single comparand carried across the whole result set would drop the head row of every pen
			// after the first, whose timestamp restarts at the window's beginning.
			while (index < rows.Count && rows[index].PenId == penId)
			{
				var utc = timeConverter.ToUtc(rows[index].ArchiveLocal);

				// ToUtc is neither order-preserving across the spring-forward gap, where a skipped local
				// hour resolves past the local values that follow it, nor injective across the autumn
				// fall-back, where both passes over the repeated hour convert to the same instants. The
				// envelope requires strictly ascending timestamps, so a row that does not advance the
				// series is dropped — at the fall-back that costs the whole second pass over the repeated
				// hour, once a year (docs/architecture/data-integration.md, Time boundary).
				if (timestamps.Count == 0 || utc > timestamps[^1])
				{
					timestamps.Add(utc);
					values.Add(rows[index].Value);
				}

				index++;
			}

			envelopes.Add(MinMaxDecimator.Decimate(penId, timestamps, values, targetColumnCount));
		}

		return envelopes;
	}
}
