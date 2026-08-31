namespace SemiPlot.Core.Trends;

// Δy is measured for the active pen only, since pens share X but not Y.
public sealed class DeltaCursorModel
{
	private readonly CursorReadoutModel _cursorReadout = new();

	public DateTime? FirstCursor { get; private set; }

	public DateTime? SecondCursor { get; private set; }

	public bool HasBothCursors => FirstCursor.HasValue && SecondCursor.HasValue;

	// A third placement starts a fresh measurement from the new point.
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

	public DeltaReadout? Compute(PenHistoryEnvelope activePenEnvelope)
	{
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
