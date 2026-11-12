namespace SemiPlot.Tools.ArchiveSeeder;

/// <summary>
/// The value a pen holds at a lattice index: the walk, or under shifts an idle level between recipe runs.
/// </summary>
public sealed class LatticeValues
{
	// The idle level's place in the stored pair: min + IdleShare * (max - min).
	private const double IdleShare = 0.05;

	private readonly long _seed;

	// The recipe runs as half-open index ranges in ascending order; none under the continuous schedule.
	private readonly long[]? _runFirstIndices;
	private readonly long[]? _runEndIndices;

	private LatticeValues(long seed, long[]? runFirstIndices, long[]? runEndIndices)
	{
		_seed = seed;
		_runFirstIndices = runFirstIndices;
		_runEndIndices = runEndIndices;
	}

	public static LatticeValues Continuous(long seed)
	{
		return new LatticeValues(seed, null, null);
	}

	/// <summary>The walk inside each run and the idle level everywhere else; run bounds sit on the lattice.</summary>
	public static LatticeValues Shifts(long seed, long intervalTicks, IEnumerable<ShiftSchedule.ScheduledRun> runs)
	{
		var ordered = runs.OrderBy(run => run.Start).ToArray();

		return new LatticeValues(
			seed,
			[.. ordered.Select(run => run.Start.Ticks / intervalTicks)],
			[.. ordered.Select(run => run.End.Ticks / intervalTicks)]);
	}

	/// <summary>
	/// A slow pen holds its last change, the nearest index at or below that is a multiple of its stride.
	/// </summary>
	public double At(SyntheticPen pen, long stride, long index)
	{
		var heldIndex = index - (index % stride);

		return IdlesAt(heldIndex) ? Idle(pen) : Walk(pen, heldIndex);
	}

	private static double Idle(SyntheticPen pen)
	{
		return pen.MinValue + (IdleShare * (pen.MaxValue - pen.MinValue));
	}

	private bool IdlesAt(long index)
	{
		if (_runFirstIndices is null || _runEndIndices is null)
		{
			return false;
		}

		var found = Array.BinarySearch(_runFirstIndices, index);
		var run = found >= 0 ? found : ~found - 1;

		return run < 0 || index >= _runEndIndices[run];
	}

	private double Walk(SyntheticPen pen, long index)
	{
		return pen.LogScaleOnStart
			? SyntheticValueWalk.LogValue(_seed, pen.PenId, index, pen.MinValue, pen.MaxValue)
			: SyntheticValueWalk.Value(_seed, pen.PenId, index, pen.MinValue, pen.MaxValue);
	}
}
