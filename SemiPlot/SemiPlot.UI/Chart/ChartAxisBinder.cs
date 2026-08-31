using ScottPlot;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// Each axis key owns exactly one IYAxis (the first key reuses the plot's built-in left axis); every pen
// sharing that key is assigned the same IYAxis. The bottom (time) axis is shared by all plottables and
// is never replaced here, preserving the shared-X invariant.
public sealed class ChartAxisBinder
{
	private readonly Dictionary<string, IYAxis> _axesByKey = [];
	private readonly Plot _plot;

	public ChartAxisBinder(Plot plot)
	{
		ArgumentNullException.ThrowIfNull(plot);
		_plot = plot;
	}

	public IReadOnlyDictionary<string, IYAxis> AxesByKey => _axesByKey;

	public IYAxis? FindAxis(string axisKey)
	{
		ArgumentNullException.ThrowIfNull(axisKey);

		return _axesByKey.GetValueOrDefault(axisKey);
	}

	public void Apply(
		IReadOnlyList<PenScale> scales,
		IReadOnlyDictionary<int, TrendPenState> pensById)
	{
		ArgumentNullException.ThrowIfNull(scales);
		ArgumentNullException.ThrowIfNull(pensById);

		foreach (var scale in scales)
		{
			var axis = ResolveAxis(scale.AxisKey);
			AssignPensToAxis(scale, pensById, axis);
			_plot.Axes.SetLimitsY(scale.Min, scale.Max, axis);
			axis.IsVisible = scale.IsVisible && scale.IsActive;
		}
	}

	private IYAxis ResolveAxis(string axisKey)
	{
		if (_axesByKey.TryGetValue(axisKey, out var existing))
		{
			return existing;
		}

		var axis = CreateAxis();
		_axesByKey.Add(axisKey, axis);

		return axis;
	}

	// The first axis reuses the plot's built-in left axis; subsequent axes alternate left/right.
	private IYAxis CreateAxis()
	{
		if (_axesByKey.Count == 0)
		{
			return _plot.Axes.Left;
		}

		return _axesByKey.Count % 2 == 1 ? _plot.Axes.AddRightAxis() : _plot.Axes.AddLeftAxis();
	}

	private static void AssignPensToAxis(
		PenScale scale,
		IReadOnlyDictionary<int, TrendPenState> pensById,
		IYAxis axis)
	{
		foreach (var penId in scale.PenIds)
		{
			if (!pensById.TryGetValue(penId, out var state))
			{
				continue;
			}

			state.CenterLine.Axes.YAxis = axis;
			state.Band.Axes.YAxis = axis;
		}
	}
}
