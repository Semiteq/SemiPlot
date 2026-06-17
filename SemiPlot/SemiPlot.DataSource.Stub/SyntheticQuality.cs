namespace SemiPlot.DataSource.Stub;

// Deterministic stand-in for OPC quality: a reproducible fraction of samples are marked bad so the
// null=gap path is exercised end to end.
public static class SyntheticQuality
{
	private const long BadEvery = 97;

	public static bool IsBad(long penId, long tickIndex)
	{
		return ((penId + tickIndex) % BadEvery) == 0;
	}
}
