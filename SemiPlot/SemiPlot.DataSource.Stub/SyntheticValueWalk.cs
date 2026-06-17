namespace SemiPlot.DataSource.Stub;

public static class SyntheticValueWalk
{
	public static double Value(long seed, long penId, long tickIndex, double minValue, double maxValue)
	{
		var normalized = Normalized(seed, penId, tickIndex);
		return minValue + normalized * (maxValue - minValue);
	}

	// Two decorrelated sine waves plus hash jitter, so the signal looks process-like yet stays a
	// pure function of its inputs.
	private static double Normalized(long seed, long penId, long tickIndex)
	{
		var penHash = Hash(seed, penId);

		var slowPhase = (penHash & 0xFFFF) / 65535.0 * (2.0 * Math.PI);
		var fastPhase = ((penHash >> 16) & 0xFFFF) / 65535.0 * (2.0 * Math.PI);

		var slowPeriod = 64.0 + (penHash >> 32 & 0xFF);
		var fastPeriod = 7.0 + (penHash >> 40 & 0x1F);

		var slow = Math.Sin(2.0 * Math.PI * tickIndex / slowPeriod + slowPhase);
		var fast = Math.Sin(2.0 * Math.PI * tickIndex / fastPeriod + fastPhase);

		var jitter = ToUnitInterval(Hash((long)penHash, tickIndex)) - 0.5;

		var combined = 0.6 * slow + 0.25 * fast + 0.15 * (2.0 * jitter);
		return Math.Clamp((combined + 1.0) / 2.0, 0.0, 1.0);
	}

	private static double ToUnitInterval(ulong hash)
	{
		return (hash >> 11) / (double)(1UL << 53);
	}

	// SplitMix64-style mixing of two values into a well-distributed 64-bit hash.
	private static ulong Hash(long left, long right)
	{
		unchecked
		{
			var value = (ulong)left * 0x9E3779B97F4A7C15UL ^ (ulong)right + 0x7F4A7C159E3779B9UL;
			value ^= value >> 30;
			value *= 0xBF58476D1CE4E5B9UL;
			value ^= value >> 27;
			value *= 0x94D049BB133111EBUL;
			value ^= value >> 31;
			return value;
		}
	}
}
