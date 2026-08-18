using FluentResults;

namespace SemiPlot.Core.Data.Errors;

/// <summary>
/// The server ended the read because it passed its <c>statement_timeout</c>. PostgreSQL answers
/// SQLSTATE <c>57014</c> both for that bound and for a client-issued cancel, so a consumer maps
/// <c>57014</c> onto this type only after checking that its own cancellation token is not the cause;
/// reporting a user's pan or zoom as an exceeded bound sends the operator after a server setting that
/// is working as configured.
/// <para>
/// <see cref="Timeout"/> is the <b>effective</b> <c>statement_timeout</c> the failing session ran
/// under, read back from that session, not a value SemiPlot configured — SemiPlot sends no
/// <c>statement_timeout</c> in any form, so the bound is the reader role's and the operator has to be
/// told the number the server actually applied.
/// </para>
/// </summary>
public sealed class ArchiveQueryTimedOutError(string host, int port, string database, TimeSpan timeout)
	: Error(Describe(host, port, database, timeout))
{
	public string Host { get; } = host;

	public int Port { get; } = port;

	public string Database { get; } = database;

	public TimeSpan Timeout { get; } = timeout;

	private static string Describe(string host, int port, string database, TimeSpan timeout)
	{
		var seconds = timeout.TotalSeconds;

		return FormattableString.Invariant(
			$"The read of archive '{database}' at {host}:{port} exceeded its configured bound of {seconds} s.");
	}
}
