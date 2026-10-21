using ReactiveUI;

using ScottPlot;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class TrendPenState : ReactiveObject
{
	private bool _isJoinedToLive;

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

	/// <summary>
	/// <paramref name="requestedToUtc"/> is the read's own right edge: no live column past it is kept.
	/// </summary>
	public void LoadHistory(PenHistoryEnvelope envelope, DateTime requestedToUtc)
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

		ReplaceHistory(columns, requestedToUtc);
	}

	public void ClearHistory(DateTime requestedToUtc)
	{
		ReplaceHistory([], requestedToUtc);
	}

	// docs/architecture/data-integration.md#the-break-ahead-of-a-first-live-sample
	public void AppendRealtime(DateTime timestampUtc, double? value)
	{
		if (value is not { } sampled)
		{
			Line.AppendColumn(LiveColumn(timestampUtc, double.NaN));
			_isJoinedToLive = false;
			return;
		}

		var sample = LiveColumn(timestampUtc, sampled);
		TakeLiveValue(sampled, _isJoinedToLive
			? Line.AppendColumn(sample)
			: Line.AppendAfterBreak(AheadBreak(timestampUtc), sample));
	}

	// docs/architecture/charting.md#per-pen-plottable-envelopeline
	public void FoldRealtime(DateTime timestampUtc, double value)
	{
		var sample = LiveColumn(timestampUtc, value);
		var isAppendedAfterBreak = !_isJoinedToLive && Line.AppendAfterBreak(AheadBreak(timestampUtc), sample);

		if (isAppendedAfterBreak)
		{
			TakeLiveValue(value, isTaken: true);

			return;
		}

		TakeLiveValue(value, Line.FoldIntoLastColumn(sample));
	}

	private void TakeLiveValue(double value, bool isTaken)
	{
		_isJoinedToLive = true;
		if (isTaken)
		{
			CurrentValue = value;
		}
	}

	private static EnvelopeColumn AheadBreak(DateTime timestampUtc)
	{
		return LiveColumn(timestampUtc - Sample.BreakStep, double.NaN);
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
	private void ReplaceHistory(IReadOnlyList<EnvelopeColumn> columns, DateTime requestedToUtc)
	{
		if (Line.ReplaceColumns(columns, LocalTimeAxis.ToAxis(requestedToUtc)))
		{
			_isJoinedToLive = false;
		}

		CurrentValue = Line.LastNonGapCenter();
	}
}
