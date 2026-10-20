using SemiPlot.Core.Data.Errors;

namespace SemiPlot.Core.Data;

/// <summary>
/// What a reader of the archive reports about its connection to it. <see cref="Fault"/> is null while the
/// archive answers and names the fault while it does not.
/// </summary>
public sealed record ArchiveConnectionState(ArchiveError? Fault)
{
	/// <summary>Failed reads in a row that make a fault: one reconnect after a server restart fails one read.</summary>
	public const int ConsecutiveFailuresBeforeFault = 3;

	public static readonly ArchiveConnectionState Connected = new((ArchiveError?)null);

	public bool IsConnected => Fault is null;
}
