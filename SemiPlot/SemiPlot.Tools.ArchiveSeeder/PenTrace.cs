namespace SemiPlot.Tools.ArchiveSeeder;

// The single place the anchor-pair rule lives: a change carries the previous value one poll interval
// ahead of it, unless the last row is already that old — during a ramp or a spike that row is itself the
// anchor, and a second one would collide with it on (id, l, t).
internal sealed class PenTrace(List<ArchiveRow> rows, int penId, TimeSpan pollInterval)
{
	private double _lastValue;

	private int _segmentIndex;

	public DateTime LastTimestamp { get; private set; }

	public double RestingLevel { get; set; }

	public int NextSegment()
	{
		return _segmentIndex++;
	}

	// A run opens on the pen's current level rather than on its last written row, which is what makes
	// the resumed run's first row a change row with no pre-anchor.
	public void StartRun(DateTime firstTimestamp)
	{
		LastTimestamp = firstTimestamp;
		_lastValue = RestingLevel;

		rows.Add(Row(firstTimestamp, RestingLevel));
	}

	public void Change(DateTime instant, double value)
	{
		// The distance rather than the sum: the same rule, but a run ending at the last representable
		// instant makes LastTimestamp + pollInterval throw out of the guard itself.
		ArgumentOutOfRangeException.ThrowIfLessThan(instant - LastTimestamp, pollInterval);

		if (instant - LastTimestamp > pollInterval)
		{
			rows.Add(Row(instant - pollInterval, _lastValue));
		}

		rows.Add(Row(instant, value));

		LastTimestamp = instant;
		_lastValue = value;
	}

	private ArchiveRow Row(DateTime timestamp, double value)
	{
		return new ArchiveRow(penId, ArchiveRow.RawLayer, timestamp, value, ArchiveRow.OrdinaryQuality);
	}
}
