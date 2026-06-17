namespace SemiPlot.Core.Trends;

// A coalesced realtime update: one ascending timeline shared by every pen in the batch, with each
// pen carrying one nullable value per timestamp. A null value marks a gap (no sample / bad quality)
// at that timestamp for that pen.
public sealed record RealtimeBatch(
	IReadOnlyList<DateTime> Timestamps,
	IReadOnlyList<PenRealtimeValues> Pens);

public sealed record PenRealtimeValues(
	long PenId,
	IReadOnlyList<double?> Values);
