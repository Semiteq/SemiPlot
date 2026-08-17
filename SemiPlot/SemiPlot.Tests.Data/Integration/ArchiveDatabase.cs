using Npgsql;

namespace SemiPlot.Tests.Data.Integration;

// One database per test class, cloned from the seeded template so the schema apply and the COPY are
// skipped.
public sealed class ArchiveDatabase(PostgresServer postgresServer, SemaphoreSlim creationGate, string name)
	: IAsyncDisposable
{
	public const string ClonePrefix = "semiplot_clone_";

	public const string CountDatabasesCommand = "SELECT count(*) FROM pg_database WHERE datname = @name;";

	public string Name { get; } = name;

	public string AdminConnectionString => postgresServer.AdminConnectionStringFor(Name);

	public string WriterConnectionString => postgresServer.WriterConnectionStringFor(Name);

	public string ReaderConnectionString => postgresServer.ReaderConnectionStringFor(Name);

	public static async Task<ArchiveDatabase> CloneAsync(
		PostgresServer postgresServer,
		SemaphoreSlim creationGate,
		string templateDatabase,
		CancellationToken cancellationToken = default)
	{
		var name = NewName();

		await CreateAsync(
			postgresServer,
			creationGate,
			$"""CREATE DATABASE "{name}" TEMPLATE "{templateDatabase}";""",
			cancellationToken);

		return new ArchiveDatabase(postgresServer, creationGate, name);
	}

	// template0 rather than the server's own template1, with encoding and locale stated: a test that
	// asks for an empty database must get the same one on every machine, not whatever locale the
	// server happens to have been initialised with.
	public static async Task<ArchiveDatabase> EmptyAsync(
		PostgresServer postgresServer,
		SemaphoreSlim creationGate,
		CancellationToken cancellationToken = default)
	{
		var name = NewName();

		await CreateAsync(
			postgresServer,
			creationGate,
			$"""CREATE DATABASE "{name}" TEMPLATE template0 ENCODING 'UTF8' LC_COLLATE 'C' LC_CTYPE 'C';""",
			cancellationToken);

		return new ArchiveDatabase(postgresServer, creationGate, name);
	}

	// A pooled connection counts as a session on the database and makes DROP DATABASE refuse, so the
	// pool is emptied before the drop rather than waited out.
	public static void ClearPool(string connectionString)
	{
		using var connection = new NpgsqlConnection(connectionString);

		NpgsqlConnection.ClearPool(connection);
	}

	public static async Task ExecuteAsync(
		string connectionString,
		string statement,
		CancellationToken cancellationToken = default)
	{
		await using var connection = new NpgsqlConnection(connectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(statement, connection);

		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async ValueTask DisposeAsync()
	{
		ClearPool(AdminConnectionString);
		ClearPool(WriterConnectionString);
		ClearPool(ReaderConnectionString);

		await creationGate.WaitAsync();

		try
		{
			await ExecuteAsync(
				postgresServer.AdminConnectionString,
				$"""DROP DATABASE IF EXISTS "{Name}" WITH (FORCE);""");
		}
		finally
		{
			creationGate.Release();
		}
	}

	private static string NewName()
	{
		return ClonePrefix + Guid.NewGuid().ToString("N")[..12];
	}

	private static async Task CreateAsync(
		PostgresServer postgresServer,
		SemaphoreSlim creationGate,
		string statement,
		CancellationToken cancellationToken)
	{
		await creationGate.WaitAsync(cancellationToken);

		try
		{
			await ExecuteAsync(postgresServer.AdminConnectionString, statement, cancellationToken);
		}
		finally
		{
			creationGate.Release();
		}
	}
}
