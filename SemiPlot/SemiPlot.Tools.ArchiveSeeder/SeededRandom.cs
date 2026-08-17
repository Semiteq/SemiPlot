namespace SemiPlot.Tools.ArchiveSeeder;

// SplitMix64, written out rather than taken from the BCL: the bench is pinned by a golden hash, and
// a runtime that changed its own generator would change the archive every later slice develops
// against.
internal sealed class SeededRandom
{
	private ulong _state;

	public SeededRandom(long seed, long stream)
	{
		unchecked
		{
			_state = Mix((ulong)seed * 0x9E3779B97F4A7C15UL ^ ((ulong)stream + 0x7F4A7C159E3779B9UL));
		}
	}

	public double NextDouble()
	{
		return (NextUInt64() >> 11) / (double)(1UL << 53);
	}

	public int NextInt32(int minimumInclusive, int maximumExclusive)
	{
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumExclusive, minimumInclusive);

		var span = (ulong)(maximumExclusive - minimumInclusive);

		return minimumInclusive + (int)(NextUInt64() % span);
	}

	// Inverse-transform sampling: the wait between changes of a change-archived variable is bursty
	// rather than uniform.
	public double NextExponential(double mean)
	{
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(mean, 0.0);

		return -mean * Math.Log(1.0 - NextDouble());
	}

	private ulong NextUInt64()
	{
		unchecked
		{
			_state += 0x9E3779B97F4A7C15UL;

			return Mix(_state);
		}
	}

	private static ulong Mix(ulong value)
	{
		unchecked
		{
			value ^= value >> 30;
			value *= 0xBF58476D1CE4E5B9UL;
			value ^= value >> 27;
			value *= 0x94D049BB133111EBUL;
			value ^= value >> 31;

			return value;
		}
	}
}
