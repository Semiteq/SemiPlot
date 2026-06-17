using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// Selects the visible pens' envelopes and maps a cursor X to each one's center-channel value via the
// renderer-agnostic CursorReadoutModel. Kept separate so the chart view model stays within its size
// budget and the visibility filtering has one home; the model itself holds the interpolation/gap math.
public sealed class ChartCursorReader(
	IReadOnlyDictionary<long, TrendPenState> pensById,
	IReadOnlyDictionary<long, PenHistoryEnvelope> envelopesById)
{
	private readonly CursorReadoutModel _cursorReadout = new();
	private readonly IReadOnlyDictionary<long, TrendPenState> _pensById = pensById;
	private readonly IReadOnlyDictionary<long, PenHistoryEnvelope> _envelopesById = envelopesById;

	public IReadOnlyDictionary<long, double?> ReadAt(DateTime cursorTime)
	{
		var visibleEnvelopes = _pensById.Values
			.Where(state => state.IsVisible && _envelopesById.ContainsKey(state.Pen.ProjectVarId))
			.Select(state => _envelopesById[state.Pen.ProjectVarId])
			.ToArray();

		return _cursorReadout.ReadAt(cursorTime, visibleEnvelopes);
	}
}
