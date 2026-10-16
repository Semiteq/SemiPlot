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

	/// <summary>One visible pen's value at <paramref name="time"/>; null for a hidden pen or a gap.</summary>
	public double? ReadPenAt(int penId, DateTime time)
	{
		return pensById.TryGetValue(penId, out var state)
			&& state.IsVisible
			&& envelopesById.TryGetValue(penId, out var envelope)
				? CursorReadoutModel.ReadPenAt(time, envelope)
				: null;
	}
}
