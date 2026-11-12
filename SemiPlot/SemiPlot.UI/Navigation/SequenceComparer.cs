namespace SemiPlot.UI.Navigation;

internal sealed class SequenceComparer<T> : IEqualityComparer<IReadOnlyList<T>>
{
	public static readonly SequenceComparer<T> Instance = new();

	public bool Equals(IReadOnlyList<T>? x, IReadOnlyList<T>? y)
	{
		return ReferenceEquals(x, y) || (x is not null && y is not null && x.SequenceEqual(y));
	}

	public int GetHashCode(IReadOnlyList<T> obj)
	{
		return obj.Count;
	}
}
