using System.Net.Sockets;

using FluentResults;

using Npgsql;

using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Translates everything a read can throw into the public error vocabulary, so nothing internal crosses
/// the provider boundary. Constructed rather than static because the public types demand values no
/// exception carries: <c>Host</c>, <c>Port</c> and <c>Database</c> on every <c>Archive*</c> type, the
/// username on <see cref="ArchiveAccessDeniedError"/>, and the effective <c>statement_timeout</c> on
/// <see cref="ArchiveQueryTimedOutError"/>, which lives only in the data source's cached field.
/// <para>
/// Cancellation is not part of the vocabulary. A caller's token cancelling raises
/// <see cref="OperationCanceledException"/>, which leaves here as it arrived: in .NET a cancelled
/// operation is not a failed <see cref="Result"/>, and a self-cancelled read is not an error at all.
/// The server's own <c>57014</c> is a different thing and does map, onto
/// <see cref="ArchiveQueryTimedOutError"/>.
/// </para>
/// </summary>
internal sealed class ArchiveExceptionMapper
{
	private readonly PostgresConnectionSettings _settings;
	private readonly Func<TimeSpan?> _effectiveStatementTimeout;

	public ArchiveExceptionMapper(PostgresConnectionSettings settings, Func<TimeSpan?> effectiveStatementTimeout)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(effectiveStatementTimeout);

		_settings = settings;
		_effectiveStatementTimeout = effectiveStatementTimeout;
	}

	/// <param name="exception">What the read threw.</param>
	/// <param name="missingRelation">
	/// The relation a <c>42P01</c> refers to, already resolved by the caller. Required on that path and
	/// unused on every other: <c>ArchiveNotInitialisedError.Table</c> is what consumers route on.
	/// </param>
	public Error Map(Exception exception, string? missingRelation = null)
	{
		ArgumentNullException.ThrowIfNull(exception);

		if (exception is OperationCanceledException)
		{
			throw exception;
		}

		return Classify(exception, missingRelation).CausedBy(exception);
	}

	// Everything Npgsql raises that is not a server-delivered error is a connection-level failure: a
	// refused or reset socket, or the command bound firing. Npgsql wraps both in an NpgsqlException of its
	// own, so the wrapped forms need no clause here.
	private static bool IsConnectionFailure(Exception exception)
	{
		return exception is NpgsqlException or SocketException or TimeoutException;
	}

	// The provider substitutes its own statement's relation before calling, so an unnamed one here is a
	// caller defect rather than a state to paper over.
	private static string RequireRelation(string? missingRelation)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(missingRelation);

		return missingRelation;
	}

	private Error Classify(Exception exception, string? missingRelation)
	{
		if (exception is PostgresException postgres)
		{
			return MapSqlState(postgres, missingRelation);
		}

		if (IsConnectionFailure(exception))
		{
			return new ArchiveUnreachableError(_settings.Host, _settings.Port, _settings.Database);
		}

		return new ArchiveReadFailedError(_settings.Host, _settings.Port, _settings.Database, string.Empty);
	}

	private Error MapSqlState(PostgresException postgres, string? missingRelation)
	{
		var host = _settings.Host;
		var port = _settings.Port;
		var database = _settings.Database;

		return postgres.SqlState switch
		{
			PostgresErrorCodes.InvalidCatalogName => new ArchiveDatabaseMissingError(host, port, database),
			PostgresErrorCodes.UndefinedTable => new ArchiveNotInitialisedError(
				host,
				port,
				database,
				RequireRelation(missingRelation)),
			PostgresErrorCodes.InvalidPassword
				or PostgresErrorCodes.InvalidAuthorizationSpecification
				or PostgresErrorCodes.InsufficientPrivilege
				=> new ArchiveAccessDeniedError(host, port, database, _settings.Username),
			PostgresErrorCodes.QueryCanceled => new ArchiveQueryTimedOutError(
				host,
				port,
				database,
				_effectiveStatementTimeout() ?? TimeSpan.Zero),
			_ => new ArchiveReadFailedError(host, port, database, postgres.SqlState)
		};
	}
}
