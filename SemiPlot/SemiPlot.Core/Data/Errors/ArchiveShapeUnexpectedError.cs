using FluentResults;

namespace SemiPlot.Core.Data.Errors;

/// <summary>
/// The archive table exists but does not carry the columns SemiPlot reads, so a statement this build
/// issues names a column the server does not know. PostgreSQL answers SQLSTATE <c>42703</c>
/// (<c>undefined_column</c>), and this type is what turns that answer into a state naming the shape
/// rather than into an unrecognised read failure.
/// <para>
/// <b>There is no prober behind this type, and none may be added.</b> The state is reached from a real
/// read that failed; the type names it rather than detects it. A reader comparing
/// <c>information_schema.columns</c> against an expected shape held here would be a second
/// transcription of the vendor DDL, which the provisioning slice's scope guard forbids: the tool that
/// creates the table has nothing to verify it against, and a copy of its shape in this repository
/// would be the drift that move exists to kill.
/// </para>
/// <para>
/// <see cref="Detail"/> is the server's own message text, which already names the column it could not
/// resolve. It is carried verbatim rather than parsed: the operator needs the column name, and the
/// server is the only party that knows it.
/// </para>
/// </summary>
public sealed class ArchiveShapeUnexpectedError(string host, int port, string database, string detail)
	: Error(Describe(host, port, database, detail))
{
	public string Host { get; } = host;

	public int Port { get; } = port;

	public string Database { get; } = database;

	public string Detail { get; } = detail;

	private static string Describe(string host, int port, string database, string detail)
	{
		var archive = FormattableString.Invariant($"The archive '{database}' at {host}:{port}");
		var suffix = string.IsNullOrEmpty(detail)
			? "."
			: $": {detail}";

		return $"{archive} does not carry the columns SemiPlot reads (SQLSTATE 42703){suffix}";
	}
}
