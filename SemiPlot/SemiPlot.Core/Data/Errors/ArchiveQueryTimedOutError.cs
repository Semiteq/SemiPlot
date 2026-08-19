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
/// under, not a value SemiPlot configured — SemiPlot sends no <c>statement_timeout</c> in any form, so
/// the bound is the reader role's and the operator has to be told the number the server actually
/// applied. <see cref="TimeSpan.Zero"/> means there is no number to report: either the bound could not
/// be read, or the server bounds nothing and the <c>57014</c> was a cancel rather than an exceeded
/// bound. Nothing here can tell those two apart, so the message names the SQLSTATE and states that no
/// bound can be named, rather than naming a mechanism: "a bound of 0 s" is a sentence no operator can
/// act on, and blaming <c>statement_timeout</c> sends them after a setting that reads <c>0</c>.
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
		var read = FormattableString.Invariant($"The read of archive '{database}' at {host}:{port}");

		if (timeout == TimeSpan.Zero)
		{
			return FormattableString.Invariant(
				$"{read} was ended by the server (SQLSTATE 57014) and no bound can be named.");
		}

		var seconds = timeout.TotalSeconds;

		return FormattableString.Invariant($"{read} exceeded its configured bound of {seconds} s.");
	}
}
