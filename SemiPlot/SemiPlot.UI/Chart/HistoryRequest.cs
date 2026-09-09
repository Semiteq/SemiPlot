using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

/// <summary>
/// One archive read: the pens, the range it covers, and the resolution it is read at. The range travels with
/// the request so the result alone tells the prefetch gate which band the envelopes in hand describe.
/// </summary>
/// <remarks>
/// <c>Range.ColumnTarget</c> is the quantized count the gate compares, which a one-pixel resize does not move;
/// <c>TargetColumnCount</c> is the unquantized pixel width the provider decimates to.
/// </remarks>
public sealed record HistoryRequest(
	IReadOnlyList<int> PenIds,
	FetchRange Range,
	int TargetColumnCount)
{
	public DateTime FromUtc => Range.FromUtc;

	public DateTime ToUtc => Range.ToUtc;

	public AggregationLayer Layer => Range.Layer;
}
