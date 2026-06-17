using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// Owns the dual-cursor state and resolves the active pen's envelope so the renderer-agnostic
// DeltaCursorModel can measure Δt/Δy. Kept separate from the chart view model so the cursor view-state
// has one home and the view model stays within its size budget; the delta math itself lives in Core.
public sealed class ChartDeltaCursorReader(
	IReadOnlyDictionary<long, PenHistoryEnvelope> envelopesById)
{
	private static readonly PenHistoryEnvelope _emptyEnvelope = new(0, [], [], [], []);

	private readonly DeltaCursorModel _deltaCursor = new();
	private readonly IReadOnlyDictionary<long, PenHistoryEnvelope> _envelopesById = envelopesById;

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

	public DeltaReadout? Measure(long activePenId)
	{
		var envelope = _envelopesById.GetValueOrDefault(activePenId)
			?? _emptyEnvelope with { PenId = activePenId };

		return _deltaCursor.Compute(envelope);
	}
}
