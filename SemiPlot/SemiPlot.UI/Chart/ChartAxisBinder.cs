using ScottPlot;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class ChartAxisBinder(Plot plot)
{
	private const float LogMinorGridWidth = 1f;

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
			SetDrawn(axis, false);
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

			var mask = pen?.Pen.Format;

			if (LogTickGenerator.IsLogarithmic(axis) == scale.IsLogarithmic)
			{
				if (axis.TickGenerator is LogTickGenerator logTicks)
				{
					logTicks.Mask = mask;
				}

				SetLimits(axis, scale);
			}
			else
			{
				// One lock for the whole switch, docs/architecture/charting.md#log10-y-axis
				lock (_plot.Sync)
				{
					axis.TickGenerator = scale.IsLogarithmic
						? new LogTickGenerator { Mask = mask, IsDrawn = axis.IsVisible }
						: new LinearTickGenerator { IsDrawn = axis.IsVisible };
					SetLimits(axis, scale);
					RefreshMinorGridWidth();
				}
			}

			var isDrawn = scale.IsActive && pen is { IsVisible: true };
			SetDrawn(axis, isDrawn);

			if (isDrawn)
			{
				DrawGridFrom(axis);
			}
		}
	}

	// docs/architecture/charting.md#ticks-for-the-drawn-axis-only
	private void SetDrawn(IYAxis axis, bool isDrawn)
	{
		var generator = (IDrawnTickGenerator)axis.TickGenerator;

		if (axis.IsVisible == isDrawn && generator.IsDrawn == isDrawn)
		{
			return;
		}

		lock (_plot.Sync)
		{
			generator.IsDrawn = isDrawn;
			axis.IsVisible = isDrawn;
		}
	}

	private void SetLimits(IYAxis axis, PenScale scale)
	{
		if (scale.IsLogarithmic)
		{
			_plot.Axes.SetLimitsY(Math.Log10(scale.Min), Math.Log10(scale.Max), axis);
		}
		else
		{
			_plot.Axes.SetLimitsY(scale.Min, scale.Max, axis);
		}
	}

	// ScottPlot draws the horizontal gridlines from Grid.YAxis alone and never reads that axis's own IsVisible,
	// so it keeps the plot's first axis until the drawn one is assigned here.
	private void DrawGridFrom(IYAxis axis)
	{
		if (ReferenceEquals(_plot.Grid.YAxis, axis))
		{
			RefreshMinorGridWidth();

			return;
		}

		lock (_plot.Sync)
		{
			_plot.Grid.YAxis = axis;
			RefreshMinorGridWidth();
		}
	}

	private void RefreshMinorGridWidth()
	{
		var minorWidth = LogTickGenerator.IsLogarithmic(_plot.Grid.YAxis) ? LogMinorGridWidth : 0f;
		var minorLine = _plot.Grid.YAxisStyle.MinorLineStyle;

		if (minorLine.Width == minorWidth)
		{
			return;
		}

		lock (_plot.Sync)
		{
			minorLine.Width = minorWidth;
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
		var axis = _axesByPenId.Count == 0 ? _plot.Axes.Left : _plot.Axes.AddLeftAxis();

		lock (_plot.Sync)
		{
			axis.TickGenerator = new LinearTickGenerator { IsDrawn = axis.IsVisible };
		}

		return axis;
	}
}
