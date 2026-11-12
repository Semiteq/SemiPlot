using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Navigation;

using Xunit;

using static SemiPlot.Tests.Unit.Core.Trends.BerlinClock;

namespace SemiPlot.Tests.Unit.UI.Navigation;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class NavigationListRowsTests : IDisposable
{
	private static readonly NavigationListEntry[] _newestFirstMarkers =
	[
		NavigationListEntry.Of(new StoredMarker(3, Utc(11, 12, 14, 32), "Etch start")),
		NavigationListEntry.Of(new StoredMarker(2, Utc(11, 3, 8, 0), null)),
		NavigationListEntry.Of(new StoredMarker(1, Utc(10, 30, 9, 15), "Purge"))
	];

	private static readonly NavigationListEntry[] _newestFirstRuns =
	[
		NavigationListEntry.Of(new RecipeRun(Utc(11, 12, 9, 0), Utc(11, 12, 10, 5), false, "Etch_Si.csv")),
		NavigationListEntry.Of(new RecipeRun(Utc(11, 3, 8, 0), Utc(11, 3, 8, 35), true, "Purge.csv")),
		NavigationListEntry.Of(new RecipeRun(Utc(10, 30, 9, 15), Utc(10, 30, 9, 15, 29), false))
	];

	private readonly CultureScope _culture = new("en-US");

	[Fact]
	public void Build_PutsTheMarkersNewestFirst_UnderAHeaderPerMonthWithItsCount()
	{
		var rows = NavigationListRows.Build(_newestFirstMarkers, filter: null, TimeZoneInfo.Utc);

		rows.Select(Describe).Should().Equal(
			"NOVEMBER 2026 (2)",
			"Etch start | 12 Nov 14:32",
			" | 3 Nov 08:00",
			"OCTOBER 2026 (1)",
			"Purge | 30 Oct 09:15");
		rows.OfType<NavigationListItem>().Should().OnlyContain(item => !item.IsRun && item.DurationText == null);
	}

	[Fact]
	public void Build_PutsTheRunsNewestFirst_WithTheirDurationAndInterruptedFlag()
	{
		var rows = NavigationListRows.Build(_newestFirstRuns, filter: null, TimeZoneInfo.Utc);

		rows.Select(Describe).Should().Equal(
			"NOVEMBER 2026 (2)",
			"Etch_Si.csv | 12 Nov 09:00 | 1 h 05 min",
			"Purge.csv | 3 Nov 08:00 | 35 min | interrupted",
			"OCTOBER 2026 (1)",
			" | 30 Oct 09:15 | 0 min");
	}

	[Theory]
	[InlineData("en-US", 35, "35 min")]
	[InlineData("en-US", 65, "1 h 05 min")]
	[InlineData("en-US", 600, "10 h 00 min")]
	[InlineData("ru-RU", 35, "35 \u043c\u0438\u043d")]
	[InlineData("ru-RU", 65, "1 \u0447 05 \u043c\u0438\u043d")]
	public void DurationText_ShowsMinutesUnderAnHour_AndHoursWithTwoDigitMinutesFromIt(
		string cultureName,
		int minutes,
		string expected)
	{
		using var culture = new CultureScope(cultureName);

		NavigationListRows.DurationText(TimeSpan.FromMinutes(minutes)).Should().Be(expected);
	}

	[Fact]
	public void ARunsDuration_IsRoundedToWholeMinutes()
	{
		var start = Utc(11, 3, 8, 0);

		NavigationListEntry.Of(new RecipeRun(start, start.AddSeconds(89), IsInterrupted: false))
			.Duration.Should().Be(TimeSpan.FromMinutes(1));
		NavigationListEntry.Of(new RecipeRun(start, start.AddSeconds(90), IsInterrupted: false))
			.Duration.Should().Be(TimeSpan.FromMinutes(2));
	}

	[Fact]
	public void Build_AnEntryWithNoName_ShowsItsDateAlone_WithNoTooltip()
	{
		var rows = NavigationListRows.Build(_newestFirstMarkers, filter: null, TimeZoneInfo.Utc);

		var unlabelled = rows.OfType<NavigationListItem>().Single(item => item.Entry.Key == 2);
		(unlabelled.Name, unlabelled.NameTip).Should().Be((string.Empty, null));
		rows.OfType<NavigationListItem>().First().NameTip.Should().Be("Etch start", "a long label shows whole in its tip");
	}

	[Theory]
	[InlineData("etch", new long[] { 3 })]
	[InlineData("NOV", new long[] { 3, 2 })]
	[InlineData("12 nov", new long[] { 3 })]
	[InlineData("  purge ", new long[] { 1 })]
	[InlineData("november", new long[] { 3, 2 })]
	[InlineData("2026", new long[] { 3, 2, 1 })]
	[InlineData("october 2026", new long[] { 1 })]
	[InlineData("nov etch", new long[] { 3 })]
	[InlineData("etch oct", new long[0])]
	[InlineData("08:00  november", new long[] { 2 })]
	public void Build_KeepsTheEntriesThatHoldEveryTokenInTheirNameShownDateOrMonthHeader_IgnoringCase(
		string filter,
		long[] kept)
	{
		var rows = NavigationListRows.Build(_newestFirstMarkers, filter, TimeZoneInfo.Utc);

		rows.OfType<NavigationListItem>().Select(item => item.Entry.Key).Should().Equal(kept);
		rows.OfType<NavigationListHeader>().Sum(header => header.Count).Should().Be(
			kept.Length, "a header counts its kept entries");
	}

