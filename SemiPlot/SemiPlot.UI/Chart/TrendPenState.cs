using ReactiveUI;

using ScottPlot;
using ScottPlot.Plottables;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// Owns one pen's two plottables (the center Scatter line and the Min/Max FillY band) and the
// backing point buffers, keeping all current-value/visibility bookkeeping off the AvaPlot control
// so the chart view model stays unit-testable headless. History loads reset the buffers; realtime
// samples append a single point per pen with the band degenerate (Min == Max) at the live edge.
public sealed class TrendPenState : ReactiveObject
{
	// Bounds the realtime tail so the buffers do not grow without limit when the chart is detached from
	// the live edge (not sticky) and never re-queries. Far more than any visible window of decimated
	// columns, so trimming never drops on-screen data; the oldest points fall off the back of the tail.
	private const int MaxRealtimePoints = 100_000;

	private readonly List<Coordinates> _centerPoints;
	private readonly List<(double X, double Top, double Bottom)> _bandPoints = [];

	private bool _isVisible = true;
	private double? _currentValue;

	// The centerPoints list MUST be the exact instance the center-line Scatter was built against
	// (ScottPlot's Scatter holds a live reference to it and re-reads it on every render). Mutating any
	// other list would leave the center line empty while only the band updated.
	public TrendPenState(Pen pen, Scatter centerLine, FillY band, List<Coordinates> centerPoints)
	{
		ArgumentNullException.ThrowIfNull(pen);
		ArgumentNullException.ThrowIfNull(centerLine);
		ArgumentNullException.ThrowIfNull(band);
		ArgumentNullException.ThrowIfNull(centerPoints);

		Pen = pen;
		CenterLine = centerLine;
		Band = band;
		_centerPoints = centerPoints;
		CenterLine.ConnectStyle = PenLineStyleMap.ToConnectStyle(pen.LineStyle);
	}

	public Pen Pen { get; }

	public Scatter CenterLine { get; }

	public FillY Band { get; }

	public IReadOnlyList<Coordinates> CenterPoints => _centerPoints;

	public IReadOnlyList<(double X, double Top, double Bottom)> BandPoints => _bandPoints;

	public bool IsVisible
	{
		get => _isVisible;
		set
		{
			this.RaiseAndSetIfChanged(ref _isVisible, value);
			CenterLine.IsVisible = value;
			Band.IsVisible = value;
		}
	}

	public double? CurrentValue
	{
		get => _currentValue;
		private set => this.RaiseAndSetIfChanged(ref _currentValue, value);
	}

	public void LoadHistory(PenHistoryEnvelope envelope)
	{
		ArgumentNullException.ThrowIfNull(envelope);

		_centerPoints.Clear();
		_bandPoints.Clear();

		for (var index = 0; index < envelope.Timestamps.Count; index++)
		{
			var x = LocalTimeAxis.ToAxis(envelope.Timestamps[index]);
			_centerPoints.Add(new Coordinates(x, envelope.Center[index]));
			_bandPoints.Add((x, envelope.Max[index], envelope.Min[index]));
		}

		Band.SetDataSource(_bandPoints);
		CurrentValue = LastNonGapCenter();
	}

	// Appends the realtime tail: the center line grows by one point and the band degenerates to
	// Min == Max == value at the live edge (a null sample is a gap, drawn as NaN).
	public void AppendRealtime(DateTime timestampUtc, double? value)
	{
		var x = LocalTimeAxis.ToAxis(timestampUtc);
		var y = value ?? double.NaN;

		_centerPoints.Add(new Coordinates(x, y));
		_bandPoints.Add((x, y, y));
		TrimToCap();
		Band.SetDataSource(_bandPoints);

		if (value.HasValue)
		{
			CurrentValue = value;
		}
	}

	// Drops the oldest realtime points once the tail exceeds the cap, keeping the buffers bounded.
	private void TrimToCap()
	{
		var overflow = _centerPoints.Count - MaxRealtimePoints;
		if (overflow <= 0)
		{
			return;
		}

		_centerPoints.RemoveRange(0, overflow);
		_bandPoints.RemoveRange(0, overflow);
	}

	// At coarse aggregation layers (minute/hour/day) a realtime sample does not draw a raw point;
	// it folds into the current (last) decimation column, widening that column's Min/Max band and
	// moving its center, consistent with the decimation envelope contract. A null sample, an empty
	// buffer, or a gap-valued tail is skipped (the next coarse-layer history re-query supplies the
	// proper column).
	public void FoldRealtime(double? value)
	{
		if (!value.HasValue || _bandPoints.Count == 0)
		{
			return;
		}

		var index = _bandPoints.Count - 1;
		var (x, top, bottom) = _bandPoints[index];
		if (double.IsNaN(top) || double.IsNaN(bottom))
		{
			return;
		}

		var foldedTop = Math.Max(top, value.Value);
		var foldedBottom = Math.Min(bottom, value.Value);
		_bandPoints[index] = (x, foldedTop, foldedBottom);
		_centerPoints[index] = new Coordinates(x, value.Value);
		Band.SetDataSource(_bandPoints);

		CurrentValue = value;
	}

	private double? LastNonGapCenter()
	{
		for (var index = _centerPoints.Count - 1; index >= 0; index--)
		{
			var y = _centerPoints[index].Y;
			if (!double.IsNaN(y))
			{
				return y;
			}
		}

		return null;
	}
}
