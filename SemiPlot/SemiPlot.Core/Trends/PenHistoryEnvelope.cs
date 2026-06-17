namespace SemiPlot.Core.Trends;

// Per-pen decimated history: parallel ascending arrays where, for each column, the band spans
// [Min, Max] and Center is the representative value used by cursor and legend readouts. Gaps are
// represented by NaN in Min/Max/Center so the renderer can break the line and band at the same X.
public sealed record PenHistoryEnvelope(
	long PenId,
	IReadOnlyList<DateTime> Timestamps,
	IReadOnlyList<double> Min,
	IReadOnlyList<double> Max,
	IReadOnlyList<double> Center);
