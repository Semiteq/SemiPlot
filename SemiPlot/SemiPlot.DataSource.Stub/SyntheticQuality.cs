namespace SemiPlot.DataSource.Stub;

// Deterministically marks a reproducible fraction of samples bad so the null=gap path is exercised.
public static class SyntheticQuality
{
	private const long BadEvery = 97;

	public static bool IsBad(long penId, long tickIndex)
	{
		return ((penId + tickIndex) % BadEvery) == 0;
	}
}
