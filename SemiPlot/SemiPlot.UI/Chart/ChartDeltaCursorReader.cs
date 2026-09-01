using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class ChartDeltaCursorReader(
	IReadOnlyDictionary<int, PenHistoryEnvelope> envelopesById)
{
	private static readonly PenHistoryEnvelope _emptyEnvelope = new(0, [], [], [], []);

	private readonly DeltaCursorModel _deltaCursor = new();

	public bool IsEnabled { get; private set; }

	public DateTime? FirstCursor => _deltaCursor.FirstCursor;

	public DateTime? SecondCursor => _deltaCursor.SecondCursor;

	public void SetEnabled(bool isEnabled)
	{
		IsEnabled = isEnabled;
		_deltaCursor.Clear();
	}

	public void Place(DateTime cursorTime)
	{
		_deltaCursor.Place(cursorTime);
	}

	public DeltaReadout? Measure(int activePenId)
	{
		var envelope = envelopesById.GetValueOrDefault(activePenId)
					   ?? _emptyEnvelope with { PenId = activePenId };

		return _deltaCursor.Compute(envelope);
	}

	public static string FormatReadout(DeltaReadout? readout)
	{
		if (readout is null)
		{
			return string.Empty;
		}

		var deltaY = readout.DeltaY is { } value ? value.ToString("0.###") : "—";

		return $"Δt {FormatDeltaTime(readout.DeltaTime)}   Δy {deltaY}";
	}

	private static string FormatDeltaTime(TimeSpan deltaTime)
	{
		if (deltaTime.TotalHours >= 1.0)
		{
			return $"{(int)deltaTime.TotalHours}h {deltaTime.Minutes}m {deltaTime.Seconds}s";
		}

		if (deltaTime.TotalMinutes >= 1.0)
		{
			return $"{deltaTime.Minutes}m {deltaTime.Seconds}s";
		}

		return $"{deltaTime.TotalSeconds:0.###}s";
	}
}
