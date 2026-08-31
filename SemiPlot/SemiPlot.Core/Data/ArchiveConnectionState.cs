using SemiPlot.Core.Data.Errors;

namespace SemiPlot.Core.Data;

/// <summary>
/// What a provider reports about its own connection to the archive. <see cref="Fault"/> is null while the
/// archive answers and names the fault while it does not.
/// <para>
/// <see cref="Connected"/> is one shared instance and the stream is never filtered with
/// <c>DistinctUntilChanged</c>: every subscription's first successful tick reports <see cref="Connected"/>,
/// which is the armed point consumers sequence on.
/// </para>
/// </summary>
public sealed record ArchiveConnectionState(ArchiveError? Fault)
{
	public static readonly ArchiveConnectionState Connected = new((ArchiveError?)null);

	public bool IsConnected => Fault is null;
}
