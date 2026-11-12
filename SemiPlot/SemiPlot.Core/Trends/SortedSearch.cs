namespace SemiPlot.Core.Trends;

internal static class SortedSearch
{
	/// <summary>
	/// The first index at which <paramref name="reached"/> holds, or the count; it must hold monotonically.
	/// </summary>
	public static int FirstIndex(int count, Func<int, bool> reached)
	{
		var low = 0;
		var high = count;
		while (low < high)
		{
			var middle = low + ((high - low) / 2);
			if (reached(middle))
			{
				high = middle;
			}
			else
			{
				low = middle + 1;
			}
		}

		return low;
	}
}
