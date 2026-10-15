using System.Globalization;
using System.Text.RegularExpressions;

namespace SemiPlot.UI.Navigation;

/// <summary>A window width as <c>h:mm:ss</c> with the hours unbounded: <c>0:05:00</c>, <c>168:00:00</c>.</summary>
internal static partial class WindowWidthText
{
	internal const string FieldFormat = "h:mm:ss";

	private const long MaxWholeHours = (long.MaxValue / TimeSpan.TicksPerHour) - 1;

	public static string Format(TimeSpan width)
	{
		var hours = width.Ticks / TimeSpan.TicksPerHour;

		return string.Create(CultureInfo.InvariantCulture, $"{hours}:{width.Minutes:00}:{width.Seconds:00}");
	}

	public static bool TryParse(string text, out TimeSpan width)
	{
		width = TimeSpan.Zero;

		if (WidthPattern().Match(text) is not { Success: true } match
			|| !long.TryParse(
				match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
			|| hours > MaxWholeHours)
		{
			return false;
		}

		var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
		var seconds = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
		width = TimeSpan.FromTicks(hours * TimeSpan.TicksPerHour) + new TimeSpan(0, minutes, seconds);

		return true;
	}

	[GeneratedRegex(@"^\s*([0-9]+):([0-5][0-9]):([0-5][0-9])\s*$")]
	private static partial Regex WidthPattern();
}
