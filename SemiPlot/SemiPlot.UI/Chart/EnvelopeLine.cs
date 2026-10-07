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
	private const int TrimChunk = MaxColumns / 10;
	// docs/architecture/charting.md#log10-y-axis
	private const float PinnedFloorPixels = 2f;

	private readonly LineStyle _stroke = new() { Width = 1f };
	private readonly List<EnvelopePoint> _pathPoints = [];

	// The columns and the style are written on the UI thread only, through TrendPenState; read on the render
	// thread in Render and GetAxisLimits. Every access from either thread runs under _renderStateLock.
	private readonly List<EnvelopeColumn> _columns = [];
	private readonly Lock _renderStateLock = new();
	private Color _color = Colors.Black;
	private PenLineStyle _penLineStyle = PenLineStyle.Interpolated;

	internal IReadOnlyList<EnvelopeColumn> Columns => _columns;

	public bool IsVisible { get; set; } = true;

	public IAxes Axes { get; set; } = new Axes();

	public IEnumerable<LegendItem> LegendItems => [];

	// docs/architecture/charting.md#log10-y-axis
	public AxisLimits GetAxisLimits()
	{
		var isLogarithmic = LogTickGenerator.IsLogarithmic(Axes.YAxis);

		lock (_renderStateLock)
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

			return isLogarithmic || double.IsInfinity(bottom)
				? AxisLimits.HorizontalOnly(left, right)
				: new AxisLimits(left, right, bottom, top);
		}
	}

	public void Render(RenderPack rp)
	{
		lock (_renderStateLock)
		{
			_stroke.Color = _color;
			var (first, lastExclusive) = EnvelopePath.VisibleRange(_columns, Axes.XAxis.Min, Axes.XAxis.Max);
			EnvelopePath.Build(_columns, first, lastExclusive, _penLineStyle, _pathPoints);
		}

		if (_pathPoints.Count < 2)
		{
			return;
		}

		var yAxis = Axes.YAxis;
		var isLogarithmic = LogTickGenerator.IsLogarithmic(yAxis);
		var floor = yAxis.GetCoordinate(rp.DataRect.Bottom - PinnedFloorPixels, rp.DataRect);

		using var path = new SKPath();
		foreach (var point in _pathPoints)
		{
			var pixelX = Axes.XAxis.GetPixel(point.X, rp.DataRect);
			var pixelY = yAxis.GetPixel(isLogarithmic ? LogAxis.Project(point.Y, floor) : point.Y, rp.DataRect);
			if (point.StartsSegment)
			{
				path.MoveTo(pixelX, pixelY);
			}
			else
			{
				path.LineTo(pixelX, pixelY);
			}
		}

		Drawing.DrawLines(rp.Canvas, rp.Paint, path, _stroke);
	}

	public void Restyle(Color color, PenLineStyle lineStyle)
	{
		lock (_renderStateLock)
		{
			_color = color;
			_penLineStyle = lineStyle;
		}
	}

	internal void ReplaceColumns(IReadOnlyList<EnvelopeColumn> columns)
	{
		lock (_renderStateLock)
		{
			_columns.Clear();
			_columns.AddRange(columns);
		}
	}

	internal void ClearColumns()
	{
		lock (_renderStateLock)
		{
			_columns.Clear();
		}
	}

	// A column at or before the last drawn X would render a segment running backwards; rejected instead.
	internal bool AppendColumn(EnvelopeColumn column)
	{
		lock (_renderStateLock)
		{
			if (_columns.Count > 0 && column.X <= _columns[^1].X)
			{
				return false;
			}

			_columns.Add(column);

			if (_columns.Count > MaxColumns)
			{
				_columns.RemoveRange(0, TrimChunk);
			}

			return true;
		}
	}

	internal bool FoldIntoLastColumn(double value)
	{
		lock (_renderStateLock)
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
