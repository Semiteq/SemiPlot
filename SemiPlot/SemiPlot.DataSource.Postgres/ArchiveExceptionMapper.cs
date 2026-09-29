using System.Net.Sockets;

using FluentResults;

using Npgsql;

using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Translates everything a statement can throw into an <see cref="ArchiveError"/>; a caller's
/// <see cref="OperationCanceledException"/> leaves as it arrived, while the server's own <c>57014</c>
/// maps to <see cref="ArchiveFault.QueryTimedOut"/>.
/// </summary>
internal sealed class ArchiveExceptionMapper(PostgresConnectionSettings settings)
{
	private readonly PostgresConnectionSettings _settings = settings;

	/// <summary>
	/// <paramref name="relation"/> is what the statement touches, read on <c>42P01</c> and <c>42883</c> only.
	/// </summary>
	public Error Map(Exception exception, string? relation)
	{
		if (exception is OperationCanceledException)
		{
			throw exception;
		}

		return Classify(exception, relation).CausedBy(exception);
	}

	/// <summary>
	/// <paramref name="subject"/> is the pen or group name the three write kinds carry as their detail.
	/// </summary>
	public Error MapWrite(Exception exception, string subject)
	{
		if (exception is PostgresException postgres && WriteFaultOf(postgres.SqlState) is { } kind)
		{
			return Fault(kind, subject).CausedBy(exception);
		}

		// docs/architecture/data-integration.md#two-error-planes
		return Map(exception, ArchiveStatements.PenCatalogRelations);
	}

	/// <summary>A write that touched no row: the pen or group it names is gone.</summary>
	public ArchiveError RowGone(string subject)
	{
		return Fault(ArchiveFault.RowGone, subject);
	}

	private static ArchiveFault? WriteFaultOf(string sqlState)
	{
		return sqlState switch
		{
			PostgresErrorCodes.CheckViolation => ArchiveFault.ValueRejected,
			PostgresErrorCodes.UniqueViolation => ArchiveFault.NameTaken,
			PostgresErrorCodes.ForeignKeyViolation => ArchiveFault.RowGone,
			_ => null
		};
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
			PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedFunction
				=> Fault(ArchiveFault.TableMissing, relation ?? string.Empty),
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
