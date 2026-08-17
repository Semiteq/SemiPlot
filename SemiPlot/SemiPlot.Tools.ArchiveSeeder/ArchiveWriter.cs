using FluentResults;

using Npgsql;

using NpgsqlTypes;

namespace SemiPlot.Tools.ArchiveSeeder;

// Connects as scada_writer and creates the archive tables itself, the way Simple-Scada 2 creates its
// own tables on a site. That keeps SemiBase's default-privileges chain on a path exercised by every
// seeded run instead of leaving it to commissioning day.
public sealed class ArchiveWriter(string connectionString)
{
	// The script is a resource rather than a file under sql/: a console binary running out of
	// Artifacts/bin/ and a test assembly running out of its own output directory have no path to the
	// repository at runtime.
	public const string SchemaResourceName = "SemiPlot.Tools.ArchiveSeeder.semiplot_dev.sql";

	// The one probe for "does this database already carry an archive", shared with the test fixture so
	// that the writer's refusal and the fixture's reuse decision cannot drift apart.
	public const string ArchiveExistsCommand = "SELECT to_regclass('public.trends') IS NOT NULL;";

	private const string CopyCommand = "COPY public.trends (id, l, t, v, q) FROM STDIN (FORMAT BINARY)";

	public static string ReadSchemaScript()
	{
		using var stream = typeof(ArchiveWriter).Assembly.GetManifestResourceStream(SchemaResourceName)
			?? throw new InvalidOperationException(
				$"The schema resource '{SchemaResourceName}' is missing from the seeder assembly.");

		using var reader = new StreamReader(stream);

		return reader.ReadToEnd();
	}

	public async Task<Result<long>> WriteAsync(
		IEnumerable<ArchiveRow> rows,
		DateTime start,
		DateTime endExclusive,
		CancellationToken cancellationToken = default)
	{
		var statements = PartitionScript.CreateStatements(start, endExclusive);

		try
		{
			await using var connection = new NpgsqlConnection(connectionString);

			await connection.OpenAsync(cancellationToken);

			if (await ArchiveExistsAsync(connection, cancellationToken))
			{
				return Result.Fail<long>(
					"public.trends already exists: the seeder writes into an empty archive and never replaces one.");
			}

			// One transaction over the schema, the partitions and the COPY. A COPY that fails part-way
			// would otherwise leave public.trends created and partly filled, and every later run would
			// be refused by the existence check with no recovery but dropping the table by hand.
			await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

			await ExecuteAsync(connection, transaction, ReadSchemaScript(), cancellationToken);

			foreach (var statement in statements)
			{
				await ExecuteAsync(connection, transaction, statement, cancellationToken);
			}

			var written = await CopyAsync(connection, rows, cancellationToken);

			await transaction.CommitAsync(cancellationToken);

			return Result.Ok(written);
		}
		catch (Exception exception) when (IsReportable(exception))
		{
			return Result.Fail<long>(new ExceptionalError(exception.Message, exception));
		}
	}

	// A malformed connection string throws out of the NpgsqlConnection constructor as an
	// ArgumentException or a FormatException, well before any Npgsql failure type can appear, so a
	// mistyped --connection has to reach the caller as a stated error rather than as a stack trace.
	internal static bool IsReportable(Exception exception)
	{
		return exception is NpgsqlException or InvalidOperationException or ArgumentException or FormatException;
	}

	private static async Task<bool> ArchiveExistsAsync(
		NpgsqlConnection connection,
		CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand(ArchiveExistsCommand, connection);

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
