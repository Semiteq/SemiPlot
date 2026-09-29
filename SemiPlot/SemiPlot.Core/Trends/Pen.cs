namespace SemiPlot.Core.Trends;

public sealed record Pen(
	int PenId,
	string Name,
	IReadOnlyList<string> Groups,
	string Color,
	string? Unit = null,
	string? Format = null,
	bool EnabledOnStart = true,
	double? ScaleMin = null,
	double? ScaleMax = null,
	PenLineStyle LineStyle = PenLineStyle.Interpolated)
{
	/// <summary>Compares <see cref="Groups"/> element by element, so two reads of one stored pen are equal.</summary>
	public bool Equals(Pen? other)
	{
		return other is not null
			&& PenId == other.PenId
			&& string.Equals(Name, other.Name, StringComparison.Ordinal)
			&& Groups.SequenceEqual(other.Groups, StringComparer.Ordinal)
			&& string.Equals(Color, other.Color, StringComparison.Ordinal)
			&& string.Equals(Unit, other.Unit, StringComparison.Ordinal)
			&& string.Equals(Format, other.Format, StringComparison.Ordinal)
			&& EnabledOnStart == other.EnabledOnStart
			&& Nullable.Equals(ScaleMin, other.ScaleMin)
			&& Nullable.Equals(ScaleMax, other.ScaleMax)
			&& LineStyle == other.LineStyle;
	}

	public override int GetHashCode()
	{
		var hash = new HashCode();

		hash.Add(PenId);
		hash.Add(Name, StringComparer.Ordinal);

		foreach (var group in Groups)
		{
			hash.Add(group, StringComparer.Ordinal);
		}

		hash.Add(Color, StringComparer.Ordinal);
		hash.Add(Unit, StringComparer.Ordinal);
		hash.Add(Format, StringComparer.Ordinal);
		hash.Add(EnabledOnStart);
		hash.Add(ScaleMin);
		hash.Add(ScaleMax);
		hash.Add(LineStyle);

		return hash.ToHashCode();
	}
}
