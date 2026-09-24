using ScottPlot;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class ChartAxisBinder(Plot plot)
{
	private readonly Dictionary<int, IYAxis> _axesByPenId = [];
	private readonly Plot _plot = plot;

	public IReadOnlyDictionary<int, IYAxis> AxesByPenId => _axesByPenId;

	public IYAxis? FindAxis(int penId)
	{
		return _axesByPenId.GetValueOrDefault(penId);
	}

	// The removed pen's axis stays keyed so re-adding that pen reuses it; hidden meanwhile, because
	// nothing computes a scale for it any more and it would otherwise keep drawing its last bounds.
	public void HideAxis(int penId)
	{
		if (_axesByPenId.TryGetValue(penId, out var axis))
		{
			axis.IsVisible = false;
		}
	}

	public void Apply(
		IReadOnlyList<PenScale> scales,
		IReadOnlyDictionary<int, TrendPenState> pensById)
	{
		foreach (var scale in scales)
		{
			var axis = ResolveAxis(scale.PenId);

			if (pensById.TryGetValue(scale.PenId, out var pen))
			{
				pen.Line.Axes.YAxis = axis;
			}

			_plot.Axes.SetLimitsY(scale.Min, scale.Max, axis);
			axis.IsVisible = scale.IsActive && pen is { IsVisible: true };

			if (axis.IsVisible)
			{
				// ScottPlot draws the horizontal gridlines from Grid.YAxis alone and never reads that axis's
				// own IsVisible, so it keeps the plot's first axis until the drawn one is assigned here.
				_plot.Grid.YAxis = axis;
			}
		}
	}

	private IYAxis ResolveAxis(int penId)
	{
		if (_axesByPenId.TryGetValue(penId, out var existing))
		{
			return existing;
		}

		var axis = CreateAxis();
		_axesByPenId.Add(penId, axis);

		return axis;
	}

	private IYAxis CreateAxis()
	{
		return _axesByPenId.Count == 0 ? _plot.Axes.Left : _plot.Axes.AddLeftAxis();
	}
}
