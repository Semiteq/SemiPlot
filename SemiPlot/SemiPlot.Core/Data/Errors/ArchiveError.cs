using FluentResults;

namespace SemiPlot.Core.Data.Errors;

/// <summary>
/// Every failure the archive can answer with, keyed by <see cref="Kind"/>. <see cref="Detail"/> carries the
/// one per-kind value the operator's remedy names — see <see cref="ArchiveFault"/> — and is empty for the
/// kinds that have none. Consumers route on <see cref="Kind"/> and never on <see cref="Error.Message"/>.
/// </summary>
public sealed class ArchiveError(ArchiveFault kind, string host, int port, string database, string detail = "")
	: Error(Describe(kind, host, port, database, detail))
{
	public ArchiveFault Kind { get; } = kind;

	public string Host { get; } = host;

	public int Port { get; } = port;

	public string Database { get; } = database;

	public string Detail { get; } = detail;

	private static string Describe(ArchiveFault kind, string host, int port, string database, string detail)
	{
		var archive = FormattableString.Invariant($"archive '{database}' at {host}:{port}");

		return kind switch
		{
			ArchiveFault.Unreachable => $"No connection to the {archive}.",
			ArchiveFault.AccessDenied => $"The {archive} refused user '{detail}'; check the password and the grants.",
			ArchiveFault.DatabaseMissing => FormattableString.Invariant(
				$"The server at {host}:{port} answers but holds no database '{database}'."),
			ArchiveFault.TableMissing => $"The {archive} holds no table '{detail}'.",
			ArchiveFault.ShapeUnexpected =>
				$"The {archive} does not carry the columns SemiPlot reads (SQLSTATE 42703): {detail}",
			ArchiveFault.QueryTimedOut => $"The read of the {archive} was ended by the server (SQLSTATE 57014).",
			ArchiveFault.ConnectionLost =>
				$"The live edge of the {archive} stopped answering after {detail} consecutive failed reads.",
			_ => detail.Length == 0
				? $"The {archive} rejected the read for an unrecognised reason."
				: $"The {archive} rejected the read for an unrecognised reason (SQLSTATE {detail})."
		};
	}
}
