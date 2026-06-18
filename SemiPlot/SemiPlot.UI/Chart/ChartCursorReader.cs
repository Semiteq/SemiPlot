using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class ChartCursorReader(
	IReadOnlyDictionary<long, TrendPenState> pensById,
	IReadOnlyDictionary<long, PenHistoryEnvelope> envelopesById)
{
	private readonly CursorReadoutModel _cursorReadout = new();
	private readonly IReadOnlyDictionary<long, PenHistoryEnvelope> _envelopesById = envelopesById;
	private readonly IReadOnlyDictionary<long, TrendPenState> _pensById = pensById;

	public IReadOnlyDictionary<long, double?> ReadAt(DateTime cursorTime)
	{
		var visibleEnvelopes = _pensById.Values
			.Where(state => state.IsVisible && _envelopesById.ContainsKey(state.Pen.PenId))
			.Select(state => _envelopesById[state.Pen.PenId])
			.ToArray();

		return _cursorReadout.ReadAt(cursorTime, visibleEnvelopes);
	}
}
