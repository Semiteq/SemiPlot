using Npgsql;

using NpgsqlTypes;

namespace SemiPlot.Tools.ArchiveSeeder;

// The demo writer's coarse layers, thinned by LayerThinner over rows read back from the finer layer.
// Writing the open period's first row as the period opens keeps the seam inside FreshTail's clamp.
// docs/architecture/bench.md#the-demo-writer
public static class CoarseFlush
{
	private const string PeriodRowsCommand = """
		SELECT id, l, t, v, q
		FROM public.trends
		WHERE l = @finer AND t >= @periodStart AND t < @periodEndExclusive
		ORDER BY id, t;
		""";

	private const string InsertCommand = """
		INSERT INTO public.trends (id, l, t, v, q)
		SELECT * FROM unnest(@ids, @layers, @ts, @vs, @qs)
		ON CONFLICT DO NOTHING;
		""";

	// The open period's first raw row, per pen, at the coarse layer. Only the first row is written early,
	// because only it is already final: the closed flush selects the same row and ON CONFLICT DO NOTHING
	// then skips it, so the early write adds nothing to the period's final content.
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

	public static async Task<long> FlushAsync(
		FollowOptions options,
		DateTime previousTickLocal,
		DateTime nowLocal,
		CancellationToken cancellationToken = default)
	{
		await using var connection = new NpgsqlConnection(options.ConnectionString);

		await connection.OpenAsync(cancellationToken);

		var inserted = 0L;

		// The identifiers the follow loop itself writes. scada_writer holds no privilege on semiplot_tags,
		// so the pens are taken from the generator rather than from a catalogue read.
		var penIds = RawLayerGenerator.SelectPens(options.PenCount)
			.Select(pen => pen.PenId)
			.ToArray();

		// Minute, then hour, then day: each layer reads the one below it, so the finer layer of a closing
		// period has to be complete before the coarser layer is thinned from it.
		foreach (var layer in LayerThinner.CoarseLayers)
		{
			inserted += await OpenPeriodAsync(connection, layer, penIds, nowLocal, cancellationToken);

			inserted += await FlushLayerAsync(connection, layer, previousTickLocal, nowLocal, cancellationToken);
		}

		return inserted;
	}

	// A pen with no raw row at or after the period start contributes nothing.
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

	// Every period the two instants leave behind, not only the first.
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
		var rows = await ReadFinerLayerAsync(connection, layer, periodStart, cancellationToken);

		return rows.Count == 0
			? 0L
			: await InsertAsync(connection, LayerThinner.Thin(rows, layer), cancellationToken);
	}

	private static async Task<IReadOnlyList<ArchiveRow>> ReadFinerLayerAsync(
		NpgsqlConnection connection,
		short layer,
		DateTime periodStart,
		CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand(PeriodRowsCommand, connection);

		command.Parameters.AddWithValue("finer", NpgsqlDbType.Smallint, FinerLayer(layer));
		command.Parameters.AddWithValue("periodStart", NpgsqlDbType.Timestamp, periodStart);
		command.Parameters.AddWithValue(
			"periodEndExclusive", NpgsqlDbType.Timestamp, PeriodEndExclusive(periodStart, layer));

		await using var reader = await command.ExecuteReaderAsync(cancellationToken);

		var rows = new List<ArchiveRow>();

		while (await reader.ReadAsync(cancellationToken))
		{
			// trends.v is nullable while ArchiveRow.Value is not, and a NULL is neither a minimum nor a
			// maximum, so such a row is no candidate for any of the four the thinner selects.
			if (reader.IsDBNull(3))
			{
				continue;
			}

			rows.Add(new ArchiveRow(
				reader.GetInt32(0),
				reader.GetInt16(1),
				reader.GetDateTime(2),
				reader.GetDouble(3),
				reader.GetInt32(4)));
		}

		return rows;
	}

	private static async Task<long> InsertAsync(
		NpgsqlConnection connection,
		IReadOnlyList<ArchiveRow> rows,
		CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand(InsertCommand, connection);

		command.Parameters.AddWithValue(
			"ids", NpgsqlDbType.Array | NpgsqlDbType.Integer, rows.Select(row => row.Id).ToArray());
		command.Parameters.AddWithValue(
			"layers", NpgsqlDbType.Array | NpgsqlDbType.Smallint, rows.Select(row => row.Layer).ToArray());
		command.Parameters.AddWithValue(
			"ts", NpgsqlDbType.Array | NpgsqlDbType.Timestamp, rows.Select(row => row.Timestamp).ToArray());
		command.Parameters.AddWithValue(
			"vs", NpgsqlDbType.Array | NpgsqlDbType.Double, rows.Select(row => row.Value).ToArray());
		command.Parameters.AddWithValue(
			"qs", NpgsqlDbType.Array | NpgsqlDbType.Integer, rows.Select(row => row.Quality).ToArray());

		return await command.ExecuteNonQueryAsync(cancellationToken);
	}

	// The minute layer is thinned from the raw rows; every coarser layer from the layer below it.
	private static short FinerLayer(short layer)
	{
		return layer == LayerThinner.MinuteLayer ? ArchiveRow.RawLayer : (short)(layer - 1);
	}

	// The width is read off LayerThinner's own lattice so calendar alignment lives in one method.
	private static DateTime PeriodEndExclusive(DateTime periodStart, short layer)
	{
		var previousStart = LayerThinner.PeriodStart(periodStart.AddTicks(-1), layer);

		return periodStart.AddTicks(periodStart.Ticks - previousStart.Ticks);
	}
}
