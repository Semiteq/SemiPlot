using ScottPlot;

using SemiPlot.Core.Trends;

using SkiaSharp;

namespace SemiPlot.UI.Chart;

/// <summary>One decimation column: its Min/Max extent and Center at one axis X.</summary>
public readonly record struct EnvelopeColumn(double X, double Min, double Max, double Center);

// One pen drawn as a single stroked polyline through every visible column's Min and Max: no fill, no band,
// no markers, and only the columns inside the X range are walked.
// docs/architecture/charting.md#per-pen-plottable-envelopeline
public sealed class EnvelopeLine(IReadOnlyList<EnvelopeColumn> columns) : IPlottable
{
	private readonly IReadOnlyList<EnvelopeColumn> _columns = columns;
	private readonly LineStyle _lineStyle = new() { Width = 1f };
	private readonly List<EnvelopePoint> _pathPoints = [];

	public bool IsVisible { get; set; } = true;

	public IAxes Axes { get; set; } = new Axes();

	public Color Color
	{
		get => _lineStyle.Color;
		set => _lineStyle.Color = value;
	}

	public float LineWidth
	{
		get => _lineStyle.Width;
		set => _lineStyle.Width = value;
	}

	public PenLineStyle PenLineStyle { get; set; } = PenLineStyle.Interpolated;

	public IEnumerable<LegendItem> LegendItems => [];

	public AxisLimits GetAxisLimits()
	{
		if (_columns.Count == 0)
		{
			return AxisLimits.NoLimits;
		}

		var bottom = double.PositiveInfinity;
		var top = double.NegativeInfinity;

		foreach (var column in _columns)
		{
			if (double.IsNaN(column.Min) || double.IsNaN(column.Max))
			{
				continue;
			}

			bottom = Math.Min(bottom, column.Min);
			top = Math.Max(top, column.Max);
		}

		var left = _columns[0].X;
		var right = _columns[^1].X;

		return double.IsInfinity(bottom)
			? AxisLimits.HorizontalOnly(left, right)
			: new AxisLimits(left, right, bottom, top);
	}

	public void Render(RenderPack rp)
	{
		var (first, lastExclusive) = EnvelopePath.VisibleRange(_columns, Axes.XAxis.Min, Axes.XAxis.Max);
		EnvelopePath.Build(_columns, first, lastExclusive, PenLineStyle, _pathPoints);

		if (_pathPoints.Count < 2)
		{
			return;
		}

		using var path = new SKPath();
		foreach (var point in _pathPoints)
		{
			var pixel = Axes.GetPixel(new Coordinates(point.X, point.Y));
			if (point.StartsSegment)
			{
				path.MoveTo(pixel.X, pixel.Y);
			}
			else
			{
				path.LineTo(pixel.X, pixel.Y);
			}
		}

		Drawing.DrawLines(rp.Canvas, rp.Paint, path, _lineStyle);
	}
}
