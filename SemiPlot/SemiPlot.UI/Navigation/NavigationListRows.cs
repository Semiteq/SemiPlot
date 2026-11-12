using System.Collections.ObjectModel;
using System.Globalization;

using SemiPlot.Core.Data;
using SemiPlot.UI.Localization;

namespace SemiPlot.UI.Navigation;

/// <summary>A navigation list's rows: the filter, the month headers and the entry nearest a time.</summary>
internal static class NavigationListRows
{
	private const string DateFormat = "d MMM HH:mm";
	private const string MonthFormat = "MMMM yyyy";
	private const int MinutesPerHour = 60;
	private const string MinutesPastHourFormat = "00";

	/// <summary>
	/// The entries that every whitespace-separated token of the filter finds in their name, shown date or month
	/// header, newest first, under a header per month of the zone, in <see cref="CultureInfo.CurrentCulture"/>.
	/// </summary>
	public static ReadOnlyCollection<NavigationListRow> Build(
		IReadOnlyList<NavigationListEntry> newestFirst,
		string? filter,
		TimeZoneInfo zone)
	{
		var culture = CultureInfo.CurrentCulture;
		var tokens = (filter ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
		var rows = new List<NavigationListRow>();
		var months = Shown(newestFirst, zone, culture).GroupBy(entry => (entry.Local.Year, entry.Local.Month));
		foreach (var month in months)
		{
			var header = month.First().Local.ToString(MonthFormat, culture).ToUpper(culture);
			var kept = month
				.Select(entry => entry.Item)
				.Where(item => Array.TrueForAll(tokens, token => Matches(item, header, token, culture)))
				.ToList();
			if (kept.Count > 0)
			{
				rows.Add(new NavigationListHeader(header, kept.Count));
				rows.AddRange(kept);
			}
		}

		return rows.AsReadOnly();
	}

	/// <summary>The entry row nearest the time; the newer on a tie.</summary>
	public static NavigationListItem? Nearest(IReadOnlyList<NavigationListRow> rows, DateTime timeUtc)
	{
		return rows.OfType<NavigationListItem>().MinBy(item => item.Entry.DistanceTo(timeUtc));
	}

	/// <summary>A run's duration as <c>35 min</c> under an hour and <c>1 h 05 min</c> from it.</summary>
	public static string DurationText(TimeSpan duration)
	{
		var minutes = (int)duration.TotalMinutes;

		return minutes < MinutesPerHour
			? Resources.FormatRunListDurationMinutes(minutes)
			: Resources.FormatRunListDurationHours(
				minutes / MinutesPerHour,
				(minutes % MinutesPerHour).ToString(MinutesPastHourFormat, CultureInfo.CurrentCulture));
	}

	private static IEnumerable<(NavigationListItem Item, DateTime Local)> Shown(
		IReadOnlyList<NavigationListEntry> entries,
		TimeZoneInfo zone,
		CultureInfo culture)
	{
		foreach (var entry in entries)
		{
			var local = ArchiveClock.WallClockOf(entry.TimeUtc, zone);
			var durationText = entry.Duration is { } duration ? DurationText(duration) : null;

			yield return (new NavigationListItem(entry, local.ToString(DateFormat, culture), durationText), local);
		}
	}

	private static bool Matches(NavigationListItem item, string header, string token, CultureInfo culture)
	{
		var compare = culture.CompareInfo;

		return compare.IndexOf(item.Name, token, CompareOptions.IgnoreCase) >= 0
			|| compare.IndexOf(item.DateText, token, CompareOptions.IgnoreCase) >= 0
			|| compare.IndexOf(header, token, CompareOptions.IgnoreCase) >= 0;
	}
}
