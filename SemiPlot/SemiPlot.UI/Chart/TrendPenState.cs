using ReactiveUI;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class TrendPenState : ReactiveObject
{
	private const int MaxRealtimePoints = 100_000;

	private readonly List<EnvelopeColumn> _columns;

	// columns MUST be the exact instance the EnvelopeLine was built against: the plottable holds a live
	// reference to it and re-reads it on every render.
	public TrendPenState(Pen pen, EnvelopeLine line, List<EnvelopeColumn> columns)
	{
		Pen = pen;
		Line = line;
		_columns = columns;
		Line.PenLineStyle = pen.LineStyle;
	}

	public Pen Pen { get; }

	public EnvelopeLine Line { get; }

	public IReadOnlyList<EnvelopeColumn> Columns => _columns;

	public bool IsVisible
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			Line.IsVisible = value;
		}
	} = true;

	public double? CurrentValue
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public void LoadHistory(PenHistoryEnvelope envelope)
	{
		_columns.Clear();

		for (var index = 0; index < envelope.Timestamps.Count; index++)
		{
			_columns.Add(new EnvelopeColumn(
				LocalTimeAxis.ToAxis(envelope.Timestamps[index]),
				envelope.Min[index],
				envelope.Max[index],
				envelope.Center[index]));
		}

		CurrentValue = LastNonGapCenter();
	}

	public void ClearHistory()
	{
		_columns.Clear();
		CurrentValue = null;
	}

	// A point at or before the last drawn X would render a segment running backwards; dropped.
	public void AppendRealtime(DateTime timestampUtc, double? value)
	{
		var x = LocalTimeAxis.ToAxis(timestampUtc);
		if (_columns.Count > 0 && x <= _columns[^1].X)
		{
			return;
		}

		var y = value ?? double.NaN;

		_columns.Add(new EnvelopeColumn(x, y, y, y));
		TrimToCap();

		if (value.HasValue)
		{
			CurrentValue = value;
		}
	}

	private void TrimToCap()
	{
		var overflow = _columns.Count - MaxRealtimePoints;
		if (overflow <= 0)
		{
			return;
		}

		_columns.RemoveRange(0, overflow);
	}

	// At coarse layers a realtime sample folds into the current (last) decimation column instead of drawing
	// a raw point, widening its Min/Max; a null/empty/gap tail is skipped.
	public void FoldRealtime(double? value)
	{
		if (!value.HasValue || _columns.Count == 0)
		{
			return;
		}

		var index = _columns.Count - 1;
		var column = _columns[index];
		if (double.IsNaN(column.Min) || double.IsNaN(column.Max))
		{
			return;
		}

		_columns[index] = column with
		{
			Min = Math.Min(column.Min, value.Value),
			Max = Math.Max(column.Max, value.Value),
			Center = value.Value
		};

		CurrentValue = value;
	}

	private double? LastNonGapCenter()
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
