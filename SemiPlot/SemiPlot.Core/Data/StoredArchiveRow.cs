namespace SemiPlot.Core.Data;

/// <summary>One stored archive row; Time is the archive's own wall-clock value, never converted.</summary>
public readonly record struct StoredArchiveRow(int PenId, DateTime Time, double? Value);
