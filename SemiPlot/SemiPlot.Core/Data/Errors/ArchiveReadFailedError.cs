using FluentResults;

namespace SemiPlot.Core.Data.Errors;

/// <summary>
/// The archive rejected the read for a reason this build does not recognise. It is the named answer for
/// a SQLSTATE no other <c>Archive*</c> type claims, so nothing crosses the provider boundary as a bare
/// exception or as an untyped <c>Result.Fail(string)</c> a consumer cannot route on.
/// <para>
/// <see cref="SqlState"/> is what an engineer needs to name the cause, and it is the empty string when
/// the failure carried none — a client-side fault such as an unexpected column type reaches this type
/// with no server answer behind it.
/// </para>
/// </summary>
public sealed class ArchiveReadFailedError(string host, int port, string database, string sqlState)
	: Error(Describe(host, port, database, sqlState))
{
	public string Host { get; } = host;

	public int Port { get; } = port;

	public string Database { get; } = database;

	public string SqlState { get; } = sqlState;

	private static string Describe(string host, int port, string database, string sqlState)
	{
		var suffix = string.IsNullOrEmpty(sqlState)
			? "."
			: $" (SQLSTATE {sqlState}).";

		return $"The archive '{database}' at {host}:{port} rejected the read for an unrecognised reason{suffix}";
	}
}
