namespace SemiPlot.Core.Trends;

// Result of the dual-cursor measurement; DeltaY is null when either endpoint is a gap or out of range.
public sealed record DeltaReadout(TimeSpan DeltaTime, double? DeltaY);
