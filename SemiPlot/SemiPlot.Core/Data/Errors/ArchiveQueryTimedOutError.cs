using FluentResults;

namespace SemiPlot.Core.Data.Errors;

/// <summary>
/// The server ended the read with SQLSTATE <c>57014</c>: its <c>statement_timeout</c> passed, or an
/// administrator cancelled the statement. A consumer maps <c>57014</c> onto this type only after checking
/// that its own cancellation token is not the cause. The bound itself is the reader role's and is not
/// carried here.
/// </summary>
public sealed class ArchiveQueryTimedOutError(string host, int port, string database)
	: Error(FormattableString.Invariant(
		$"The read of archive '{database}' at {host}:{port} was ended by the server (SQLSTATE 57014)."))
{
	public string Host { get; } = host;

	public int Port { get; } = port;

	public string Database { get; } = database;
}
