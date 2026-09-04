using System.Globalization;
using System.Text;

namespace SemiPlot.UI.Chart;

public static class ChartHoverReadout
{
	public static string BuildContent(
		DateTime cursorTime,
		IReadOnlyDictionary<int, double?> values,
		IReadOnlyCollection<TrendPenState> pens)
	{
		var content = new StringBuilder();
		content.Append(FormatTimestamp(cursorTime));

		foreach (var pen in pens)
		{
			if (!pen.IsVisible)
			{
				continue;
			}

			var value = values.TryGetValue(pen.Pen.PenId, out var penValue) ? penValue : null;
			content.Append('\n');
			content.Append(pen.Pen.Name);
			content.Append(": ");
			content.Append(FormatValue(value));
		}

		return content.ToString();
	}

	private static string FormatTimestamp(DateTime cursorTimeUtc)
	{
		return DateTime.SpecifyKind(cursorTimeUtc, DateTimeKind.Utc)
			.ToLocalTime()
			.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
	}

	private static string FormatValue(double? value)
	{
		return value is { } number ? number.ToString("0.###", CultureInfo.CurrentCulture) : "—";
	}
}
