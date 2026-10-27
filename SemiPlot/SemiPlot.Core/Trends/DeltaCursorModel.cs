namespace SemiPlot.Core.Trends;

// Delta y is measured for the active pen only, since pens share X but not Y.
public sealed class DeltaCursorModel
{
	public DateTime? FirstCursor { get; private set; }

	public DateTime? SecondCursor { get; private set; }

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

	public DeltaReadout? Compute(PenHistoryEnvelope activePenEnvelope, PenLineStyle lineStyle)
	{
		if (FirstCursor is not { } first || SecondCursor is not { } second)
		{
			return null;
		}

		var deltaTime = (second - first).Duration();
		var deltaY = ComputeDeltaY(first, second, activePenEnvelope, lineStyle);

		return new DeltaReadout(deltaTime, deltaY);
	}

	private static double? ComputeDeltaY(
		DateTime first,
		DateTime second,
		PenHistoryEnvelope activePenEnvelope,
		PenLineStyle lineStyle)
	{
		var firstValue = CursorReadoutModel.ReadPenAt(first, activePenEnvelope, lineStyle);
		var secondValue = CursorReadoutModel.ReadPenAt(second, activePenEnvelope, lineStyle);

		if (firstValue is not { } start || secondValue is not { } end)
		{
			return null;
		}

		return end - start;
	}
}
