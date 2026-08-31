using Npgsql;

using NpgsqlTypes;

namespace SemiPlot.Tools.ArchiveSeeder;

// The demo writer's coarse layers, thinned on the server rather than in the process. A tick appends its
// raw rows and never reads them back, so the rows a closing period must be thinned from already sit in
// the archive, and an INSERT ... SELECT reproduces LayerThinner's selection where they are. ON CONFLICT
// DO NOTHING is what makes the coarse row the seeder already wrote for the period its fill ends inside a
// no-op rather than the duplicate-key failure that would end the run.
//
// Closing a period is not enough on its own. FreshTail clamps a pen's seam at one whole period
// (AggregationLayer.ToPointSpacing is a quarter of the period and FreshTail.Start clamps at spacing * 4),
// so a coarse layer holding closed periods alone falls under the clamp for the last seconds of every
// period and the tail vanishes. Writing the open period's first row as the period opens keeps the seam at
// or after that period's start, which never falls under the clamp: inside the period the seam leads it,
// and in the window between a boundary tick's raw commit and this call's own the two are exactly equal,
// which FreshTail's non-strict comparison keeps. There is no margin above that — see
// docs/architecture/bench.md, The demo writer.
public static class CoarseFlush
{
	// LayerThinner.AppendPeriod expressed in SQL: per pen the period's first, last, minimum and maximum
	// row, ties resolved to the earliest by the trailing `t` in each ordering, plus every marker row.
	//
	// ORDER BY v DESC NULLS LAST is load-bearing. trends.v is nullable while ArchiveRow.Value is not, so
	// the thinner never sees a NULL, while PostgreSQL's default NULLS FIRST would select one as a period's
	// maximum — a row the thinner cannot produce.
	//
	// The bound span holds exactly one calendar period, which is why the windows partition by id alone and
	// need no period key.
	private static readonly string _closedPeriodCommand =
		$"""
		INSERT INTO public.trends (id, l, t, v, q)
		SELECT id, @layer, t, v, q
		FROM (
			SELECT id, t, v, q,
				row_number() OVER (PARTITION BY id ORDER BY t)                    AS first_row,
				row_number() OVER (PARTITION BY id ORDER BY t DESC)               AS last_row,
				row_number() OVER (PARTITION BY id ORDER BY v, t)                 AS min_row,
				row_number() OVER (PARTITION BY id ORDER BY v DESC NULLS LAST, t) AS max_row
			FROM public.trends
			WHERE l = {ArchiveRow.RawLayer} AND t >= @periodStart AND t < @periodEndExclusive
		) selected
		WHERE first_row = 1 OR last_row = 1 OR min_row = 1 OR max_row = 1
			OR q <> {ArchiveRow.OrdinaryQuality}
		ON CONFLICT DO NOTHING;
		""";

	// The open period's first raw row, per pen, at the coarse layer. It is the row AppendPeriod selects as
	// the period's first, written before the period closes, so it adds nothing to the period's final
	// content: the closed flush selects the same row and ON CONFLICT DO NOTHING skips it.
	//
	// Only the first row is written early, because only the first row is already final. The period's last
	// row moves with every tick, so writing it would leave the coarse layer as dense as raw.
	//
	// No upper bound is needed: the raw layer ends at the live edge, which is inside the open period. The
	// LATERAL with LIMIT 1 is what keeps the statement cheap — the primary key is (id, l, t), so each pen
	// costs one index probe.
	private static readonly string _openingRowCommand =
		$"""
		INSERT INTO public.trends (id, l, t, v, q)
		SELECT pen.id, @layer, opening.t, opening.v, opening.q
		FROM unnest(@penIds) AS pen(id)
		CROSS JOIN LATERAL (
			SELECT t, v, q FROM public.trends
			WHERE id = pen.id AND l = {ArchiveRow.RawLayer} AND t >= @periodStart
			ORDER BY t LIMIT 1
		) AS opening
		ON CONFLICT DO NOTHING;
		""";

