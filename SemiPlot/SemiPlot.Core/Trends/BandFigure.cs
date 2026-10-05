namespace SemiPlot.Core.Trends;

/// <summary>One unbroken run of the minimap band in control coordinates: max forward, min back, and the centre.</summary>
public sealed record BandFigure(IReadOnlyList<BandPoint> Outline, IReadOnlyList<BandPoint> CenterLine);

public readonly record struct BandPoint(double X, double Y);
