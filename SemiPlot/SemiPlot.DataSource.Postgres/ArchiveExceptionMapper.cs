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
/// username on <see cref="ArchiveAccessDeniedError"/>. The relation a <c>42P01</c> names arrives as a
/// call argument, from the read whose own statement touches it.
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

	public ArchiveExceptionMapper(PostgresConnectionSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		_settings = settings;
	}

	/// <param name="exception">What the read threw.</param>
	/// <param name="relation">
	/// The relation the calling statement touches, supplied by the read that failed. Required on the
	/// <c>42P01</c> path and unused on every other: it fills <c>ArchiveNotInitialisedError.Table</c>, which
	/// names the absent object in the detail line. Passed explicitly on every call, null included, so a
	/// new call site cannot omit it by accident.
	/// </param>
	public Error Map(Exception exception, string? relation)
	{
		ArgumentNullException.ThrowIfNull(exception);

		if (exception is OperationCanceledException)
		{
			throw exception;
		}

		return Classify(exception, relation).CausedBy(exception);
	}

	// Everything Npgsql raises that is not a server-delivered error is a connection-level failure: a
	// refused or reset socket, or the command bound firing. Npgsql wraps both in an NpgsqlException of its
	// own, so the wrapped forms need no clause here.
	private static bool IsConnectionFailure(Exception exception)
	{
		return exception is NpgsqlException or SocketException or TimeoutException;
	}

	private Error Classify(Exception exception, string? relation)
	{
		if (exception is PostgresException postgres)
		{
			return MapSqlState(postgres, relation);
		}

		if (IsConnectionFailure(exception))
		{
			return new ArchiveUnreachableError(_settings.Host, _settings.Port, _settings.Database);
		}

		return new ArchiveReadFailedError(_settings.Host, _settings.Port, _settings.Database, string.Empty);
	}

	private Error MapSqlState(PostgresException postgres, string? relation)
	{
		var host = _settings.Host;
		var port = _settings.Port;
		var database = _settings.Database;

		return postgres.SqlState switch
		{
			PostgresErrorCodes.InvalidCatalogName => new ArchiveNotInitialisedError(
				host,
				port,
				database,
				ArchiveObject.Database,
				table: null),
			PostgresErrorCodes.UndefinedTable => new ArchiveNotInitialisedError(
				host,
				port,
				database,
				ArchiveObject.Table,
				relation),
			PostgresErrorCodes.InvalidPassword
				or PostgresErrorCodes.InvalidAuthorizationSpecification
				or PostgresErrorCodes.InsufficientPrivilege
				=> new ArchiveAccessDeniedError(host, port, database, _settings.Username),
			// The server's own MessageText names the column it could not resolve, and nothing on this side
			// knows it: no shape is transcribed here, so the answer is the only source of that name.
			PostgresErrorCodes.UndefinedColumn => new ArchiveShapeUnexpectedError(
				host,
				port,
				database,
				postgres.MessageText),
			PostgresErrorCodes.QueryCanceled => new ArchiveQueryTimedOutError(host, port, database),
			_ => new ArchiveReadFailedError(host, port, database, postgres.SqlState)
		};
	}
}
