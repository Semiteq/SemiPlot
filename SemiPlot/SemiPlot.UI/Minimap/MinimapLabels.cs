using System.Globalization;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Localization;

namespace SemiPlot.UI.Minimap;

/// <summary>
/// The minimap's date labels, in the machine culture's names; a day number makes Russian months genitive.
/// </summary>
internal static class MinimapLabels
{
	private const string DayFormat = "ddd d MMM yyyy";
	private const string WeekdayAndDateFormat = "ddd d MMM";
	private const string MonthFormat = "MMM";
	private const string MonthNameFormat = "MMMM";
	private const string YearFormat = "yyyy";
	private const string DayOfMonthFormat = "%d";
	private const string DayAndMonthFormat = "d MMM";
	private const string DateFormat = "d MMM yyyy";
	private const string HourTickFormat = "HH:mm";
	private const string DayTickFormat = "ddd d";
	private const string WeekHoverFormat = "ddd d HH:mm";

	/// <summary>An archive wall-clock day, <c>Fri 14 Aug 2026</c>.</summary>
	public static string Day(DateOnly day)
	{
		return day.ToString(DayFormat, CultureInfo.CurrentCulture);
	}

	/// <summary>
	/// A Day page as its day, <c>Fri 14 Aug 2026</c>; a Week page as its days inclusive, <c>11-17 May 2026</c>; a
	/// Month page as its month, <c>November 2026</c>; each with the year of its last day.
	/// </summary>
	public static string Page(MinimapPage page)
	{
		var culture = CultureInfo.CurrentCulture;
		var (first, last) = (page.FirstDay, page.EndDay.AddDays(-1));
		var year = " " + last.ToString(YearFormat, culture);
		if (page.Unit != MinimapPageUnit.Week)
		{
			var format = page.Unit == MinimapPageUnit.Day ? WeekdayAndDateFormat : MonthNameFormat;

			return first.ToString(format, culture) + year;
		}

		var lastText = last.ToString(DayAndMonthFormat, culture);
		if (first.Year != last.Year)
		{
			return first.ToString(DateFormat, culture) + " - " + lastText + year;
		}

		return first.Month != last.Month
			? first.ToString(DayAndMonthFormat, culture) + " - " + lastText + year
			: first.ToString(DayOfMonthFormat, culture) + "-" + lastText + year;
	}

	/// <summary>An hour tick as <c>07:00</c>, a day tick as <c>Mon 11</c>.</summary>
	public static string Tick(TrackTick tick)
	{
		var format = tick.Kind == TrackTickKind.Hour ? HourTickFormat : DayTickFormat;

		return tick.WallClock.ToString(format, CultureInfo.CurrentCulture);
	}

	/// <summary>A calendar day label as <c>Mon 11</c>, a month label as <c>Aug</c>.</summary>
	public static string Calendar(CalendarLabel label)
	{
		var format = label.Kind == CalendarLabelKind.Day ? DayTickFormat : MonthFormat;

		return label.Day.ToString(format, CultureInfo.CurrentCulture);
	}

	/// <summary>
	/// A calendar day's tooltip: the day, then its first and last sample or that it holds no rows, then the count of
	/// its operator markers when it holds any.
	/// </summary>
	public static string DayTip(DateOnly day, ArchiveDaySpan? span, int markerCount, TimeZoneInfo zone)
	{
		var culture = CultureInfo.CurrentCulture;
		var tip = span is null
			? Resources.FormatMinimapDayNoRowsTip(Day(day))
			: Resources.FormatMinimapDayRowsTip(
				Day(day),
				ArchiveClock.WallClockOf(span.FirstUtc, zone).ToString(HourTickFormat, culture),
				ArchiveClock.WallClockOf(span.LastUtc, zone).ToString(HourTickFormat, culture));

		return markerCount > 0
			? Resources.FormatMinimapDayMarkersTip(
				tip,
				CountForms.Pick(
					markerCount,
					Resources.FormatMinimapMarkerCountOne(markerCount),
					Resources.FormatMinimapMarkerCountFew(markerCount),
					Resources.FormatMinimapMarkerCountMany(markerCount)))
			: tip;
	}

	/// <summary>The wall-clock time under the pointer: the clock on a Day page, else the day and clock.</summary>
	public static string HoverTime(DateTime wallClock, MinimapPageUnit unit)
	{
		var format = unit == MinimapPageUnit.Day ? HourTickFormat : WeekHoverFormat;

		return wallClock.ToString(format, CultureInfo.CurrentCulture);
	}
}
