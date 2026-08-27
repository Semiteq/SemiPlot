using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class ChartRealtimeApplier(
	IReadOnlyDictionary<long, TrendPenState> pensById,
	ChartNavigationController navigation)
{
	private readonly ChartNavigationController _navigation = navigation;
	private readonly IReadOnlyDictionary<long, TrendPenState> _pensById = pensById;

	public void Apply(RealtimeBatch batch, bool foldIntoColumn)
	{
		ApplyBatch(batch, foldIntoColumn);

		if (batch.Timestamps.Count > 0)
		{
			_navigation.OnLiveEdge(batch.Timestamps[^1]);
		}
	}

	private void ApplyBatch(RealtimeBatch batch, bool foldIntoColumn)
	{
		foreach (var penValues in batch.Pens)
		{
			if (!_pensById.TryGetValue(penValues.PenId, out var state))
			{
				continue;
			}

			// The pen's own timestamps, never the batch's union: a timestamp this pen did not sample is
			// not its gap, and appending it here would draw a break the archive never recorded.
			for (var index = 0; index < penValues.Values.Count; index++)
			{
				if (foldIntoColumn)
				{
					state.FoldRealtime(penValues.Values[index]);
				}
				else
				{
					state.AppendRealtime(penValues.TimestampsUtc[index], penValues.Values[index]);
				}
			}
		}
	}
}
