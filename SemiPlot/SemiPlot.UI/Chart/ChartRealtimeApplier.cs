using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// Applies coalesced realtime batches to the per-pen buffers and advances the navigation live edge.
// At the Raw layer each sample appends a raw point; at coarse layers it folds into the current
// decimation column instead (consistent with the envelope contract).
public sealed class ChartRealtimeApplier(
	IReadOnlyDictionary<long, TrendPenState> pensById,
	ChartNavigationController navigation)
{
	private readonly IReadOnlyDictionary<long, TrendPenState> _pensById = pensById;
	private readonly ChartNavigationController _navigation = navigation;

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

			for (var index = 0; index < batch.Timestamps.Count; index++)
			{
				if (foldIntoColumn)
				{
					state.FoldRealtime(penValues.Values[index]);
				}
				else
				{
					state.AppendRealtime(batch.Timestamps[index], penValues.Values[index]);
				}
			}
		}
	}
}
