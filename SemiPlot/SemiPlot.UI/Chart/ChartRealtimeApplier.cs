using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class ChartRealtimeApplier(
	IReadOnlyDictionary<int, TrendPenState> pensById,
	ChartNavigationController navigation)
{
	public void Apply(RealtimeBatch batch, bool foldIntoColumn)
	{
		ApplyBatch(batch, foldIntoColumn);

		if (batch.Timestamps.Count > 0)
		{
			navigation.OnLiveEdge(batch.Timestamps[^1]);
		}
	}

	private void ApplyBatch(RealtimeBatch batch, bool foldIntoColumn)
	{
		foreach (var penValues in batch.Pens)
		{
			if (!pensById.TryGetValue(penValues.PenId, out var state))
			{
				continue;
			}

			// The pen's own timestamps, never the batch's union: a timestamp this pen did not report is not its gap.
			for (var index = 0; index < penValues.Values.Count; index++)
			{
				var timestampUtc = penValues.TimestampsUtc[index];
				var value = penValues.Values[index];

				if (value is null)
				{
					state.AppendRealtime(timestampUtc, null);
				}
				else if (foldIntoColumn)
				{
					state.FoldRealtime(timestampUtc, value.Value);
				}
				else
				{
					state.AppendRealtime(timestampUtc, value);
				}
			}
		}
	}
}
