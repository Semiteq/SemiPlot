namespace SemiPlot.Core.Trends;

public sealed record PenRealtimeValues(
	long PenId,
	IReadOnlyList<double?> Values);
