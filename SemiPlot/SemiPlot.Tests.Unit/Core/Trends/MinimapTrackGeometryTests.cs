using AwesomeAssertions;

using SemiPlot.Core.Trends;

using Xunit;

using static SemiPlot.Tests.Unit.Core.Trends.BerlinClock;

namespace SemiPlot.Tests.Unit.Core.Trends;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class MinimapTrackGeometryTests
{
	private const double PageWidth = 1000.0;
	private const double MinimumBoxWidth = 6.0;
	private static readonly DayHours _officeHours = new(7, 17);

	// Wednesday 7 January 2026, 07:00-17:00 Berlin, 100 px per hour over the page width.
	private static readonly MinimapPage _dayPage = DayPage(1, 7, _officeHours);

	[Fact]
	public void WindowBox_InsideThePage_CutsNoEdge()
	{
		var box = Box(Berlin(1, 7, 9), Berlin(1, 7, 11));

		ShouldBe(box, 200.0, 200.0, cutsLeft: false, cutsRight: false);
	}

	[Fact]
	public void WindowBox_StraddlingTheLeftEdge_IsCutThere()
	{
		var box = Box(Berlin(1, 7, 6), Berlin(1, 7, 8));

		ShouldBe(box, 0.0, 100.0, cutsLeft: true, cutsRight: false);
	}

	[Fact]
	public void WindowBox_StraddlingTheRightEdge_IsCutThere()
	{
		var box = Box(Berlin(1, 7, 16), Berlin(1, 8, 9));

		ShouldBe(box, 900.0, 100.0, cutsLeft: false, cutsRight: true);
	}

	[Fact]
	public void WindowBox_WiderThanThePage_IsCutAtBothEdges()
	{
		var box = Box(Berlin(1, 6, 9), Berlin(1, 8, 9));

		ShouldBe(box, 0.0, PageWidth, cutsLeft: true, cutsRight: true);
	}

	[Fact]
	public void WindowBox_OnThePageEdges_CutsNoEdge()
	{
		var box = Box(_dayPage.FromUtc, _dayPage.ToUtc);

		ShouldBe(box, 0.0, PageWidth, cutsLeft: false, cutsRight: false);
	}

	[Fact]
	public void WindowBox_OfANarrowWindowCutAtTheRightEdge_KeepsTheMinimumWidthInside()
	{
		var box = Box(_dayPage.ToUtc.AddSeconds(-30), _dayPage.ToUtc.AddMinutes(1));

		ShouldBe(box, PageWidth - MinimumBoxWidth, MinimumBoxWidth, cutsLeft: false, cutsRight: true);
	}

	[Theory]
	[InlineData(5, 6)]
	[InlineData(17, 18)]
	public void WindowBox_MissingThePage_IsNull(int fromHour, int toHour)
	{
		Box(Berlin(1, 7, fromHour), Berlin(1, 7, toHour)).Should().BeNull();
	}

	[Fact]
	public void WindowBox_EndingOnThePageStart_IsNull()
	{
		Box(Berlin(1, 7, 6), _dayPage.FromUtc).Should().BeNull();
	}

	[Fact]
	public void RunShades_OnADayPage_ClipEachRunAndEdgeOnlyAnInterruptionOnThePage()
	{
		RecipeRun[] runs =
		[
			new(Berlin(1, 7, 6), Berlin(1, 7, 8), IsInterrupted: true),
			new(Berlin(1, 7, 9), Berlin(1, 7, 10), IsInterrupted: false),
			new(Berlin(1, 7, 12), Berlin(1, 7, 13), IsInterrupted: true),
			new(Berlin(1, 7, 16), Berlin(1, 7, 18), IsInterrupted: true),
			new(Berlin(1, 7, 18), Berlin(1, 7, 19), IsInterrupted: false)
		];

		var shades = MinimapTrackGeometry.RunShades(_dayPage, runs, PageWidth, MinimumBoxWidth);

		shades.Select(shade => (Math.Round(shade.Left, 6), Math.Round(shade.Width, 6), shade.HasEndEdge))
			.Should().Equal(
			(0.0, 100.0, true),
			(200.0, 100.0, false),
			(500.0, 100.0, true),
			(900.0, 100.0, false));
	}

	[Fact]
	public void RunShades_OnAWeekPage_AreNone()
	{
		var week = MinimapPage.Snap(MinimapPageUnit.Week, Berlin(1, 7, 12), _officeHours, [], BerlinZone);
		RecipeRun[] runs = [new(Berlin(1, 7, 9), Berlin(1, 7, 10), IsInterrupted: false)];

		MinimapTrackGeometry.RunShades(week, runs, PageWidth, MinimumBoxWidth).Should().BeEmpty();
	}

