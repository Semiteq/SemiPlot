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

	internal const float DefaultLineWidth = 1f;
	internal const float ActiveLineWidth = 2f;

	private readonly LineStyle _stroke = new();
	private readonly List<EnvelopePoint> _pathPoints = [];

	// The UI thread writes the columns, the history end, the hold end and the style, and reads the columns to pick a
	// pen; the render thread reads them in Render and GetAxisLimits. Every access runs under the lock.
	private readonly List<EnvelopeColumn> _columns = [];
	private readonly Lock _renderStateLock = new();
	// The last X the last ReplaceColumns wrote: every column past it is a live append.
	private double _historyEndX = double.NegativeInfinity;
	// docs/architecture/charting.md#per-pen-plottable-envelopeline
	private double _unreadFromX = double.NaN;
	private double _unreadGapX = double.NaN;
	private double _holdEndX = double.NaN;
	// The break written at the hold end when a visible outage ended; a read that reaches it covered the outage.
	private double _resumeGapX = double.NaN;
	private Color _color = Colors.Black;
	private PenLineStyle _penLineStyle = PenLineStyle.Interpolated;
	private float _lineWidth = DefaultLineWidth;

	// docs/architecture/testing-strategy.md#frames-in-a-realised-view
	internal IReadOnlyList<EnvelopeColumn> SnapshotColumns()
	{
		lock (_renderStateLock)
		{
			return [.. _columns];
		}
	}

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
				if (EnvelopePath.IsGap(column))
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
			_stroke.Width = _lineWidth;
			var (first, lastExclusive) = EnvelopePath.VisibleRange(_columns, Axes.XAxis.Min, Axes.XAxis.Max);
			EnvelopePath.Build(
				_columns, first, lastExclusive, _penLineStyle, _holdEndX, _unreadFromX, _pathPoints);
		}

		if (_pathPoints.Count < 2)
		{
			return;
		}

		var logFloor = LogFloorOf(rp.DataRect);

		using var path = new SKPath();
		foreach (var point in _pathPoints)
		{
			var pixel = ProjectVertex(point, rp.DataRect, logFloor);
			if (point.StartsSegment)
			{
				path.MoveTo(pixel.X, pixel.Y);
			}
			else
			{
				path.LineTo(pixel.X, pixel.Y);
			}
		}

		Drawing.DrawLines(rp.Canvas, rp.Paint, path, _stroke);
	}

	// docs/architecture/charting.md#per-pen-plottable-envelopeline
	internal double DistanceTo(Pixel click, PixelRect dataRect)
	{
		if (!dataRect.Contains(click))
		{
			return double.PositiveInfinity;
		}

		List<EnvelopePoint> vertices = [];
		lock (_renderStateLock)
		{
			var (first, lastExclusive) = EnvelopePath.VisibleRange(_columns, Axes.XAxis.Min, Axes.XAxis.Max);
			EnvelopePath.Build(_columns, first, lastExclusive, _penLineStyle, _holdEndX, _unreadFromX, vertices);
		}

		var logFloor = LogFloorOf(dataRect);
		var nearest = double.PositiveInfinity;
		var previous = default(Pixel);
		foreach (var vertex in vertices)
		{
			var pixel = ProjectVertex(vertex, dataRect, logFloor);
			if (!vertex.StartsSegment)
			{
				nearest = Math.Min(nearest, EnvelopePath.ClippedSegmentDistance(previous, pixel, click, dataRect));
			}

			previous = pixel;
		}

		return nearest;
	}

	// The Y a non-positive value is pinned to on a log10 axis; null on a linear one.
	private double? LogFloorOf(PixelRect dataRect)
	{
		return LogTickGenerator.IsLogarithmic(Axes.YAxis)
			? Axes.YAxis.GetCoordinate(dataRect.Bottom - PinnedFloorPixels, dataRect)
			: null;
	}

	private Pixel ProjectVertex(EnvelopePoint vertex, PixelRect dataRect, double? logFloor)
	{
		return Project(vertex.X, vertex.Y, dataRect, logFloor);
	}

	/// <summary>The pixel a value of this pen at <paramref name="x"/> draws at, on a log10 axis too.</summary>
	internal Pixel PixelOf(double x, double value, PixelRect dataRect)
	{
		return Project(x, value, dataRect, LogFloorOf(dataRect));
	}

	private Pixel Project(double x, double value, PixelRect dataRect, double? logFloor)
	{
		var y = logFloor is { } floor ? LogAxis.Project(value, floor) : value;

		return new Pixel(Axes.XAxis.GetPixel(x, dataRect), Axes.YAxis.GetPixel(y, dataRect));
	}

	public void Restyle(Color color, PenLineStyle lineStyle)
	{
		lock (_renderStateLock)
		{
			_color = color;
			_penLineStyle = lineStyle;
		}
	}

	internal void SetLineWidth(float width)
	{
		lock (_renderStateLock)
		{
			_lineWidth = width;
		}
	}

	// docs/architecture/data-integration.md#what-one-history-query-covers
	internal void ReplaceColumns(IReadOnlyList<EnvelopeColumn> columns, double requestedEndX, bool isUnreadPastEnd)
	{
		lock (_renderStateLock)
		{
			RemoveCoveredGap(ref _unreadGapX, requestedEndX);
			RemoveCoveredGap(ref _resumeGapX, requestedEndX);

			var newHistoryEndX = columns.Count > 0 ? columns[^1].X : double.NegativeInfinity;
			var keptTailStart = EnvelopePath.FirstAfter(_columns, Math.Max(_historyEndX, newHistoryEndX));
			var keptTailEnd = Math.Max(keptTailStart, EnvelopePath.FirstAfter(_columns, requestedEndX));

			_columns.RemoveRange(keptTailEnd, _columns.Count - keptTailEnd);
			_columns.RemoveRange(0, keptTailStart);
			_columns.InsertRange(0, columns);
			_historyEndX = newHistoryEndX;
			_unreadFromX = isUnreadPastEnd ? requestedEndX : double.NaN;

			var overflow = _columns.Count - MaxColumns;
			if (overflow > 0)
			{
				_columns.RemoveRange(0, overflow);
			}
		}
	}

	// A column at or before the last drawn X would render a segment running backwards; rejected instead.
	internal bool AppendColumn(EnvelopeColumn column)
	{
		lock (_renderStateLock)
		{
			return AppendAfterLastColumn(column);
		}
	}

	// docs/architecture/charting.md#per-pen-plottable-envelopeline
	internal bool FoldIntoLastColumn(EnvelopeColumn sample, double columnWidthX)
	{
		lock (_renderStateLock)
		{
			if (_columns.Count == 0)
			{
				return false;
			}

			var index = _columns.Count - 1;
			var column = _columns[index];
			var liesPastTheColumn = sample.X - column.X >= columnWidthX;
			if (EnvelopePath.IsGap(column) || CrossesUnreadSpan(sample.X) || liesPastTheColumn)
			{
				return AppendAfterLastColumn(sample);
			}

			_columns[index] = column with
			{
				Min = Math.Min(column.Min, sample.Min),
				Max = Math.Max(column.Max, sample.Max),
				Center = sample.Center
			};

			return true;
		}
	}

	/// <summary>Moves the X the last column is held to; an end before the current one is ignored.</summary>
	internal void SetHoldEnd(double holdEndX)
	{
		lock (_renderStateLock)
		{
			if (double.IsNaN(_holdEndX) || holdEndX > _holdEndX)
			{
				_holdEndX = holdEndX;
			}
		}
	}

	/// <summary>Ends the line with a gap at its drawn hold end, or right after a last value past it.</summary>
	internal void BreakAtHoldEnd()
	{
		lock (_renderStateLock)
		{
			var breakX = EnvelopePath.BreakAfterHold(_columns, _holdEndX, _unreadFromX);
			if (!double.IsNaN(breakX)
				&& AppendAfterLastColumn(new EnvelopeColumn(breakX, double.NaN, double.NaN, double.NaN)))
			{
				_resumeGapX = breakX;
			}
		}
	}

	internal HeldSpan? ReadHeldSpan()
	{
		lock (_renderStateLock)
		{
			return EnvelopePath.Hold(_columns, _holdEndX, _unreadFromX);
		}
	}

	internal double? LastNonGapCenter()
	{
		lock (_renderStateLock)
		{
			for (var index = _columns.Count - 1; index >= 0; index--)
			{
				var center = _columns[index].Center;
				if (!double.IsNaN(center))
				{
					return center;
				}
			}

			return null;
		}
	}

	private bool AppendAfterLastColumn(EnvelopeColumn column)
	{
		if (IsAheadOfTheResumeGap(column))
		{
			_columns.Insert(_columns.Count - 1, column);

			return true;
		}

		if (_columns.Count > 0 && column.X <= _columns[^1].X)
		{
			return false;
		}

		if (CrossesUnreadSpan(column.X))
		{
			var lastX = _columns[^1].X;
			_unreadGapX = _unreadFromX > lastX ? _unreadFromX : lastX + ((column.X - lastX) / 2);
			_columns.Add(new EnvelopeColumn(_unreadGapX, double.NaN, double.NaN, double.NaN));
		}

		if (column.X > _unreadFromX)
		{
			_unreadFromX = double.NaN;
		}

		_columns.Add(column);

		if (_columns.Count > MaxColumns)
		{
			_columns.RemoveRange(0, TrimChunk);
		}

		return true;
	}

	private bool CrossesUnreadSpan(double x)
	{
		return !double.IsNaN(_unreadFromX)
			&& _columns.Count > 0
			&& !EnvelopePath.IsGap(_columns[^1])
			&& _columns[^1].X <= _unreadFromX
			&& x > _unreadFromX;
	}

	// A value the archive committed before the outage arrives with the first answer after it, behind the break.
	private bool IsAheadOfTheResumeGap(EnvelopeColumn column)
	{
		return _columns.Count > 1
			&& _columns[^1].X == _resumeGapX
			&& EnvelopePath.IsGap(_columns[^1])
			&& !EnvelopePath.IsGap(column)
			&& column.X > _columns[^2].X
			&& column.X < _resumeGapX;
	}

	// A read that reached the gap covered the span it stood for.
	private void RemoveCoveredGap(ref double gapX, double requestedEndX)
	{
		if (double.IsNaN(gapX) || gapX > requestedEndX)
		{
			return;
		}

		var index = EnvelopePath.FirstAfter(_columns, gapX) - 1;
		if (index >= 0 && _columns[index].X == gapX && EnvelopePath.IsGap(_columns[index]))
		{
			_columns.RemoveAt(index);
		}

		gapX = double.NaN;
	}
}
