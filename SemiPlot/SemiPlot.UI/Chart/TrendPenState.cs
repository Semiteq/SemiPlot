using ReactiveUI;

using ScottPlot;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class TrendPenState : ReactiveObject
{
	private LiveJoin _liveJoin = LiveJoin.Unjoined;
	private double _holdEdgeX = double.NaN;

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

	private bool IsAfterASourceBreak =>
		_liveJoin is LiveJoin.UnjoinedBySourceBreak or LiveJoin.ReconfirmedAfterSourceBreak;

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

	/// <summary>The axis X the line is held to while the live stream confirms it; NaN holds nothing.</summary>
	internal void HoldTo(double edgeX)
	{
		if (_holdEdgeX.Equals(edgeX))
		{
			return;
		}

		_holdEdgeX = edgeX;
		ApplyHold();
	}

	internal double? HeldValueAt(DateTime time)
	{
		if (Line.ReadHeldSpan() is not { } span)
		{
			return null;
		}

		var x = LocalTimeAxis.ToAxis(time);

		return x >= span.FromX && x <= span.ToX ? CurrentValue : null;
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
			SetLiveJoin(LiveJoin.UnjoinedBySourceBreak);
			return;
		}

		var sample = LiveColumn(timestampUtc, sampled);
		TakeLiveValue(sample, _liveJoin == LiveJoin.Joined
			? Line.AppendColumn(sample)
			: Line.AppendAfterBreak(AheadBreak(timestampUtc), sample));
	}

	// docs/architecture/charting.md#per-pen-plottable-envelopeline
	public void FoldRealtime(DateTime timestampUtc, double value)
	{
		var sample = LiveColumn(timestampUtc, value);

		if (_liveJoin == LiveJoin.Joined)
		{
			TakeLiveValue(sample, Line.FoldIntoLastColumn(sample));

			return;
		}

		if (Line.AppendAfterBreak(AheadBreak(timestampUtc), sample))
		{
			TakeLiveValue(sample, isTaken: true);

			return;
		}

		TakeLiveValue(sample, !IsAfterASourceBreak && Line.FoldIntoLastColumn(sample));
	}

	private void TakeLiveValue(EnvelopeColumn sample, bool isTaken)
	{
		Line.ConfirmLive(sample.X);

		if (isTaken)
		{
			CurrentValue = sample.Center;
			SetLiveJoin(LiveJoin.Joined);

			return;
		}

		SetLiveJoin(_liveJoin switch
		{
			LiveJoin.Unjoined => LiveJoin.Joined,
			LiveJoin.UnjoinedBySourceBreak => LiveJoin.ReconfirmedAfterSourceBreak,
			_ => _liveJoin
		});
	}

	private void SetLiveJoin(LiveJoin liveJoin)
	{
		if (_liveJoin == liveJoin)
		{
			return;
		}

		_liveJoin = liveJoin;
		ApplyHold();
	}

	private void ApplyHold()
	{
		var isHeld = _liveJoin is LiveJoin.Joined or LiveJoin.ReconfirmedAfterSourceBreak;
		Line.SetHoldEnd(isHeld ? _holdEdgeX : double.NaN);
	}

	// docs/architecture/charting.md#per-pen-plottable-envelopeline
	private EnvelopeColumn AheadBreak(DateTime timestampUtc)
	{
		var kind = IsAfterASourceBreak ? BreakKind.Ordinary : BreakKind.Ahead;

		return LiveColumn(timestampUtc - Sample.BreakStep, double.NaN) with { Break = kind };
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
			SetLiveJoin(LiveJoin.Unjoined);
		}

		CurrentValue = Line.LastNonGapCenter();
	}

	// docs/architecture/charting.md#per-pen-plottable-envelopeline
	private enum LiveJoin
	{
		Unjoined,
		UnjoinedBySourceBreak,
		ReconfirmedAfterSourceBreak,
		Joined
	}
}
