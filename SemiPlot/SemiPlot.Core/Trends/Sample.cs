namespace SemiPlot.Core.Trends;

/// <summary>A null <see cref="Value"/> is a break for the pen from this moment until its next sample.</summary>
public sealed record Sample(int PenId, DateTime TimestampUtc, double? Value)
{
	/// <summary>The time axis' smallest step: a break placed beside a known stamp sits this far from it.</summary>
	public static readonly TimeSpan BreakStep = TimeSpan.FromMilliseconds(1);
}
