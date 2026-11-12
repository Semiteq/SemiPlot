namespace SemiPlot.Core.Data;

/// <summary>An archive wall-clock day that holds raw rows, with its earliest and latest raw sample in UTC.</summary>
public sealed record ArchiveDaySpan(DateOnly Day, DateTime FirstUtc, DateTime LastUtc);
