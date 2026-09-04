using System.Net.Sockets;

using Npgsql;

namespace SemiPlot.Tools.ArchiveSeeder;

/// <summary>What a converge run reports: the wait for the server, and the archive and catalogue it filled.</summary>
public sealed record ConvergeResult(TimeSpan ReadinessWait, long RowsWritten, int TagsWritten, string Database);

// The bench-only verb: docs/architecture/bench.md#the-application-bench.
public static class Converge
{
	private static readonly TimeSpan _readinessBound = TimeSpan.FromSeconds(60);

	private static readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(500);

	private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(1);

	private const string DatabasePrefix = "semiplot_";

	public static async Task<ConvergeResult> RunAsync(
		ConvergeOptions options,
		CancellationToken cancellationToken = default)
	{
		var database = new NpgsqlConnectionStringBuilder(options.ConnectionString).Database
			?? throw new SeederException("--connection must name a database.");

		RequireBenchDatabase(database);

		var waited = await WaitForServerAsync(options.AdminConnectionString, cancellationToken);

		await RecreateAsync(options.AdminConnectionString, database, cancellationToken);

		var adminOnStand = RePointDatabase(options.AdminConnectionString, database);

		// A rerun in the same process pools a physical connection FORCE just terminated: without
		// clearing, the next OpenAsync on the same connection string can hand that dead connection back.
		NpgsqlConnection.ClearPool(new NpgsqlConnection(options.ConnectionString));
		NpgsqlConnection.ClearPool(new NpgsqlConnection(adminOnStand));

		var seederOptions = new SeederOptions(
			options.ConnectionString,
			DateTime.SpecifyKind(options.End ?? DateTime.Now, DateTimeKind.Unspecified),
			SeederOptions.DefaultDays,
			SeederOptions.DefaultPenCount,
			SeederOptions.DefaultSeed,
			options.ChangeSeconds,
			SeederOptions.DefaultBreakCount,
			adminOnStand);

		var fill = await SeedFiller.FillAsync(seederOptions, cancellationToken);

		var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString);

		await ConnectionFileWriter.WriteAsync(
			options.ConfigDirectory,
			builder.Host!,
			builder.Port,
			database,
			BenchRoles.ReaderRole,
			BenchRoles.ReaderPassword,
			TimeZoneInfo.Local.Id,
			_pollInterval,
			cancellationToken);

		return new ConvergeResult(waited, fill.RowsWritten, fill.TagsWritten ?? 0, database);
	}

	// The admin connection targets the maintenance database, so the wait covers initdb and `semibase
	// bench` alone: a refused connection is the container still starting, anything else is reported.
	private static async Task<TimeSpan> WaitForServerAsync(string adminConnectionString, CancellationToken cancellationToken)
	{
		var started = DateTime.UtcNow;

		while (true)
		{
			try
			{
				await using var connection = new NpgsqlConnection(adminConnectionString);

				await connection.OpenAsync(cancellationToken);

				return DateTime.UtcNow - started;
			}
			catch (Exception exception) when (IsRefused(exception))
			{
				if (DateTime.UtcNow - started >= _readinessBound)
				{
					throw new SeederException(
						$"The admin connection refused every attempt for {_readinessBound.TotalSeconds:0} s: "
						+ "the container never started accepting connections.");
				}

				await Task.Delay(_retryDelay, cancellationToken);
			}
		}
	}

	// A brief startup FATAL between the entrypoint's temporary server and semibase bench is a Postgres
	// answer, not a refused socket, and carries the same "not ready yet" meaning.
	private static bool IsRefused(Exception exception)
	{
		return exception is SocketException
			or NpgsqlException { InnerException: SocketException }
			or PostgresException { SqlState: PostgresErrorCodes.CannotConnectNow };
	}

	// --connection names whatever the operator passes, and FORCE drops it with no confirmation: the prefix
	// check is the only thing standing between a mistyped --connection and a real archive being destroyed.
	private static void RequireBenchDatabase(string database)
	{
		if (!database.StartsWith(DatabasePrefix, StringComparison.Ordinal) || database == BenchRoles.ProvisionedDatabase)
		{
			throw new SeederException(
				$"--connection names '{database}': converge only recreates a database named '{DatabasePrefix}*' "
				+ $"other than '{BenchRoles.ProvisionedDatabase}' itself, the template it clones from.");
		}
	}

	private static async Task RecreateAsync(string adminConnectionString, string database, CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(adminConnectionString);

		await connection.OpenAsync(cancellationToken);

		await ExecuteAsync(
			connection,
			$"""DROP DATABASE IF EXISTS "{QuoteIdentifier(database)}" WITH (FORCE);""",
			cancellationToken);
		await ExecuteAsync(
			connection,
			$"""CREATE DATABASE "{QuoteIdentifier(database)}" TEMPLATE "{QuoteIdentifier(BenchRoles.ProvisionedDatabase)}";""",
			cancellationToken);
	}

	// Postgres has no parameter binding for an identifier; doubling an embedded quote is what the DDL
	// grammar itself expects between the quotes this string interpolates into.
	private static string QuoteIdentifier(string identifier)
	{
		return identifier.Replace("\"", "\"\"");
	}

	private static async Task ExecuteAsync(NpgsqlConnection connection, string statement, CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand(statement, connection);

		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static string RePointDatabase(string connectionString, string database)
	{
		var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = database };

		return builder.ConnectionString;
	}
}