	[Fact]
	public void PageTicks_OfADay_AreEveryWholeHour_WhenTheLabelsFit()
	{
		var ticks = MinimapTrackGeometry.PageTicks(_dayPage, PageWidth, minimumSpacing: 60.0, BerlinZone);

		ticks.Select(tick => tick.WallClock.Hour).Should().Equal(7, 8, 9, 10, 11, 12, 13, 14, 15, 16);
		ticks.Select(tick => tick.X).Should().Equal(0.0, 100.0, 200.0, 300.0, 400.0, 500.0, 600.0, 700.0, 800.0, 900.0);
		ticks.Should().AllSatisfy(tick => tick.Kind.Should().Be(TrackTickKind.Hour));
	}

	[Fact]
	public void PageTicks_OfADay_TakeTheSmallestStrideThatFits_AlignedToMidnight()
	{
		var ticks = MinimapTrackGeometry.PageTicks(_dayPage, PageWidth, minimumSpacing: 150.0, BerlinZone);

		ticks.Select(tick => tick.WallClock.Hour).Should().Equal(8, 10, 12, 14, 16);
		ticks[0].X.Should().Be(100.0);
	}

	[Fact]
	public void PageTicks_OverTheSpringChange_SkipTheMissingHourWithoutStacking()
	{
		// Berlin skips 02:00-03:00 on Sunday 29 March 2026, so the whole day holds 23 hours.
		var page = DayPage(3, 29, DayHours.FullDay);

		var ticks = MinimapTrackGeometry.PageTicks(page, 230.0, minimumSpacing: 10.0, BerlinZone);

		ticks.Should().HaveCount(23);
		ticks.Select(tick => tick.WallClock.Hour).Should().NotContain(2);
		ticks.Select(tick => tick.X).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
		ticks.Single(tick => tick.WallClock.Hour == 3).X.Should().BeApproximately(20.0, 1e-9);
	}

	[Fact]
	public void PageTicks_OverTheAutumnChange_KeepTheStrideOfRealHours()
	{
		// Berlin repeats 02:00-03:00 on Sunday 25 October 2026; at 10 px per hour a 15 px minimum takes a
		// two-hour stride, so the second 02:00, one real hour after the first, is dropped.
		var page = DayPage(10, 25, DayHours.FullDay);

		var ticks = MinimapTrackGeometry.PageTicks(page, 250.0, minimumSpacing: 15.0, BerlinZone);

		ticks.Select(tick => tick.WallClock.Hour).Should().Equal(0, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22);
		ticks.Select(tick => tick.X).Take(3).Should().Equal(0.0, 20.0, 50.0);
	}

	[Fact]
	public void PageTicks_OfAWeek_AreItsMidnights_OverTheAutumnChange()
	{
		// Berlin repeats 02:00-03:00 on Sunday 25 October 2026, so the week of 19 October holds 169 hours.
		var page = MinimapPage.Snap(MinimapPageUnit.Week, Berlin(10, 21, 12), _officeHours, [], BerlinZone);

		var ticks = MinimapTrackGeometry.PageTicks(page, 1690.0, minimumSpacing: 60.0, BerlinZone);

		ticks.Select(tick => tick.WallClock).Should().Equal(
			Enumerable.Range(19, 7).Select(day => new DateTime(2026, 10, day, 0, 0, 0, DateTimeKind.Unspecified)));
		ticks.Select(tick => tick.X).Should().Equal(0.0, 240.0, 480.0, 720.0, 960.0, 1200.0, 1440.0);
		ticks.Should().AllSatisfy(tick => tick.Kind.Should().Be(TrackTickKind.Day));
	}

	[Theory]
	[InlineData(25.0, new[] { 5, 7, 9, 11 })]
	[InlineData(30.0, new[] { 5 })]
	public void PageTicks_OfANarrowWeek_ThinTheDays(double minimumSpacing, int[] expectedDays)
	{
		var page = MinimapPage.Snap(MinimapPageUnit.Week, Berlin(1, 7, 12), _officeHours, [], BerlinZone);

		var ticks = MinimapTrackGeometry.PageTicks(page, 100.0, minimumSpacing, BerlinZone);

		ticks.Select(tick => tick.WallClock.Day).Should().Equal(expectedDays);
	}

	[Fact]
	public void PageTicks_OverAZeroWidth_AreEmpty()
	{
		MinimapTrackGeometry.PageTicks(_dayPage, 0.0, minimumSpacing: 60.0, BerlinZone).Should().BeEmpty();
	}

	private static PageWindowBox? Box(DateTime windowFromUtc, DateTime windowToUtc)
	{
		return MinimapTrackGeometry.WindowBox(_dayPage, windowFromUtc, windowToUtc, PageWidth, MinimumBoxWidth);
	}

	private static void ShouldBe(PageWindowBox? box, double left, double width, bool cutsLeft, bool cutsRight)
	{
		box.Should().NotBeNull();
		box.Value.Left.Should().BeApproximately(left, 1e-9);
		box.Value.Width.Should().BeApproximately(width, 1e-9);
		(box.Value.CutsLeft, box.Value.CutsRight).Should().Be((cutsLeft, cutsRight));
	}

	private static MinimapPage DayPage(int month, int day, DayHours hours)
	{
		return MinimapPage.Snap(MinimapPageUnit.Day, Berlin(month, day, 12), hours, [], BerlinZone);
	}
}
