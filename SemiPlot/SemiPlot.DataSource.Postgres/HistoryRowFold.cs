using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Postgres;

internal static class HistoryRowFold
{
	// The archive marks the last sample before a break q = 32 and the first sample after it q = 16
	// (docs/architecture/scada-archive.md#quality-and-gaps). Only the opening mark takes a branch here:
	// the resumption is what the decimator already produces on the far side of a null.
	//
	// The comparison below is exact equality, which bets on the measured vocabulary {0, 16, 32} rather
	// than on the manual: the manual puts the break marks in the two low hexadecimal digits of an OPC UA
	// status, so a code carrying a mark alongside upper status bits would be missed. No such code has been
	// measured; masking one that has not is a rule with nothing behind it.
	private const int LastBeforeBreakQuality = 32;

	/// <summary>
	/// One row of the windowed read; a null <c>Value</c> is a gap. <c>Quality</c> is the archive's
	/// <c>q</c> column, carried on every row; <c>32</c> marks the last sample before a break.
	/// </summary>
	public readonly record struct Row(long PenId, DateTime ArchiveLocal, double? Value, int Quality);

	/// <summary>
	/// Unenforced precondition: <paramref name="rows"/> arrive in the order
	/// <see cref="ArchiveStatements.SparseHistoryWindow"/> produces them, <c>ORDER BY id, t</c>, so each
	/// pen's rows form one consecutive ascending run. Runs are grouped by consecutive identifier and
	/// nothing sorts or de-duplicates client-side.
	/// <para>
	/// That statement is a <c>UNION ALL</c> of a seed branch and a window branch, and the union is safe
	/// because the <c>ORDER BY</c> sits outside both: a seeded pen's seed row and its window rows merge
	/// into one ascending run, seed first, so the fold still sees one run for the pen and builds one
	/// envelope from it.
	/// </para>
	/// <para>
	/// What breaks the precondition is the loss of that single total ordering rather than a second branch:
	/// the outer <c>ORDER BY</c> removed, or replaced by an ordering per branch, or a second statement
	/// feeding this same fold. An <c>ORDER BY</c> added inside a <c>UNION ALL</c> branch while the outer
	/// clause stands is inert in PostgreSQL and splits nothing. Losing the total ordering lets a pen appear
	/// in two runs, which yields two envelopes for one pen, and no consumer rejects that:
	/// <c>TrendChartViewModel.ApplyHistory</c> keys by pen identifier, so one envelope silently
	/// overwrites the other.
	/// </para>
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

					// The break marker's own point is kept and the anchor goes after it, so the line runs
					// to the last recorded sample before stopping. One tick is four orders of magnitude
					// below the archive's timestamp(3) resolution, so no real row lands inside it, and the
					// tick is added on the UTC side where no daylight-saving boundary can reach it.
					// MinMaxDecimator splits on the null and emits the NaN column that is the gap.
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
