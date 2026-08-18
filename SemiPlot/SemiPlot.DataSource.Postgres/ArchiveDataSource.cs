using System.Globalization;

using Microsoft.Extensions.Logging;

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
/// statement string, for two reasons. Later reads bind parameters onto the command. And the effective
/// bound only exists once a physical connection has opened —
/// <see cref="NpgsqlDataSource.CreateCommand(string)"/> stamps <c>CommandTimeout</c> before that
/// happens, so on the first read of a process it would stamp an unset value.
/// </para>
/// </summary>
public sealed class ArchiveDataSource : IDisposable, IAsyncDisposable
{
	// The one command that cannot use the bound it produces, so it carries an explicit short one of its
	// own: Command Timeout=0 would otherwise let a silent server hang every connection open.
	private const int InitializerCommandTimeoutSeconds = 10;

	private static readonly TimeSpan _commandTimeoutMargin = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan _unboundedServerFallback = TimeSpan.FromMinutes(5);

	private readonly NpgsqlDataSource _dataSource;
	private readonly ILogger<ArchiveDataSource> _logger;

	// Written from the initializer callback on whichever thread opened the physical connection and read
	// from command construction on others, so it is exchanged rather than assigned. Negative means no
	// physical connection has opened yet.
	private long _effectiveStatementTimeoutTicks = -1;

	public ArchiveDataSource(PostgresConnectionSettings settings, ILogger<ArchiveDataSource> logger)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(logger);

		_logger = logger;

		var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);

		builder.UsePhysicalConnectionInitializer(ThrowOnSynchronousOpen, CacheEffectiveStatementTimeoutAsync);

		_dataSource = builder.Build();
	}

	/// <summary>
	/// The server's own <c>statement_timeout</c> as of the last physical connection opened, or null when
	/// none has opened yet. <see cref="TimeSpan.Zero"/> means the server bounds nothing. The exception
	/// mapper reads it to fill <c>ArchiveQueryTimedOutError.Timeout</c>, which no exception carries.
	/// </summary>
	public TimeSpan? EffectiveStatementTimeout
	{
		get
		{
			var ticks = Interlocked.Read(ref _effectiveStatementTimeoutTicks);

			return ticks < 0 ? null : TimeSpan.FromTicks(ticks);
		}
	}

	/// <summary>
	/// An open connection from the pool. By the time it returns, the physical-connection initializer has
	/// run, so <see cref="EffectiveStatementTimeout"/> is set and a command built against this connection
	/// carries a real bound.
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

	// Npgsql calls this from NpgsqlConnection.Open, and its own remark is that registering an initializer
	// means supplying both versions. Every read here opens asynchronously, so reaching this is a defect
	// in the caller rather than a state to recover from.
	private static void ThrowOnSynchronousOpen(NpgsqlConnection connection)
	{
		throw new NotSupportedException(
			$"{nameof(ArchiveDataSource)} opens connections asynchronously only, so the synchronous "
			+ "physical-connection initializer is never expected to run.");
	}

	private static int ParseMilliseconds(string? setting)
	{
		return int.TryParse(setting, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds)
			&& milliseconds > 0
				? milliseconds
				: 0;
	}

	private async Task CacheEffectiveStatementTimeoutAsync(NpgsqlConnection connection)
	{
		await using var command = connection.CreateCommand();

		command.CommandText = ArchiveStatements.EffectiveStatementTimeout;
		command.CommandTimeout = InitializerCommandTimeoutSeconds;

		CacheEffectiveStatementTimeout(await command.ExecuteScalarAsync().ConfigureAwait(false) as string);
	}

	// The warning is raised only when the cached value changes: this runs per physical connection open, and
	// repeating it for the life of the process would bury every other entry.
	internal void CacheEffectiveStatementTimeout(string? setting)
	{
		var ticks = TimeSpan.FromMilliseconds(ParseMilliseconds(setting)).Ticks;
		var previousTicks = Interlocked.Exchange(ref _effectiveStatementTimeoutTicks, ticks);

		if (ticks == 0 && previousTicks != 0)
		{
			_logger.LogWarning(
				"The archive bounds no statement: statement_timeout is 0, so read commands take the fixed "
				+ "{FallbackSeconds} s bound instead of the server's own plus a margin.",
				_unboundedServerFallback.TotalSeconds);
		}
	}

	// One step above the server's own bound, so a command that trips this can only mean the server stopped
	// answering — which is why the resulting TimeoutException maps to ArchiveUnreachableError and never to
	// ArchiveQueryTimedOutError.
	private int ResolveCommandTimeoutSeconds()
	{
		var bound = EffectiveStatementTimeout;

		if (bound is null || bound == TimeSpan.Zero)
		{
			return (int)_unboundedServerFallback.TotalSeconds;
		}

		return (int)Math.Ceiling((bound.Value + _commandTimeoutMargin).TotalSeconds);
	}
}
