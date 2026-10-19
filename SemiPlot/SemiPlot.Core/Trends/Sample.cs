namespace SemiPlot.Core.Trends;

/// <summary>A null <see cref="Value"/> is a break for the pen from this moment until its next sample.</summary>
public sealed record Sample(int PenId, DateTime TimestampUtc, double? Value);
