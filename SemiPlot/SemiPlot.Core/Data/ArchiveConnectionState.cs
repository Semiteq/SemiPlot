using FluentResults;

namespace SemiPlot.Core.Data;

/// <summary>
/// What a provider reports about its own connection to the archive. Not an error type — it carries one:
/// <see cref="Fault"/> is null while the archive answers and holds the error that names the fault while it
/// does not, so a consumer routes on <see cref="IsConnected"/> and renders <see cref="Fault"/>.
/// <para>
/// <see cref="Connected"/> is one shared instance, so two connected states are reference-equal. That is
/// deliberate and is also why nothing filters this stream with <c>DistinctUntilChanged</c>: every
/// subscription's first successful tick reports <see cref="Connected"/>, and a distinct filter would drop
/// every one after the first — the armed point consumers sequence on.
/// </para>
/// </summary>
public sealed record ArchiveConnectionState(IError? Fault)
{
	/// <summary>
	/// The archive answered. Reported by a subscription's first successful tick and by the first success
	/// after a fault, never by an ordinary tick in between.
	/// </summary>
	public static readonly ArchiveConnectionState Connected = new((IError?)null);

	public bool IsConnected => Fault is null;
}
