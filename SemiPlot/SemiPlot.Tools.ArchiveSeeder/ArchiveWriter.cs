using Npgsql;

using NpgsqlTypes;

namespace SemiPlot.Tools.ArchiveSeeder;

// Connects as scada_writer and fills the archive table that provisioning already created, the way
// Simple-Scada 2 fills its own on a site. That keeps SemiBase's default-privileges chain on a path
// exercised by every seeded run instead of leaving it to commissioning day.
public sealed class ArchiveWriter(string connectionString)
{
	// The archive table is provisioning's, not this seeder's, so its presence is a precondition of a
	// seeding run. Its absence names a database `semibase bench` never touched.
	public const string ArchiveExistsCommand = "SELECT to_regclass('public.trends') IS NOT NULL;";

	// What a previous seeding run leaves: rows, and the day partitions the run creates before its COPY.
	// Provisioning creates the table empty and tpdefault with it, so neither of those is evidence of a
	// seeding run and neither counts here.
	public const string ArchiveIsSeededCommand = """
		SELECT EXISTS (SELECT 1 FROM public.trends)
			OR EXISTS (
				SELECT 1
				FROM pg_inherits
				JOIN pg_class AS partition ON partition.oid = pg_inherits.inhrelid
				WHERE pg_inherits.inhparent = to_regclass('public.trends')
					AND partition.relname <> 'tpdefault');
		""";

	private const string CopyCommand = "COPY public.trends (id, l, t, v, q) FROM STDIN (FORMAT BINARY)";

	/// <summary>
	/// The number of rows written. A missing archive table or a seeded archive is refused with a
	/// <see cref="SeederException"/>; a connection that cannot be made or a rejected statement throws.
	/// allowExistingRows is what a follow run sets: it skips the seeded refusal and nothing else.
	/// </summary>
	public async Task<long> WriteAsync(
		IEnumerable<ArchiveRow> rows,
		DateTime start,
		DateTime endExclusive,
		bool allowExistingRows = false,
		CancellationToken cancellationToken = default)
	{
		var statements = PartitionScript.CreateStatements(start, endExclusive);

		await using var connection = new NpgsqlConnection(connectionString);

		await connection.OpenAsync(cancellationToken);

		if (!await ScalarIsTrueAsync(connection, ArchiveExistsCommand, cancellationToken))
		{
			throw new SeederException(
				"public.trends does not exist: the archive table is created by provisioning, so run "
				+ "`semibase bench` against this database before seeding it.");
		}

		// A seeding run fills an empty archive in one go, so a half-filled one read as a whole one is the
		// failure this refusal prevents. An appending run has the opposite contract: it adds to an archive
		// somebody else already filled.
		if (!allowExistingRows && await ScalarIsTrueAsync(connection, ArchiveIsSeededCommand, cancellationToken))
		{
			throw new SeederException(
				"public.trends already carries rows or day partitions: the seeder fills an empty archive "
				+ "and never adds to one.");
		}

		// One transaction over the partitions and the COPY. A COPY that fails part-way would otherwise
		// leave the day partitions behind, and every later run would be refused by the seeded check with
		// no recovery but dropping them by hand.
		await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

		foreach (var statement in statements)
		{
			await ExecuteAsync(connection, transaction, statement, cancellationToken);
		}

		var written = await CopyAsync(connection, rows, cancellationToken);

		await transaction.CommitAsync(cancellationToken);

		return written;
	}

	private static async Task<bool> ScalarIsTrueAsync(
		NpgsqlConnection connection,
		string statement,
		CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand(statement, connection);

		return await command.ExecuteScalarAsync(cancellationToken) is true;
	}

	private static async Task ExecuteAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		string statement,
		CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand(statement, connection, transaction);

		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task<long> CopyAsync(
		NpgsqlConnection connection,
		IEnumerable<ArchiveRow> rows,
		CancellationToken cancellationToken)
	{
		await using var writer = await connection.BeginBinaryImportAsync(CopyCommand, cancellationToken);
		var written = 0L;

		foreach (var row in rows)
		{
			await writer.StartRowAsync(cancellationToken);
			await writer.WriteAsync(row.Id, NpgsqlDbType.Integer, cancellationToken);
			await writer.WriteAsync(row.Layer, NpgsqlDbType.Smallint, cancellationToken);
			await writer.WriteAsync(row.Timestamp, NpgsqlDbType.Timestamp, cancellationToken);
			await writer.WriteAsync(row.Value, NpgsqlDbType.Double, cancellationToken);
			await writer.WriteAsync(row.Quality, NpgsqlDbType.Integer, cancellationToken);

			written++;
		}

		await writer.CompleteAsync(cancellationToken);

		return written;
	}
}
