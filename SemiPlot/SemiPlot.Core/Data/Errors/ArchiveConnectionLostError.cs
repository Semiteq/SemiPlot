using FluentResults;

namespace SemiPlot.Core.Data.Errors;

/// <summary>
/// The live edge has stopped answering: a run of consecutive poll ticks failed, long enough to be a fault
/// rather than a hiccup. It carries the threshold that run had to reach — the number that separates the
/// two — beside the address the ticks were issued against.
/// <para>
/// The threshold is not a running count. The poll raises this once per outage and keeps failing behind it,
/// so a fault read ten minutes into an outage still names the number of failures that raised it. Reporting
/// the running total would mean re-raising on every tick, which is the banner-per-second the single raise
/// exists to prevent.
/// </para>
/// <para>
/// This is not a read failure. The history the chart already holds is still drawn and the poll keeps
/// trying, so the state is reported on the connection stream rather than returned from a
/// <see cref="Result"/>, and it is withdrawn by the first tick that succeeds.
/// </para>
/// </summary>
public sealed class ArchiveConnectionLostError(string host, int port, string database, int failureThreshold)
	: Error(Describe(host, port, database, failureThreshold))
{
	public string Host { get; } = host;

	public int Port { get; } = port;

	public string Database { get; } = database;

	/// <summary>
	/// The number of consecutive failed reads that raised this fault, fixed at the moment it was raised.
	/// </summary>
	public int FailureThreshold { get; } = failureThreshold;

	private static string Describe(string host, int port, string database, int failureThreshold)
	{
		var edge = FormattableString.Invariant($"The live edge of archive '{database}' at {host}:{port}");
		var failures = FormattableString.Invariant($"{failureThreshold} consecutive failed reads.");

		return $"{edge} stopped answering after {failures}";
	}
}
