using ReactiveUI;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class TrendPenState : ReactiveObject
{
	public TrendPenState(Pen pen, EnvelopeLine line)
	{
		Pen = pen;
		Line = line;
		Line.PenLineStyle = pen.LineStyle;
	}

	public Pen Pen { get; }

	public EnvelopeLine Line { get; }

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
		var columns = new List<EnvelopeColumn>(envelope.Timestamps.Count);

		for (var index = 0; index < envelope.Timestamps.Count; index++)
		{
			columns.Add(new EnvelopeColumn(
				LocalTimeAxis.ToAxis(envelope.Timestamps[index]),
				envelope.Min[index],
				envelope.Max[index],
				envelope.Center[index]));
		}

		Line.ReplaceColumns(columns);
		CurrentValue = LastNonGapCenter(columns);
	}

	public void ClearHistory()
	{
		Line.ClearColumns();
		CurrentValue = null;
	}

	public void AppendRealtime(DateTime timestampUtc, double? value)
	{
		var y = value ?? double.NaN;
		var column = new EnvelopeColumn(LocalTimeAxis.ToAxis(timestampUtc), y, y, y);

		if (Line.AppendColumn(column) && value.HasValue)
		{
			CurrentValue = value;
		}
	}

	// At coarse layers a realtime sample folds into the current (last) decimation column instead of drawing
	// a raw point, widening its Min/Max; a null/empty/gap tail is skipped.
	public void FoldRealtime(double? value)
	{
		if (value.HasValue && Line.FoldIntoLastColumn(value.Value))
		{
			CurrentValue = value;
		}
	}

	private static double? LastNonGapCenter(List<EnvelopeColumn> columns)
	{
		for (var index = columns.Count - 1; index >= 0; index--)
		{
			var center = columns[index].Center;
			if (!double.IsNaN(center))
			{
				return center;
			}
		}

		return null;
	}
}
