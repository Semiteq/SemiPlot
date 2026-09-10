using ScottPlot;

using SemiPlot.Core.Trends;

using SkiaSharp;

namespace SemiPlot.UI.Chart;

// One pen drawn as a single stroked polyline through every visible column's Min and Max: no fill, no band,
// no markers, and only the columns inside the X range are walked.
// docs/architecture/charting.md#per-pen-plottable-envelopeline
public sealed class EnvelopeLine : IPlottable
{
	private const int MaxColumns = 100_000;

	private readonly LineStyle _lineStyle = new() { Width = 1f };
	private readonly List<EnvelopePoint> _pathPoints = [];

	// Written on the UI thread only, through TrendPenState; read on the render thread in Render and
	// GetAxisLimits. Every access from either thread runs under _columnsLock.
	private readonly List<EnvelopeColumn> _columns = [];
	private readonly Lock _columnsLock = new();

	internal IReadOnlyList<EnvelopeColumn> Columns => _columns;

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
		lock (_columnsLock)
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
	}

	public void Render(RenderPack rp)
	{
		lock (_columnsLock)
		{
			var (first, lastExclusive) = EnvelopePath.VisibleRange(_columns, Axes.XAxis.Min, Axes.XAxis.Max);
			EnvelopePath.Build(_columns, first, lastExclusive, PenLineStyle, _pathPoints);
		}

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

	internal void ReplaceColumns(IReadOnlyList<EnvelopeColumn> columns)
	{
		lock (_columnsLock)
		{
			_columns.Clear();
			_columns.AddRange(columns);
		}
	}

	internal void ClearColumns()
	{
		lock (_columnsLock)
		{
			_columns.Clear();
		}
	}

	// A column at or before the last drawn X would render a segment running backwards; rejected instead.
	internal bool AppendColumn(EnvelopeColumn column)
	{
		lock (_columnsLock)
		{
			if (_columns.Count > 0 && column.X <= _columns[^1].X)
			{
				return false;
			}

			_columns.Add(column);

			var overflow = _columns.Count - MaxColumns;
			if (overflow > 0)
			{
				_columns.RemoveRange(0, overflow);
			}

			return true;
		}
	}

	internal bool FoldIntoLastColumn(double value)
	{
		lock (_columnsLock)
		{
			if (_columns.Count == 0)
			{
				return false;
			}

			var index = _columns.Count - 1;
			var column = _columns[index];
			if (double.IsNaN(column.Min) || double.IsNaN(column.Max))
			{
				return false;
			}

			_columns[index] = column with
			{
				Min = Math.Min(column.Min, value),
				Max = Math.Max(column.Max, value),
				Center = value
			};

			return true;
		}
	}
}
