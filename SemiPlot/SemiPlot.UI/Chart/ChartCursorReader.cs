using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class ChartCursorReader(
	IReadOnlyDictionary<int, TrendPenState> pensById,
	IReadOnlyDictionary<int, PenHistoryEnvelope> envelopesById)
{
	private readonly CursorReadoutModel _cursorReadout = new();

	public IReadOnlyDictionary<int, double?> ReadAt(DateTime cursorTime)
	{
		var visibleEnvelopes = pensById.Values
			.Where(state => state.IsVisible && envelopesById.ContainsKey(state.Pen.PenId))
			.Select(state => envelopesById[state.Pen.PenId])
			.ToArray();

		return _cursorReadout.ReadAt(cursorTime, visibleEnvelopes);
	}
}