	[Fact]
	public void Build_FiltersTheRunsByRecipeName_AndNeverByTheDuration()
	{
		Kept(_newestFirstRuns, "etch_si").Should().Equal(Utc(11, 12, 9, 0).Ticks);
		Kept(_newestFirstRuns, "csv nov").Should().Equal(Utc(11, 12, 9, 0).Ticks, Utc(11, 3, 8, 0).Ticks);
		Kept(_newestFirstRuns, "35").Should().BeEmpty("the duration is no filter field");
	}

	[Fact]
	public void Build_WithNoMatch_HoldsNoRow()
	{
		NavigationListRows.Build(_newestFirstMarkers, "zzz", TimeZoneInfo.Utc).Should().BeEmpty();
	}

	// May and April, whose genitive and nominative forms differ.
	[Fact]
	public void Build_InRussian_ShowsTheGenitiveDate_AndTheNominativeMonthInTheHeader()
	{
		using var russian = new CultureScope("ru-RU");
		NavigationListEntry[] entries =
		[
			NavigationListEntry.Of(new StoredMarker(2, Utc(5, 11, 14, 32), "Etch start")),
			NavigationListEntry.Of(new StoredMarker(1, Utc(4, 30, 9, 15), "Purge"))
		];

		var rows = NavigationListRows.Build(entries, filter: null, TimeZoneInfo.Utc);

		rows.Select(Describe).Should().Equal(
			"\u041c\u0410\u0419 2026 (1)",
			"Etch start | 11 \u043c\u0430\u044f 14:32",
			"\u0410\u041f\u0420\u0415\u041b\u042c 2026 (1)",
			"Purge | 30 \u0430\u043f\u0440. 09:15");
		Kept(entries, "\u043c\u0430\u044f").Should().Equal(2);
		Kept(entries, "\u043c\u0430\u0439").Should().Equal(2);
		Kept(entries, "\u0430\u043f\u0440\u0435\u043b\u044c").Should().Equal(1);
		Kept(entries, "\u043c\u0430\u044f etch").Should().Equal(2);
	}

	[Fact]
	public void Build_GroupsByTheMonthOfTheZone_NotOfUtc()
	{
		// 23:30 UTC on 31 October is 00:30 on 1 November in Berlin.
		NavigationListEntry[] entries = [NavigationListEntry.Of(new StoredMarker(1, Utc(10, 31, 23, 30), "late"))];

		var rows = NavigationListRows.Build(entries, filter: null, BerlinZone);

		rows.Select(Describe).Should().Equal("NOVEMBER 2026 (1)", "late | 1 Nov 00:30");
	}

	[Fact]
	public void Nearest_IsTheMarkerNearestTheTime_TheNewerOnATie()
	{
		var rows = NavigationListRows.Build(_newestFirstMarkers, filter: null, TimeZoneInfo.Utc);

		NavigationListRows.Nearest(rows, Utc(11, 4, 0, 0))!.Entry.Key.Should().Be(2);
		NavigationListRows.Nearest(rows, Utc(11, 1, 8, 37).AddSeconds(30))!.Entry.Key.Should().Be(
			2, "a tie takes the newer");
		NavigationListRows.Nearest([], Utc(11, 4, 0, 0)).Should().BeNull();
	}

	[Fact]
	public void Nearest_IsTheRunThatHoldsTheTime_ElseTheRunWhoseNearerEndLiesNearest()
	{
		var rows = NavigationListRows.Build(_newestFirstRuns, filter: null, TimeZoneInfo.Utc);

		NavigationListRows.Nearest(rows, Utc(11, 12, 10, 0))!.Entry.Key.Should().Be(Utc(11, 12, 9, 0).Ticks);
		NavigationListRows.Nearest(rows, Utc(11, 3, 12, 0))!.Entry.Key.Should().Be(
			Utc(11, 3, 8, 0).Ticks, "08:35 lies nearer noon than the next run's start nine days later");
	}

	public void Dispose()
	{
		_culture.Dispose();
	}

	private static IEnumerable<long> Kept(IReadOnlyList<NavigationListEntry> entries, string filter)
	{
		return NavigationListRows.Build(entries, filter, TimeZoneInfo.Utc)
			.OfType<NavigationListItem>()
			.Select(item => item.Entry.Key);
	}

	private static string Describe(NavigationListRow row)
	{
		return row switch
		{
			NavigationListHeader header => $"{header.Text} ({header.Count})",
			NavigationListItem { IsRun: false } item => $"{item.Name} | {item.DateText}",
			NavigationListItem item => $"{item.Name} | {item.DateText} | {item.DurationText}"
				+ (item.IsInterrupted ? " | interrupted" : string.Empty),
			_ => throw new InvalidOperationException("The list holds headers and entries only.")
		};
	}

	private static DateTime Utc(int month, int day, int hour, int minute, int second = 0)
	{
		return new DateTime(2026, month, day, hour, minute, second, DateTimeKind.Utc);
	}
}
