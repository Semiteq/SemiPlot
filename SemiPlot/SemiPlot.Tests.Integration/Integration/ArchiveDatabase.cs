using Npgsql;

namespace SemiPlot.Tests.Integration;

public sealed class ArchiveDatabase(PostgresServer postgresServer, string name) : IAsyncDisposable
{
	private const string ClonePrefix = "semiplot_clone_";

	public string Name { get; } = name;

	public string AdminConnectionString => postgresServer.AdminConnectionStringFor(Name);

	public string WriterConnectionString => postgresServer.WriterConnectionStringFor(Name);

	public string ReaderConnectionString => postgresServer.ReaderConnectionStringFor(Name);

	public static async Task<ArchiveDatabase> CloneAsync(
		PostgresServer postgresServer,
		string templateDatabase,
		CancellationToken cancellationToken = default)
	{
		var name = NewName();

		await CopyAsync(postgresServer, templateDatabase, name, cancellationToken);

		return new ArchiveDatabase(postgresServer, name);
	}

	// Clones under a stated name and returns nothing to dispose. The seeded template is built this way:
	// it is named by a constant and dies with the container, so nothing has to drop it by name.
	public static Task CopyAsync(
		PostgresServer postgresServer,
		string templateDatabase,
		string name,
		CancellationToken cancellationToken = default)
	{
		return ExecuteAsync(
			postgresServer.AdminConnectionString,
			$"""CREATE DATABASE "{QuoteIdentifier(name)}" TEMPLATE "{QuoteIdentifier(templateDatabase)}";""",
			cancellationToken);
	}

	// Postgres has no parameter binding for an identifier; doubling an embedded quote is what the DDL
	// grammar itself expects between the quotes this string interpolates into. Every name reaching here is
	// an internally generated GUID, so this guards intent rather than an exploitable input.
	private static string QuoteIdentifier(string identifier)
	{
		return identifier.Replace("\"", "\"\"");
	}

	// CREATE DATABASE ... TEMPLATE refuses while another session holds the source, and every connection
	// this harness opens is pooled rather than closed.
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
		await ExecuteAsync(
			postgresServer.AdminConnectionString,
			$"""DROP DATABASE IF EXISTS "{QuoteIdentifier(Name)}" WITH (FORCE);""");
	}

	private static string NewName()
	{
		return ClonePrefix + Guid.NewGuid().ToString("N")[..12];
	}
}
