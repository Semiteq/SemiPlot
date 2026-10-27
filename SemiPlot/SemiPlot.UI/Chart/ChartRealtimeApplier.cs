using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class ChartRealtimeApplier(
	IReadOnlyDictionary<int, TrendPenState> pensById,
	ChartNavigationController navigation)
{
	// docs/architecture/charting.md#per-pen-plottable-envelopeline
	internal static readonly TimeSpan ShortestVisibleOutage = TimeSpan.FromSeconds(5);

	private DateTime? _lastAnsweredAtUtc;

	/// <summary>Applies the batch; true when it ends an outage the chart shows, so the window re-reads.</summary>
	public bool Apply(RealtimeBatch batch, bool foldIntoColumn)
	{
		var endsAVisibleOutage = batch.ResumesAfterFailure
			&& _lastAnsweredAtUtc is { } lastAnsweredAtUtc
			&& batch.AnsweredAtUtc - lastAnsweredAtUtc > ShortestVisibleOutage;
		_lastAnsweredAtUtc = batch.AnsweredAtUtc;

		if (endsAVisibleOutage)
		{
			foreach (var state in pensById.Values)
			{
				state.BreakHold();
			}
		}

		ApplyBatch(batch, foldIntoColumn);

		foreach (var state in pensById.Values)
		{
			state.ApplyHold(batch.AnsweredAtUtc);
		}

		navigation.OnLiveEdge(batch.AnsweredAtUtc, batch.Timestamps.Count > 0 ? batch.Timestamps[^1] : null);

		return endsAVisibleOutage;
	}

	private void ApplyBatch(RealtimeBatch batch, bool foldIntoColumn)
	{
		var columnWidth = navigation.ColumnWidth;

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
					state.FoldRealtime(timestampUtc, value.Value, columnWidth);
				}
				else
				{
					state.AppendRealtime(timestampUtc, value);
				}
			}
		}
	}
}
