using ReactiveUI;

using ScottPlot;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class TrendPenState : ReactiveObject
{
	public TrendPenState(Pen pen, EnvelopeLine line)
	{
		Pen = pen;
		Line = line;
		IsVisible = pen.EnabledOnStart;
		RestyleLine();
	}

	public Pen Pen
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public EnvelopeLine Line { get; }

	public bool IsVisible
	{
		get;
		private set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			Line.IsVisible = value;
		}
	}

	public double? CurrentValue
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>Takes a stored revision of the pen; visibility and the loaded history stay.</summary>
	public void Revise(Pen pen)
	{
		Pen = pen;
		RestyleLine();
	}

	internal void SetVisibility(bool isVisible)
	{
		IsVisible = isVisible;
	}

	/// <summary><paramref name="requestedToUtc"/> is the read's own right edge: no live column past it is kept.</summary>
	public void LoadHistory(PenHistoryEnvelope envelope, DateTime requestedToUtc, DateTime? newestSampleUtc)
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

		ReplaceHistory(columns, requestedToUtc, newestSampleUtc);
	}

	public void ClearHistory(DateTime requestedToUtc, DateTime? newestSampleUtc)
	{
		ReplaceHistory([], requestedToUtc, newestSampleUtc);
	}

	public void AppendRealtime(DateTime timestampUtc, double? value)
	{
		if (Line.AppendColumn(LiveColumn(timestampUtc, value ?? double.NaN)) && value.HasValue)
		{
			CurrentValue = value;
		}
	}

	// docs/architecture/charting.md#per-pen-plottable-envelopeline
	public void FoldRealtime(DateTime timestampUtc, double value, TimeSpan columnWidth)
	{
		if (Line.FoldIntoLastColumn(LiveColumn(timestampUtc, value), columnWidth.TotalDays))
		{
			CurrentValue = value;
		}
	}

	/// <summary>Holds the last column to the moment the archive last answered.</summary>
	public void ApplyHold(DateTime answeredAtUtc)
	{
		Line.SetHoldEnd(LocalTimeAxis.ToAxis(answeredAtUtc));
	}

	/// <summary>Ends the line where its hold ends, after a visible outage.</summary>
	public void BreakHold()
	{
		Line.BreakAtHoldEnd();
	}

	/// <summary>The held value at <paramref name="timeUtc"/>; null outside the held span.</summary>
	public double? HeldValueAt(DateTime timeUtc)
	{
		var x = LocalTimeAxis.ToAxis(timeUtc);

		return Line.ReadHeldSpan() is { } span && x >= span.FromX && x <= span.ToX ? span.Value : null;
	}

	private static EnvelopeColumn LiveColumn(DateTime timestampUtc, double value)
	{
		return new EnvelopeColumn(LocalTimeAxis.ToAxis(timestampUtc), value, value, value);
	}

	private void RestyleLine()
	{
		Line.Restyle(new Color(Pen.Color), Pen.LineStyle);
	}

	// docs/architecture/charting.md#per-pen-plottable-envelopeline
	private void ReplaceHistory(
		IReadOnlyList<EnvelopeColumn> columns,
		DateTime requestedToUtc,
		DateTime? newestSampleUtc)
	{
		var isUnreadPastEnd = newestSampleUtc is { } newest && requestedToUtc < newest;
		Line.ReplaceColumns(columns, LocalTimeAxis.ToAxis(requestedToUtc), isUnreadPastEnd);
		CurrentValue = Line.LastNonGapCenter();
	}
}
