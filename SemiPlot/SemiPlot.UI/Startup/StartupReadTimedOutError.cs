using FluentResults;

namespace SemiPlot.UI.Startup;

/// <summary>
/// A startup read did not answer inside the bound <see cref="StartupProbe"/> gives it. This is the
/// caller's bound, not the server's: <c>ArchiveQueryTimedOutError</c> reports a server that ended
/// the read itself, while this type reports that startup stopped waiting for a read still in flight.
/// <para>
/// <see cref="Read"/> names which startup read it was, in English, so the log line says what the
/// application was doing when it gave up.
/// </para>
/// </summary>
public sealed class StartupReadTimedOutError(string read, TimeSpan bound)
	: Error(Describe(read, bound))
{
	public string Read { get; } = read;

	public TimeSpan Bound { get; } = bound;

	private static string Describe(string read, TimeSpan bound)
	{
		return FormattableString.Invariant(
			$"The startup read of the {read} did not answer within {bound.TotalSeconds} s.");
	}
}
