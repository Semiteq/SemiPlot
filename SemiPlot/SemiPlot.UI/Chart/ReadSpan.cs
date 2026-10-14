namespace SemiPlot.UI.Chart;

/// <summary>The time the reads behind one pen's envelope in hand covered.</summary>
internal readonly record struct ReadSpan(DateTime FromUtc, DateTime ToUtc);
