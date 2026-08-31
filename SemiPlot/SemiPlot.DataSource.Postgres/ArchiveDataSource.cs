using Npgsql;

using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Owns the pooled <see cref="NpgsqlDataSource"/> and the per-command time bound the connection string
/// deliberately leaves open. <see cref="PostgresConnectionSettings.ConnectionString"/> sets
/// <c>Command Timeout=0</c> so that Npgsql's implicit 30 s never pre-empts the server's own
/// <c>statement_timeout</c>.
/// <para>
/// The surface is an open connection plus a command built against it, rather than one call taking a
/// statement string, because later reads bind parameters onto the command.
/// </para>
/// </summary>
public sealed class ArchiveDataSource : IDisposable, IAsyncDisposable
{
	// A fixed client backstop, no longer derived from the server's own bound and therefore no longer
	// guaranteed above it: on a site whose reader role exceeds five minutes the two cancels race.
	private static readonly TimeSpan _commandTimeoutBackstop = TimeSpan.FromMinutes(5);

	private readonly NpgsqlDataSource _dataSource;

	public ArchiveDataSource(PostgresConnectionSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		_dataSource = new NpgsqlDataSourceBuilder(settings.ConnectionString).Build();
	}

	/// <summary>
	/// An open connection from the pool. Opening reads nothing from the server, so the bound a command
	/// carries does not depend on when the physical connection opened.
	/// </summary>
	public ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
	{
		return _dataSource.OpenConnectionAsync(cancellationToken);
	}

	/// <summary>
	/// A command over an already-open connection, carrying the bound and nothing else. The caller adds
	/// parameters.
	/// </summary>
	public NpgsqlCommand CreateCommand(string statementText, NpgsqlConnection connection)
	{
		ArgumentNullException.ThrowIfNull(statementText);
		ArgumentNullException.ThrowIfNull(connection);

		var command = connection.CreateCommand();

		command.CommandText = statementText;
		command.CommandTimeout = ResolveCommandTimeoutSeconds();

		return command;
	}

	public void Dispose()
	{
		_dataSource.Dispose();
	}

	public ValueTask DisposeAsync()
	{
		return _dataSource.DisposeAsync();
	}

	// The server's statement_timeout is what stops a long read; this backstop stops a read the server is not
	// answering. It is no longer derived from the server's own bound and therefore no longer guaranteed above
	// it, so on a site whose reader role exceeds five minutes the two cancels race. Npgsql surfaces this bound
	// firing as an NpgsqlException wrapping a TimeoutException — never as a PostgresException carrying 57014 —
	// which IsConnectionFailure routes to ArchiveFault.Unreachable.
	private static int ResolveCommandTimeoutSeconds()
	{
		return (int)_commandTimeoutBackstop.TotalSeconds;
	}
}
