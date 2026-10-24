using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class ChartCursorReader(
	IReadOnlyDictionary<int, TrendPenState> pensById,
	IReadOnlyDictionary<int, PenHistoryEnvelope> envelopesById)
{
	/// <summary>Every visible pen with an envelope or a held value at <paramref name="cursorTime"/>.</summary>
	public IReadOnlyDictionary<int, double?> ReadAt(DateTime cursorTime)
	{
		var readouts = new Dictionary<int, double?>(pensById.Count);

		foreach (var state in pensById.Values)
		{
			if (!state.IsVisible)
			{
				continue;
			}

			var hasEnvelope = envelopesById.TryGetValue(state.Pen.PenId, out var envelope);
			var value = Read(state, envelope, cursorTime);

			if (hasEnvelope || value is not null)
			{
				readouts[state.Pen.PenId] = value;
			}
		}

		return readouts;
	}

	/// <summary>One visible pen's value at <paramref name="time"/>; null for a hidden pen or a gap.</summary>
	public double? ReadPenAt(int penId, DateTime time)
	{
		if (!pensById.TryGetValue(penId, out var state) || !state.IsVisible)
		{
			return null;
		}

		return Read(state, envelopesById.GetValueOrDefault(penId), time);
	}

	private static double? Read(TrendPenState state, PenHistoryEnvelope? envelope, DateTime time)
	{
		var envelopeValue = envelope is null ? null : CursorReadoutModel.ReadPenAt(time, envelope);

		return envelopeValue ?? state.HeldValueAt(time);
	}
}
