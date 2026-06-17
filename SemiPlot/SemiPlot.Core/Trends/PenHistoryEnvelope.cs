namespace SemiPlot.Core.Trends;

// Per-pen decimated history as parallel ascending arrays; a gap is NaN in Min/Max/Center at that column.
public sealed record PenHistoryEnvelope(
	long PenId,
	IReadOnlyList<DateTime> Timestamps,
	IReadOnlyList<double> Min,
	IReadOnlyList<double> Max,
	IReadOnlyList<double> Center);
