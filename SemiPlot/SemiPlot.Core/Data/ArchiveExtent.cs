namespace SemiPlot.Core.Data;

// The full UTC time span of the stored archive (first sample to last); the minimap maps it onto its strip.
public sealed record ArchiveExtent(DateTime FirstUtc, DateTime LastUtc);
