using System.Globalization;

namespace SemiPlot.UI.Localization;

/// <summary>
/// The form a counted noun takes in the resource language (docs/architecture/ui-text.md#a-counted-noun): Russian
/// picks one of three by the count's last digits, every other language one for a count of one and many otherwise.
/// </summary>
internal static class CountForms
{
	private const string Russian = "ru";

	public static string Pick(int count, string one, string few, string many)
	{
		if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != Russian)
		{
			return count == 1 ? one : many;
		}

		var (lastDigit, lastTwoDigits) = (count % 10, count % 100);
		if (lastDigit == 1 && lastTwoDigits != 11)
		{
			return one;
		}

		return lastDigit is >= 2 and <= 4 && lastTwoDigits is < 12 or > 14 ? few : many;
	}
}
