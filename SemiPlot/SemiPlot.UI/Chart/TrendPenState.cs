using ReactiveUI;

using ScottPlot;
using ScottPlot.Plottables;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class TrendPenState : ReactiveObject
{
	private const int MaxRealtimePoints = 100_000;
	private readonly List<(double X, double Top, double Bottom)> _bandPoints = [];

	private readonly List<Coordinates> _centerPoints;

	// centerPoints MUST be the exact instance the center-line Scatter was built against: ScottPlot's
	// Scatter holds a live reference to it and re-reads it on every render.
	public TrendPenState(Pen pen, Scatter centerLine, FillY band, List<Coordinates> centerPoints)
	{
		Pen = pen;
		CenterLine = centerLine;
		Band = band;
		_centerPoints = centerPoints;
		CenterLine.ConnectStyle = pen.LineStyle == PenLineStyle.Stepped ? ConnectStyle.StepHorizontal : ConnectStyle.Straight;
	}

	public Pen Pen { get; }

	public Scatter CenterLine { get; }

	public FillY Band { get; }

	public IReadOnlyList<Coordinates> CenterPoints => _centerPoints;

	public IReadOnlyList<(double X, double Top, double Bottom)> BandPoints => _bandPoints;

	public bool IsVisible
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			CenterLine.IsVisible = value;
			ApplyBandVisibility();
		}
	} = true;

	public double? CurrentValue
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public void LoadHistory(PenHistoryEnvelope envelope)
	{
		_centerPoints.Clear();
		_bandPoints.Clear();

		for (var index = 0; index < envelope.Timestamps.Count; index++)
		{
			var x = LocalTimeAxis.ToAxis(envelope.Timestamps[index]);
			_centerPoints.Add(new Coordinates(x, envelope.Center[index]));
			_bandPoints.Add((x, envelope.Max[index], envelope.Min[index]));
		}

		Band.SetDataSource(_bandPoints);
		ApplyBandVisibility();
		CurrentValue = LastNonGapCenter();
	}

	public void ClearHistory()
	{
		_centerPoints.Clear();
		_bandPoints.Clear();
		Band.SetDataSource(_bandPoints);
		ApplyBandVisibility();
		CurrentValue = null;
	}

	// A point at or before the last drawn X would render a segment running backwards; dropped.
	public void AppendRealtime(DateTime timestampUtc, double? value)
	{
		var x = LocalTimeAxis.ToAxis(timestampUtc);
		if (_centerPoints.Count > 0 && x <= _centerPoints[^1].X)
		{
			return;
		}

		var y = value ?? double.NaN;

		_centerPoints.Add(new Coordinates(x, y));
		_bandPoints.Add((x, y, y));
		TrimToCap();
		Band.SetDataSource(_bandPoints);
		ApplyBandVisibility();

		if (value.HasValue)
		{
			CurrentValue = value;
		}
	}

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

	// At coarse layers a realtime sample folds into the current (last) decimation column instead of drawing
	// a raw point, widening its Min/Max band; a null/empty/gap tail is skipped.
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
		ApplyBandVisibility();

		CurrentValue = value;
	}

	private void ApplyBandVisibility()
	{
		Band.IsVisible = IsVisible && !BandDegeneracy.IsDegenerate(_bandPoints);
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
