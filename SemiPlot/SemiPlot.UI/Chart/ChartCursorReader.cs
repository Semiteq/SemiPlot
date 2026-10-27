using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class ChartCursorReader(
	IReadOnlyDictionary<int, TrendPenState> pensById,
	IReadOnlyDictionary<int, PenHistoryEnvelope> envelopesById)
{
	/// <summary>Every visible pen that has an envelope or a held value at <paramref name="cursorTime"/>.</summary>
	public IReadOnlyDictionary<int, double?> ReadAt(DateTime cursorTime)
	{
		var readouts = new Dictionary<int, double?>(pensById.Count);

		foreach (var state in pensById.Values)
		{
			if (!state.IsVisible)
			{
				continue;
			}

			var (hasEnvelope, value) = ReadVisiblePenAt(state, cursorTime);
			if (hasEnvelope || value is not null)
			{
				readouts[state.Pen.PenId] = value;
			}
		}

		return readouts;
	}

	/// <summary>
	/// One visible pen's value at <paramref name="time"/>; null for a hidden pen, a gap, or a moment outside the
	/// envelope and the held span.
	/// </summary>
	public double? ReadPenAt(int penId, DateTime time)
	{
		return pensById.TryGetValue(penId, out var state) && state.IsVisible
			? ReadVisiblePenAt(state, time).Value
			: null;
	}

	private (bool HasEnvelope, double? Value) ReadVisiblePenAt(TrendPenState state, DateTime time)
	{
		if (envelopesById.TryGetValue(state.Pen.PenId, out var envelope))
		{
			return (true, CursorReadoutModel.ReadPenAt(time, envelope, state.Pen.LineStyle) ?? state.HeldValueAt(time));
		}

		return (false, state.HeldValueAt(time));
	}
}
