using System.Net.Sockets;

using FluentResults;

using Npgsql;

using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Translates everything a read can throw into an <see cref="ArchiveError"/>; a caller's
/// <see cref="OperationCanceledException"/> leaves as it arrived, while the server's own <c>57014</c>
/// maps to <see cref="ArchiveFault.QueryTimedOut"/>.
/// </summary>
internal sealed class ArchiveExceptionMapper
{
	private readonly PostgresConnectionSettings _settings;

	public ArchiveExceptionMapper(PostgresConnectionSettings settings)
	{
		_settings = settings;
	}

	/// <summary>Maps what the read threw; <c>relation</c> is read on the <c>42P01</c> path only.</summary>
	public Error Map(Exception exception, string? relation)
	{
		if (exception is OperationCanceledException)
		{
			throw exception;
		}

		return Classify(exception, relation).CausedBy(exception);
	}

	// Everything Npgsql raises that is not a server-delivered error is a connection-level failure: a
	// refused or reset socket, or the command bound firing, both wrapped in an NpgsqlException.
	private static bool IsConnectionFailure(Exception exception)
	{
		return exception is NpgsqlException or SocketException or TimeoutException;
	}

	private ArchiveError Classify(Exception exception, string? relation)
	{
		if (exception is PostgresException postgres)
		{
			return MapSqlState(postgres, relation);
		}

		return Fault(IsConnectionFailure(exception) ? ArchiveFault.Unreachable : ArchiveFault.ReadFailed);
	}

	private ArchiveError MapSqlState(PostgresException postgres, string? relation)
	{
		return postgres.SqlState switch
		{
			PostgresErrorCodes.InvalidCatalogName => Fault(ArchiveFault.DatabaseMissing),
			PostgresErrorCodes.UndefinedTable => Fault(ArchiveFault.TableMissing, relation ?? string.Empty),
			PostgresErrorCodes.InvalidPassword
				or PostgresErrorCodes.InvalidAuthorizationSpecification
				or PostgresErrorCodes.InsufficientPrivilege
				=> Fault(ArchiveFault.AccessDenied, _settings.Username),
			// The server's own MessageText names the column it could not resolve; nothing on this side knows it.
			PostgresErrorCodes.UndefinedColumn => Fault(ArchiveFault.ShapeUnexpected, postgres.MessageText),
			PostgresErrorCodes.QueryCanceled => Fault(ArchiveFault.QueryTimedOut),
			_ => Fault(ArchiveFault.ReadFailed, postgres.SqlState)
		};
	}

	private ArchiveError Fault(ArchiveFault kind, string detail = "")
	{
		return new ArchiveError(kind, _settings.Host, _settings.Port, _settings.Database, detail);
	}
}
