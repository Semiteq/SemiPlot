using Npgsql;

namespace SemiPlot.Tests.Data.Integration;

// One database per test class, cloned from the seeded template so the COPY runs once per run rather
// than once per class.
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

		await CopyAsync(postgresServer, creationGate, templateDatabase, name, cancellationToken);

		return new ArchiveDatabase(postgresServer, creationGate, name);
	}

	// Clones under a stated name and returns nothing to dispose. The seeded template is built this way:
	// it outlives the run that built it, so a persistent server serves the next run without re-seeding.
	public static Task CopyAsync(
		PostgresServer postgresServer,
		SemaphoreSlim creationGate,
		string templateDatabase,
		string name,
		CancellationToken cancellationToken = default)
	{
		return CreateAsync(
			postgresServer,
			creationGate,
			$"""CREATE DATABASE "{name}" TEMPLATE "{templateDatabase}";""",
			cancellationToken);
	}

	public static async Task<bool> ExistsAsync(
		PostgresServer postgresServer,
		string name,
		CancellationToken cancellationToken = default)
	{
		await using var connection = new NpgsqlConnection(postgresServer.AdminConnectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(CountDatabasesCommand, connection);

		command.Parameters.AddWithValue("name", name);

		return await command.ExecuteScalarAsync(cancellationToken) is long and > 0;
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
