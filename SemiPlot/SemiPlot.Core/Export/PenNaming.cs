using System.Globalization;

using SemiPlot.Core.Trends;

namespace SemiPlot.Core.Export;

/// <summary>How the export tells pens apart: the label and the file field add the id to a shared name.</summary>
public static class PenNaming
{
	public static HashSet<string> SharedKeys(IEnumerable<Pen> pens, Func<Pen, string> keyOf)
	{
		return pens
			.GroupBy(keyOf, StringComparer.Ordinal)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.ToHashSet(StringComparer.Ordinal);
	}

	public static string WithId(string text, Pen pen)
	{
		return $"{text} ({pen.PenId.ToString(CultureInfo.InvariantCulture)})";
	}
}
