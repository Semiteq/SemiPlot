namespace SemiPlot.Core.Data;

// Deterministic stand-in for OPC quality: a small, reproducible fraction of samples are marked
// bad so the null=gap path is exercised end to end. A real provider replaces this with the actual
// OPC status code check at the IDataProvider boundary.
public static class SyntheticQuality
{
	private const long BadEvery = 97;

	public static bool IsBad(long penId, long tickIndex)
	{
		return ((penId + tickIndex) % BadEvery) == 0;
	}
}
