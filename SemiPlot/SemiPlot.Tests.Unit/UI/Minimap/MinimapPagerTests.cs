using System.Reactive.Linq;
using System.Windows.Input;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Minimap;

[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class MinimapPagerTests
{
	private static readonly DateOnly _thursday = new(2025, 12, 25);
	private static readonly DateOnly _friday = new(2025, 12, 26);
	private static readonly DateOnly _saturday = new(2025, 12, 27);
	private static readonly DateOnly _sunday = new(2025, 12, 28);
	private static readonly DateOnly _monday = new(2025, 12, 29);
	private static readonly DateOnly _tuesday = new(2025, 12, 30);
	private static readonly DateOnly _wednesday = new(2025, 12, 31);

	[AvaloniaFact]
	public async Task NextPage_FromAFriday_LandsOnMondayAtTheSameClockTime_AndPreviousPageReturns()
	{
		using var stand = await WithWorkingDaysAsync();
		stand.Navigation.SetWindow(At(_friday, 10), At(_friday, 11));
		var pager = stand.ViewModel.Pager;
		pager.Page.Should().Be(new MinimapPage(
			MinimapPageUnit.Day, _friday, new DayHours(7, 17), At(_friday, 7), At(_friday, 17)));

		pager.NextPageCommand.Execute().Subscribe();

		Window(stand).Should().Be((At(_monday, 10), At(_monday, 11)));
		pager.Page.FirstDay.Should().Be(_monday);

		pager.PreviousPageCommand.Execute().Subscribe();

		Window(stand).Should().Be((At(_friday, 10), At(_friday, 11)));
	}

	[AvaloniaFact]
	public async Task AStepWhoseClockOffsetHoldsNoSample_LandsAtTheDaysFirstSample_KeepingTheWidth()
	{
		using var stand = await WithWorkingDaysAsync();
		stand.Navigation.SetWindow(At(_friday, 2), At(_friday, 3, 30));

		stand.ViewModel.Pager.NextPageCommand.Execute().Subscribe();

		Window(stand).Should().Be((At(_monday, 7), At(_monday, 8, 30)));
	}

	[AvaloniaFact]
	public async Task EveryStep_LandsOnItsTarget_AtWeekAndAtDay()
	{
		using var stand = await WithWorkingDaysAsync();
		var pager = stand.ViewModel.Pager;
		stand.Navigation.SetWindow(At(_thursday, 12), At(_friday, 23));
		pager.Page.Unit.Should().Be(MinimapPageUnit.Week);
		pager.Page.FirstDay.Should().Be(new DateOnly(2025, 12, 22), "the week holds the window");
		var weekTarget = pager.NextPage;

		pager.NextPageCommand.Execute().Subscribe();

		pager.Page.Should().Be(weekTarget, "the landed window keeps the week");
		Window(stand).Should().Be(
			(At(_monday, 7), At(_monday, 7) + TimeSpan.FromHours(35.0)), "New Year's Day holds no sample");

		stand.Navigation.SetWindow(At(_friday, 16), At(_friday, 17));
		(pager.Page.Unit, pager.Page.FirstDay).Should().Be((MinimapPageUnit.Day, _friday));
		var dayTarget = pager.NextPage;

		pager.NextPageCommand.Execute().Subscribe();

		pager.Page.Should().Be(dayTarget);
		pager.Page.FirstDay.Should().Be(_monday, "the step skips the weekend");
		Window(stand).Should().Be((At(_monday, 16), At(_monday, 17)));

		stand.Navigation.SetWindow(At(_friday, 23, 30), At(_saturday, 0, 30));
		pager.Page.Unit.Should().Be(MinimapPageUnit.Week, "no Day page holds a window across midnight");
	}

	[AvaloniaFact]
	public async Task APanPastThePage_TurnsIt()
	{
		using var stand = await WithWorkingDaysAsync();
		var pager = stand.ViewModel.Pager;
		stand.Navigation.SetWindow(At(_monday, 15), At(_monday, 16));
		pager.Page.FirstDay.Should().Be(_monday);

		stand.Navigation.PanBy(TimeSpan.FromHours(18.0));

		pager.Page.Unit.Should().Be(MinimapPageUnit.Day, "09:00-10:00 lies inside Tuesday's working hours");

		pager.Page.FirstDay.Should().Be(_tuesday);
		(pager.NextPage?.FirstDay).Should().Be(_wednesday);
		(pager.PreviousPage?.FirstDay).Should().Be(_monday);
	}

	[AvaloniaFact]
	public async Task ThePage_FollowsTheWindow_DayThenWeekThenMonth_AndNarrowingReturnsToTheDay()
	{
		using var stand = await WithWorkingDaysAsync();
		var pager = stand.ViewModel.Pager;

		stand.Navigation.SetWindow(At(_monday, 10), At(_monday, 11));
		pager.Page.Unit.Should().Be(MinimapPageUnit.Day);

		stand.Navigation.SetWindow(At(_monday, 4), At(_monday, 16));
		pager.Page.Should().Be(
			new MinimapPage(
				MinimapPageUnit.Week, _monday, new DayHours(7, 17), At(_monday, 0), At(_monday.AddDays(7), 0)),
			"a 12 h window overruns the 07:00-17:00 Day page");

		stand.Navigation.SetWindow(At(_friday, 10), At(_monday, 11));
		pager.Page.Should().Be(
			new MinimapPage(
				MinimapPageUnit.Month,
				new DateOnly(2025, 12, 1),
				new DayHours(7, 17),
				At(new DateOnly(2025, 12, 1), 0),
				At(new DateOnly(2026, 1, 1), 0)),
			"the window crosses a week");

		stand.Navigation.SetWindow(At(_monday, 10), At(_monday, 11));
		pager.Page.Unit.Should().Be(MinimapPageUnit.Day);
	}

	[AvaloniaFact]
	public async Task OnAMonthPage_TheStepsTurnByMonth_ToTheNearestMonthWithRows()
	{
		var (firstNovember, lateNovember) = (new DateOnly(2025, 11, 3), new DateOnly(2025, 11, 24));
		var february = new DateOnly(2026, 2, 3);
		using var stand = new MinimapStand();
		stand.Provider.ArchiveFirstUtc = At(firstNovember, 7);
		stand.Provider.ArchiveLastUtc = At(february, 17);
		stand.Provider.ArchiveDaySpans =
			[Span(firstNovember), Span(lateNovember), Span(_monday), Span(_tuesday), Span(february)];
		stand.Navigation.TrackDataExtents(At(firstNovember, 7), At(february, 17));
		await stand.LoadExtentAsync();
		stand.LandRead();
		var pager = stand.ViewModel.Pager;
		stand.Navigation.SetWindow(At(new DateOnly(2025, 12, 22), 10), At(_tuesday, 10));
		pager.Page.Unit.Should().Be(MinimapPageUnit.Month, "an eight-day window crosses a week");

		(pager.NextPage?.FirstDay).Should().Be(new DateOnly(2026, 2, 1), "January holds no rows");
		(pager.PreviousPage?.FirstDay).Should().Be(new DateOnly(2025, 11, 1));

		pager.PreviousPageCommand.Execute().Subscribe();

		Window(stand).Should().Be(
			(At(new DateOnly(2025, 11, 22), 10), At(new DateOnly(2025, 11, 30), 10)), "the day of the month is kept");
		pager.Page.Unit.Should().Be(MinimapPageUnit.Month);
		pager.Page.FirstDay.Should().Be(new DateOnly(2025, 11, 1));
		CanExecute(pager.PreviousPageCommand).Should().BeFalse();
	}

	[AvaloniaTheory]
	[InlineData(27, 26)]
	[InlineData(28, 29)]
	public async Task ADayClickOnAWeekend_LandsOnTheDayWhoseRowsLieNearerItsNoon(
		int clickedDayOfDecember,
		int landingDayOfDecember)
	{
		using var stand = await WithWorkingDaysAsync();
		stand.Navigation.SetWindow(At(_tuesday, 10), At(_tuesday, 11));
		var landingDay = new DateOnly(2025, 12, landingDayOfDecember);

		stand.ViewModel.Pager.NavigateToArchiveDay(new DateOnly(2025, 12, clickedDayOfDecember));

		Window(stand).Should().Be((At(landingDay, 10), At(landingDay, 11)));
		stand.ViewModel.Pager.Page.FirstDay.Should().Be(landingDay);
	}

	[AvaloniaFact]
	public async Task ADayClickOnADayWithRows_KeepsTheClockOffsetOrMovesToTheFirstSample()
	{
		using var stand = await WithWorkingDaysAsync();
		stand.Navigation.SetWindow(At(_tuesday, 10), At(_tuesday, 11));

		stand.ViewModel.Pager.NavigateToArchiveDay(_thursday);
		Window(stand).Should().Be((At(_thursday, 10), At(_thursday, 11)));

		stand.Navigation.SetWindow(At(_tuesday, 20), At(_tuesday, 21));
		stand.ViewModel.Pager.NavigateToArchiveDay(_friday);
		Window(stand).Should().Be((At(_friday, 7), At(_friday, 8)));
	}

	[AvaloniaFact]
	public async Task ADetailClick_CentresTheWindowAtThePageFraction_KeepingItsStartOnThePage()
	{
		using var stand = await WithWorkingDaysAsync();
		var pager = stand.ViewModel.Pager;
		stand.Navigation.SetWindow(At(_monday, 8), At(_monday, 9));

		pager.NavigateToPageFraction(0.5);
		Window(stand).Should().Be((At(_monday, 11, 30), At(_monday, 12, 30)));

		pager.NavigateToPageFraction(0.0);
		Window(stand).Should().Be((At(_monday, 7), At(_monday, 8)));

		stand.Navigation.SetWindow(At(_tuesday, 0), At(_wednesday.AddDays(1), 0));
		pager.NavigateToPageFraction(0.0);

		Window(stand).Should().Be((At(_monday, 0), At(_wednesday, 0)));
		pager.Page.FirstDay.Should().Be(_monday);
	}

	[AvaloniaFact]
	public async Task WithoutSpans_ThePageStepsTakeCalendarDaysInsideTheExtent()
	{
		using var stand = await WithWorkingDaysAsync(failsSpans: true);
		var pager = stand.ViewModel.Pager;
		stand.Spans.Should().BeEmpty();
		stand.Navigation.SetWindow(At(_friday, 10), At(_friday, 11));

		pager.NextPageCommand.Execute().Subscribe();

		Window(stand).Should().Be((At(_saturday, 10), At(_saturday, 11)));
		pager.Page.Should().Be(new MinimapPage(
			MinimapPageUnit.Day, _saturday, DayHours.FullDay, At(_saturday, 0), At(_sunday, 0)));

		stand.Navigation.SetWindow(At(_thursday, 10), At(_thursday, 11));

		CanExecute(pager.PreviousPageCommand).Should().BeFalse("the day before the extent holds no sample");
		CanExecute(pager.NextPageCommand).Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task BeforeTheFirstSpansReadAnswers_NothingSteps_AndAFailedReadTurnsToCalendarSteps()
	{
		using var stand = new MinimapStand();
		var pager = stand.ViewModel.Pager;
		stand.Provider.GateDaySpans = true;
		stand.Navigation.TrackDataExtents(MinimapStand.ExtentFirst, MinimapStand.ExtentLast);
		await stand.LoadExtentAsync();
		stand.Navigation.SetWindow(At(_friday, 10), At(_friday, 11));

		pager.NavigateToArchiveDay(_saturday);

		Window(stand).Should()
			.Be((At(_friday, 10), At(_friday, 11)), "a calendar landing could pick a day with no rows");
		CanExecute(pager.NextPageCommand).Should().BeFalse();
		CanExecute(pager.PreviousPageCommand).Should().BeFalse();

		stand.Provider.DaySpansGate.SetResult(FailedSpansRead());
		stand.LandRead();

		CanExecute(pager.NextPageCommand).Should().BeTrue("a failed read leaves the calendar steps");
		CanExecute(pager.PreviousPageCommand).Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task TheLastPageWithRows_DisablesTheNextStep_AndNoExtentDisablesBoth()
	{
		using var stand = await WithWorkingDaysAsync();
		var pager = stand.ViewModel.Pager;
		stand.Navigation.SetWindow(At(_wednesday, 10), At(_wednesday, 11));

		CanExecute(pager.NextPageCommand).Should().BeFalse();
		CanExecute(pager.PreviousPageCommand).Should().BeTrue();

		using var empty = new MinimapStand();

		CanExecute(empty.ViewModel.Pager.NextPageCommand).Should().BeFalse();
		CanExecute(empty.ViewModel.Pager.PreviousPageCommand).Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task AStickyWindowPastTheLiveDaysEnd_KeepsTheDayPage_AndReadsTheBandOnlyWhenTheDayWidens()
	{
		var newestSample = At(_wednesday, 17, 50);
		using var stand = new MinimapStand();
		stand.ShowPens();
		stand.Provider.ArchiveDaySpans =
		[
			Span(_thursday), Span(_friday), Span(_monday), Span(_tuesday),
			new ArchiveDaySpan(_wednesday, At(_wednesday, 7), newestSample)
		];
		(stand.Provider.ArchiveFirstUtc, stand.Provider.ArchiveLastUtc) = (MinimapStand.ExtentFirst, newestSample);
		stand.Navigation.TrackDataExtents(MinimapStand.ExtentFirst, newestSample);
		await stand.LoadExtentAsync();
		stand.LandRead();
		stand.LandRead();
		var pager = stand.ViewModel.Pager;
		pager.Page.Should().Be(new MinimapPage(
			MinimapPageUnit.Day, _wednesday, new DayHours(7, 18), At(_wednesday, 7), At(_wednesday, 18)));
		var pages = new List<MinimapPage>();
		using var recording = pager.WhenAnyValue(model => model.Page).Skip(1).Subscribe(pages.Add);
		var bandReads = stand.Provider.BandHistoryQueries.Count;

		stand.Navigation.OnLiveEdge(At(_wednesday, 18, 10), newestSampleUtc: null);
		stand.LandRead();

		stand.Navigation.IsSticky.Should().BeTrue();
		stand.Navigation.To.Should().Be(At(_wednesday, 18, 10), "the poll moved the window past the day's 18:00 end");
		pages.Should().BeEmpty("no sample lies past 17:50, so the Day page still holds the window");
		stand.Provider.BandHistoryQueries.Should().HaveCount(bandReads, "a page that stays reads no band");

		stand.Navigation.OnLiveEdge(At(_wednesday, 18, 12), At(_wednesday, 18, 11));
		stand.LandRead();

		pages.Should().Equal(
			[new MinimapPage(
				MinimapPageUnit.Day, _wednesday, new DayHours(7, 19), At(_wednesday, 7), At(_wednesday, 19))],
			"the spans widen the day before the page reads the sample, so no Week page shows in between");
		stand.Provider.BandHistoryQueries.Should().HaveCount(bandReads + 1, "the widened day reads its band once");

		stand.Navigation.OnLiveEdge(At(_wednesday, 18, 14), At(_wednesday, 18, 13));
		stand.LandRead();

		pager.NewestSample.Should().Be(At(_wednesday, 18, 13));
		pages.Should().ContainSingle("a newer sample inside the widened day leaves the page as it was");
		stand.Provider.BandHistoryQueries.Should().HaveCount(bandReads + 1, "a page that stays reads no band");
	}

	private static async Task<MinimapStand> WithWorkingDaysAsync(bool failsSpans = false)
	{
		var stand = new MinimapStand();
		stand.Provider.ArchiveDaySpans =
			[Span(_thursday), Span(_friday), Span(_monday), Span(_tuesday), Span(_wednesday)];
		stand.Provider.FailDaySpans = failsSpans;

		// Moves the pan floor to the extent's first sample, so a landing is not clamped to today.
		stand.Navigation.TrackDataExtents(MinimapStand.ExtentFirst, MinimapStand.ExtentLast);
		await stand.LoadExtentAsync();
		stand.LandRead();

		return stand;
	}

	private static Result<IReadOnlyList<ArchiveDaySpan>> FailedSpansRead()
	{
		return Result.Fail<IReadOnlyList<ArchiveDaySpan>>(FakeDataProvider.UnreachableArchive());
	}

	private static bool CanExecute(ICommand command)
	{
		return command.CanExecute(null);
	}

	private static (DateTime From, DateTime To) Window(MinimapStand stand)
	{
		return (stand.Navigation.From, stand.Navigation.To);
	}

	private static ArchiveDaySpan Span(DateOnly day)
	{
		return new ArchiveDaySpan(day, At(day, 7), At(day, 17));
	}

	private static DateTime At(DateOnly day, int hour, int minute = 0)
	{
		return day.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc);
	}
}
