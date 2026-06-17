namespace SemiPlot.Core.Trends;

// A coalesced realtime update sharing one ascending timeline across pens; a null value marks a gap.
public sealed record RealtimeBatch(
	IReadOnlyList<DateTime> Timestamps,
	IReadOnlyList<PenRealtimeValues> Pens);

public sealed record PenRealtimeValues(
	long PenId,
	IReadOnlyList<double?> Values);
