using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Minimap;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Minimap;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class MinimapLabelsTests
{
	[Theory]
	[InlineData("en-US", 2026, 8, 14, "Fri 14 Aug 2026")]
	[InlineData("ru-RU", 2026, 5, 11, "\u043f\u043d 11 \u043c\u0430\u044f 2026")]
	[InlineData("ru-RU", 2026, 11, 9, "\u043f\u043d 9 \u043d\u043e\u044f\u0431. 2026")]
	public void ADayPage_IsLabelledByItsDay(string cultureName, int year, int month, int day, string expected)
	{
		using var culture = new CultureScope(cultureName);

		MinimapLabels.Page(PageOf(MinimapPageUnit.Day, year, month, day)).Should().Be(expected);
	}

	[Theory]
	[InlineData("en-US", 2026, 5, 13, "11-17 May 2026")]
	[InlineData("en-US", 2026, 4, 29, "27 Apr - 3 May 2026")]
	[InlineData("en-US", 2025, 12, 31, "29 Dec 2025 - 4 Jan 2026")]
	[InlineData("ru-RU", 2026, 5, 13, "11-17 \u043c\u0430\u044f 2026")]
	[InlineData("ru-RU", 2026, 12, 30, "28 \u0434\u0435\u043a. 2026 - 3 \u044f\u043d\u0432. 2027")]
	public void AWeekPage_IsLabelledByItsDaysInclusive(
		string cultureName,
		int year,
		int month,
		int day,
		string expected)
	{
		using var culture = new CultureScope(cultureName);

		MinimapLabels.Page(PageOf(MinimapPageUnit.Week, year, month, day)).Should().Be(expected);
	}

	[Theory]
	[InlineData("en-US", 2026, 11, "November 2026")]
	[InlineData("ru-RU", 2026, 11, "\u043d\u043e\u044f\u0431\u0440\u044c 2026")]
	[InlineData("ru-RU", 2026, 5, "\u043c\u0430\u0439 2026")]
	public void AMonthPage_IsLabelledByItsMonthInTheNominative(string cultureName, int year, int month, string expected)
	{
		using var culture = new CultureScope(cultureName);

		MinimapLabels.Page(PageOf(MinimapPageUnit.Month, year, month, 17)).Should().Be(expected);
	}

	[Theory]
	[InlineData("en-US", 0, "Mon 9 Nov 2026 \u00b7 rows 07:02-16:40")]
	[InlineData("en-US", 1, "Mon 9 Nov 2026 \u00b7 rows 07:02-16:40 \u00b7 1 marker")]
	[InlineData("en-US", 21, "Mon 9 Nov 2026 \u00b7 rows 07:02-16:40 \u00b7 21 markers")]
	[InlineData("ru-RU", 1, "\u00b7 1 \u043c\u0435\u0442\u043a\u0430")]
	[InlineData("ru-RU", 2, "\u00b7 2 \u043c\u0435\u0442\u043a\u0438")]
	[InlineData("ru-RU", 5, "\u00b7 5 \u043c\u0435\u0442\u043e\u043a")]
	[InlineData("ru-RU", 11, "\u00b7 11 \u043c\u0435\u0442\u043e\u043a")]
	[InlineData("ru-RU", 21, "\u00b7 21 \u043c\u0435\u0442\u043a\u0430")]
	[InlineData("ru-RU", 22, "\u00b7 22 \u043c\u0435\u0442\u043a\u0438")]
	[InlineData("ru-RU", 112, "\u00b7 112 \u043c\u0435\u0442\u043e\u043a")]
	public void ADaysTooltip_CountsItsOperatorMarkers_InTheLanguagesPluralForm(
		string cultureName,
		int markerCount,
		string expectedEnd)
	{
		using var culture = new CultureScope(cultureName);
		var day = new DateOnly(2026, 11, 9);
		var span = new ArchiveDaySpan(day, new DateTime(2026, 11, 9, 7, 2, 0), new DateTime(2026, 11, 9, 16, 40, 0));

		MinimapLabels.DayTip(day, span, markerCount, TimeZoneInfo.Utc).Should().EndWith(expectedEnd);
	}

	private static MinimapPage PageOf(MinimapPageUnit unit, int year, int month, int day)
	{
		var noonUtc = new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc);

		return MinimapPage.Snap(unit, noonUtc, DayHours.FullDay, [], TimeZoneInfo.Utc);
	}
}