	// The rows the statements of this call inserted. The connection is this method's own: a follow tick
	// holds none open, because ArchiveWriter.WriteAsync opens one per call and disposes it.
	public static async Task<long> FlushAsync(
		FollowOptions options,
		DateTime previousTickLocal,
		DateTime nowLocal,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(options);

		await using var connection = new NpgsqlConnection(options.ConnectionString);

		await connection.OpenAsync(cancellationToken);

		var inserted = 0L;

		// The identifiers the follow loop itself writes. scada_writer holds no privilege on semiplot_tags,
		// so the pens are taken from the generator rather than from a catalogue read.
		var penIds = RawLayerGenerator.SelectPens(options.PenCount)
			.Select(pen => pen.PenId)
			.ToArray();

		foreach (var layer in LayerThinner.CoarseLayers)
		{
			inserted += await OpenPeriodAsync(connection, layer, penIds, nowLocal, cancellationToken);

			inserted += await FlushLayerAsync(connection, layer, previousTickLocal, nowLocal, cancellationToken);
		}

		return inserted;
	}

	// Issued on every call and for every layer, since a period the caller sits inside has already opened.
	// A pen with no raw row at or after the period start contributes nothing: the LATERAL finds no row and
	// the CROSS JOIN drops the pen.
	private static async Task<long> OpenPeriodAsync(
		NpgsqlConnection connection,
		short layer,
		int[] penIds,
		DateTime nowLocal,
		CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand(_openingRowCommand, connection);

		command.Parameters.AddWithValue("layer", NpgsqlDbType.Smallint, layer);
		command.Parameters.AddWithValue("penIds", NpgsqlDbType.Array | NpgsqlDbType.Integer, penIds);
		command.Parameters.AddWithValue(
			"periodStart", NpgsqlDbType.Timestamp, LayerThinner.PeriodStart(nowLocal, layer));

		return await command.ExecuteNonQueryAsync(cancellationToken);
	}

	// Every period the two instants leave behind, not only the first. A tick can span many periods —
	// --follow accepts a cadence up to FollowOptions.MaximumSeconds, and a host suspend stretches any
	// cadence — and the append that ran ahead of this call filled those periods with raw rows, because
	// LiveTailGenerator is a pure function of absolute time. Flushing only the first would leave a
	// continuous raw layer under a coarse layer with a hole nothing marks.
	//
	// Each statement stays bound to one period, which is what lets its windows partition by id alone.
	private static async Task<long> FlushLayerAsync(
		NpgsqlConnection connection,
		short layer,
		DateTime previousTickLocal,
		DateTime nowLocal,
		CancellationToken cancellationToken)
	{
		var openPeriod = LayerThinner.PeriodStart(nowLocal, layer);
		var inserted = 0L;

		for (var closing = LayerThinner.PeriodStart(previousTickLocal, layer);
			closing < openPeriod;
			closing = PeriodEndExclusive(closing, layer))
		{
			inserted += await FlushPeriodAsync(connection, layer, closing, cancellationToken);
		}

		return inserted;
	}

	private static async Task<long> FlushPeriodAsync(
		NpgsqlConnection connection,
		short layer,
		DateTime periodStart,
		CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand(_closedPeriodCommand, connection);

		command.Parameters.AddWithValue("layer", NpgsqlDbType.Smallint, layer);
		command.Parameters.AddWithValue("periodStart", NpgsqlDbType.Timestamp, periodStart);
		command.Parameters.AddWithValue(
			"periodEndExclusive", NpgsqlDbType.Timestamp, PeriodEndExclusive(periodStart, layer));

		return await command.ExecuteNonQueryAsync(cancellationToken);
	}

	// One period past its start. The width is read off LayerThinner's own lattice rather than restated
	// here — the boundary before this one sits exactly one period earlier — so calendar alignment stays in
	// the single method that owns it and an experiment replacing that method moves these bounds with it.
	private static DateTime PeriodEndExclusive(DateTime periodStart, short layer)
	{
		var previousStart = LayerThinner.PeriodStart(periodStart.AddTicks(-1), layer);

		return periodStart.AddTicks(periodStart.Ticks - previousStart.Ticks);
	}
}
