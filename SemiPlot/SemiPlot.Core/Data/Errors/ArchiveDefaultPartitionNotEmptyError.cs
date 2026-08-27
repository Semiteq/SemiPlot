using FluentResults;

namespace SemiPlot.Core.Data.Errors;

/// <summary>
/// The archive's default partition carries rows. <c>trends</c> is <c>PARTITION BY RANGE (t)</c> with one
/// partition per calendar day and a default partition catching everything that misses one, so a row lands
/// there only when the engine failed to create the day it belongs to
/// (docs/architecture/scada-archive.md, Reader hazards). The default partition is never pruned and defeats
/// partition elimination, which makes a non-empty one a fault signal rather than a normal state.
/// <para>
/// <b>It stops nothing.</b> Every read still returns those rows — the planner simply has to open the
/// default partition on each of them. So this reaches the operator as a warning beside a working chart,
/// never as a failed startup: refusing to start would hide a readable archive over a planning fault the
/// operator fixes on the SCADA side.
/// </para>
/// </summary>
public sealed class ArchiveDefaultPartitionNotEmptyError(string host, int port, string database, string partition)
	: Error(Describe(host, port, database, partition))
{
	public string Host { get; } = host;

	public int Port { get; } = port;

	public string Database { get; } = database;

	/// <summary>
	/// The default partition the rows were found in, qualified as the read named it.
	/// </summary>
	public string Partition { get; } = partition;

	private static string Describe(string host, int port, string database, string partition)
	{
		var archive = FormattableString.Invariant($"The archive '{database}' at {host}:{port}");

		return $"{archive} holds rows in its default partition '{partition}', so a daily partition was "
			+ "missing when they were written. The rows are still read; the reads are slower.";
	}
}
