namespace SemiPlot.Core.Trends;

// Renderer-agnostic dual-cursor model. Two cursors are placed at times t1 and t2; the measurement is
// Δt = |t2 - t1| plus the active pen's center-channel change Δy = value(t2) - value(t1). Because pens
// share X but have independent Y scales, a global Δy is meaningless, so Δy is reported only for the
// active/selected pen passed to Compute. The endpoint values reuse CursorReadoutModel's interpolation
// and gap rules, so Δy is null when either endpoint lies in a gap or outside the pen's range.
public sealed class DeltaCursorModel
{
	private readonly CursorReadoutModel _cursorReadout = new();

	public DateTime? FirstCursor { get; private set; }

	public DateTime? SecondCursor { get; private set; }

	public bool HasBothCursors => FirstCursor.HasValue && SecondCursor.HasValue;

	// Places the next cursor: the first placement sets cursor one, the second sets cursor two, and any
	// further placement starts a fresh measurement from the new point.
	public void Place(DateTime cursorTime)
	{
		if (!FirstCursor.HasValue || SecondCursor.HasValue)
		{
			FirstCursor = cursorTime;
			SecondCursor = null;
			return;
		}

		SecondCursor = cursorTime;
	}

	public void Clear()
	{
		FirstCursor = null;
		SecondCursor = null;
	}

	// Returns the measurement once both cursors are placed; Δy resolves the active pen's value at each
	// endpoint and is null when either endpoint is a gap or out of range. Returns null until both
	// cursors exist.
	public DeltaReadout? Compute(PenHistoryEnvelope activePenEnvelope)
	{
		ArgumentNullException.ThrowIfNull(activePenEnvelope);

		if (FirstCursor is not { } first || SecondCursor is not { } second)
		{
			return null;
		}

		var deltaTime = (second - first).Duration();
		var deltaY = ComputeDeltaY(first, second, activePenEnvelope);

		return new DeltaReadout(deltaTime, deltaY);
	}

	private double? ComputeDeltaY(DateTime first, DateTime second, PenHistoryEnvelope activePenEnvelope)
	{
		var firstValue = _cursorReadout.ReadAt(first, [activePenEnvelope])[activePenEnvelope.PenId];
		var secondValue = _cursorReadout.ReadAt(second, [activePenEnvelope])[activePenEnvelope.PenId];

		if (firstValue is not { } start || secondValue is not { } end)
		{
			return null;
		}

		return end - start;
	}
}
