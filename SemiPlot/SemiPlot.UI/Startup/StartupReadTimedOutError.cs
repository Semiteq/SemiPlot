using FluentResults;

namespace SemiPlot.UI.Startup;

public enum StartupRead
{
	PenCatalogue,
	ArchiveExtent
}

/// <summary>
/// A startup read did not answer inside the bound <see cref="StartupProbe"/> gives it. This is the
/// caller's bound, not the server's: <c>ArchiveFault.QueryTimedOut</c> reports a server that ended
/// the read itself, while this type reports that startup stopped waiting for a read still in flight.
/// </summary>
public sealed class StartupReadTimedOutError(StartupRead read, TimeSpan bound)
	: Error(Describe(read, bound))
{
	public StartupRead Read { get; } = read;

	public TimeSpan Bound { get; } = bound;

	// This line reaches the log, which stays English; the operator reads the same name from the
	// resource set through ArchiveFailureMapper (docs/architecture/ui-text.md#what-stays-a-literal).
	private static string Describe(StartupRead read, TimeSpan bound)
	{
		var name = read switch
		{
			StartupRead.PenCatalogue => "pen catalogue",
			StartupRead.ArchiveExtent => "archive extent",
			_ => throw new ArgumentOutOfRangeException(nameof(read), read, null)
		};

		return FormattableString.Invariant(
			$"The startup read of the {name} did not answer within {bound.TotalSeconds} s.");
	}
}
