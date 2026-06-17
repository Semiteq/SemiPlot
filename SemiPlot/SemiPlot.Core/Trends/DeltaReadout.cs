namespace SemiPlot.Core.Trends;

// Result of the dual-cursor measurement: the absolute time span between the two cursors and the
// active pen's center-channel change across them. DeltaY is null when either endpoint falls in a gap
// or outside the active pen's range, so the measurement is only reported when both ends resolve.
public sealed record DeltaReadout(TimeSpan DeltaTime, double? DeltaY);
