using System.ComponentModel;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Navigation;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.ChartTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TimeWindowPanelViewModelTests
{
	private static readonly DateTime _firstSample = From.AddDays(-5);
	private static readonly DateTime _firstDay = new(2026, 6, 10);
	private static readonly DateTime _windowDay = new(2026, 6, 15);

	[AvaloniaFact]
	public void Seed_FillsTheFormAndTheCalendarRangeFromTheWindowInForce()
	{
		var (chart, _, _, _) = OpenedStand();

		var panel = chart.TimeWindow;
		panel.Form.StartDate.Should().Be(_windowDay);
		panel.Form.StartTimeText.Should().Be("08:00:00");
		panel.Form.EndDate.Should().Be(_windowDay);
		panel.Form.EndTimeText.Should().Be("09:00:00");
		panel.Form.WidthText.Should().Be("1:00:00");
		panel.Days.FirstDay.Should().Be(_firstDay);
		panel.Days.LastDay.Should().Be(_windowDay);
	}

	[AvaloniaFact]
	public void Seed_WithAWindowStartingBeforeTheFirstSample_OffersTheStartDay()
	{
		var (chart, _, _, _) = CreateViewModelOnOneScheduler();
		var lastSample = new DateTime(2026, 6, 15, 0, 30, 0, DateTimeKind.Utc);
		chart.Navigation.TrackDataExtents(lastSample.AddMinutes(-20), lastSample);

		chart.TimeWindow.Seed();

		chart.TimeWindow.Form.StartDate.Should().Be(new DateTime(2026, 6, 14));
		chart.TimeWindow.Days.FirstDay.Should().Be(new DateTime(2026, 6, 14));
	}

	[AvaloniaFact]
	public void Seed_PutsTheNewestSampleOnItsLocalDay()
	{
		var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
		var (chart, scheduler, _, provider) = CreateViewModelOnOneScheduler(timeZone: berlin);
		var lastSample = new DateTime(2026, 6, 15, 22, 30, 0, DateTimeKind.Utc);
		chart.Navigation.TrackDataExtents(_firstSample, lastSample);

		chart.TimeWindow.Seed();
		chart.TimeWindow.Opened();
		scheduler.AdvanceBy(1);

		chart.TimeWindow.Days.LastDay.Should().Be(new DateTime(2026, 6, 16));
		provider.LastDaysRange.Should().Be((DateOnly.FromDateTime(_firstDay), new DateOnly(2026, 6, 16)));
	}

	[AvaloniaFact]
	public void Commit_WithTheFormUntouched_WritesNothing()
	{
		var (chart, _, _, _) = OpenedStand();
		var changes = chart.CountWindowChanges();

		chart.TimeWindow.CommitCommand.Execute().Subscribe();

		changes().Should().Be(0);
		chart.Navigation.IsSticky.Should().BeTrue();
	}

	[AvaloniaFact]
	public void Commit_UntouchedOverASubSecondWindow_WritesNothingAndKeepsSticky()
	{
		var (chart, _, _, _) = CreateViewModelOnOneScheduler();
		chart.Navigation.TrackDataExtents(_firstSample, To.AddMilliseconds(900));
		chart.TimeWindow.Seed();
		var fromBefore = chart.Navigation.From;
		var changes = chart.CountWindowChanges();

		chart.TimeWindow.CommitCommand.Execute().Subscribe();

		changes().Should().Be(0);
		chart.Navigation.IsSticky.Should().BeTrue();
		chart.Navigation.From.Should().Be(fromBefore);
	}

	[AvaloniaFact]
	public void Commit_WithANewStart_MovesTheLeftEdgeDropsStickyAndReseeds()
	{
		var (chart, _, _, _) = OpenedStand();

		chart.TimeWindow.Form.StartTimeText = "08:30:00";
		chart.TimeWindow.CommitCommand.Execute().Subscribe();

		chart.Navigation.From.Should().Be(From.AddMinutes(30));
		chart.Navigation.To.Should().Be(To);
		chart.Navigation.IsSticky.Should().BeFalse();
		chart.TimeWindow.Form.WidthText.Should().Be("0:30:00");
		chart.TimeWindow.Form.EdgesDifferFromSeed.Should().BeFalse();
	}

	[AvaloniaFact]
	public void Commit_WithANewEnd_MovesTheRightEdgeOnlyAndDropsSticky()
	{
		var (chart, _, _, _) = OpenedStand();

		chart.TimeWindow.Form.EndTimeText = "08:45:00";
		chart.TimeWindow.CommitCommand.Execute().Subscribe();

		chart.Navigation.From.Should().Be(From);
		chart.Navigation.To.Should().Be(From.AddMinutes(45));
		chart.Navigation.IsSticky.Should().BeFalse();
		chart.TimeWindow.Form.WidthText.Should().Be("0:45:00");
	}

	[AvaloniaFact]
	public void Commit_WithAnInvalidForm_WritesNothingAndKeepsTheText()
	{
		var (chart, _, _, _) = OpenedStand();
		var changes = chart.CountWindowChanges();

		chart.TimeWindow.Form.StartTimeText = "8:3";
		chart.TimeWindow.CommitCommand.Execute().Subscribe();

		changes().Should().Be(0);
		chart.TimeWindow.Form.StartTimeText.Should().Be("8:3");
	}

	[AvaloniaFact]
	public void Commit_WithANewWidth_KeepsTheRightEdgeAndSticky()
	{
		var (chart, _, _, _) = OpenedStand();

		chart.TimeWindow.Form.WidthText = "0:05:00";
		chart.TimeWindow.CommitCommand.Execute().Subscribe();

		chart.Navigation.To.Should().Be(To);
		chart.Navigation.From.Should().Be(To.AddMinutes(-5));
		chart.Navigation.IsSticky.Should().BeTrue();
		chart.TimeWindow.Form.StartTimeText.Should().Be("08:55:00");
	}

	[AvaloniaTheory]
	[InlineData("1:00:00")]
	[InlineData("0:60:00")]
	[InlineData("0:00:00")]
	public void Commit_WithAnUntouchedOrInvalidWidth_WritesNothing(string widthText)
	{
		var (chart, _, _, _) = OpenedStand();
		var changes = chart.CountWindowChanges();

		chart.TimeWindow.Form.WidthText = widthText;
		chart.TimeWindow.CommitCommand.Execute().Subscribe();

		changes().Should().Be(0);
	}

	[AvaloniaFact]
	public void Commit_WithANewStartAfterTheLiveEdgeMoved_KeepsTheLiveRightEdge()
	{
		var (chart, _, _, _) = OpenedStand();
		var liveEdge = To.AddSeconds(20.4);

		chart.TimeWindow.Form.StartTimeText = "08:30:00";
		chart.Navigation.OnLiveEdge(liveEdge);
		chart.TimeWindow.CommitCommand.Execute().Subscribe();

		chart.Navigation.From.Should().Be(From.AddMinutes(30));
		chart.Navigation.To.Should().Be(liveEdge, "the untouched edge is the one in force, not the seeded one");
	}

	[AvaloniaFact]
	public void Commit_WithANewEndAfterTheLiveEdgeMoved_KeepsTheLiveLeftEdge()
	{
		var (chart, _, _, _) = OpenedStand();
		var liveEdge = To.AddSeconds(20.4);

		chart.TimeWindow.Form.EndTimeText = "08:45:00";
		chart.Navigation.OnLiveEdge(liveEdge);
		chart.TimeWindow.CommitCommand.Execute().Subscribe();

		chart.Navigation.From.Should().Be(From.AddSeconds(20.4));
		chart.Navigation.To.Should().Be(From.AddMinutes(45));
	}

	[AvaloniaFact]
	public void Commit_WithANewEndBeforeTheMovedLeftEdge_WritesTheTypedPair()
	{
		var (chart, _, _, _) = OpenedStand();

		chart.TimeWindow.Form.EndTimeText = "08:00:10";
		chart.Navigation.OnLiveEdge(To.AddSeconds(20.4));
		chart.TimeWindow.CommitCommand.Execute().Subscribe();

		chart.Navigation.From.Should().Be(From, "the live left edge lies after the typed end, so the seeded one holds");
		chart.Navigation.To.Should().Be(From.AddSeconds(10));
	}

	[AvaloniaFact]
	public void Commit_WritesPendingEdgesThenThePendingWidthAsTheCloseDoes()
	{
		var (chart, _, _, _) = OpenedStand();

		chart.TimeWindow.Form.StartTimeText = "08:30:00";
		chart.TimeWindow.Form.WidthText = "0:10:00";
		chart.TimeWindow.CommitCommand.Execute().Subscribe();

		chart.Navigation.To.Should().Be(To);
		chart.Navigation.From.Should().Be(To.AddMinutes(-10), "Enter writes the typed width, not only the edges");
		chart.TimeWindow.Form.WidthText.Should().Be("0:10:00");
	}

	[AvaloniaFact]
	public void EachPreset_SetsItsExactWidthAndKeepsSticky()
	{
		var (chart, _, _, _) = OpenedStand();

		chart.TimeWindow.Presets.Select(preset => preset.Width).Should().Equal(
			TimeSpan.FromMinutes(1),
			TimeSpan.FromMinutes(2),
			TimeSpan.FromMinutes(3),
			TimeSpan.FromMinutes(5),
			TimeSpan.FromMinutes(10),
			TimeSpan.FromMinutes(30),
			TimeSpan.FromHours(1));

		foreach (var preset in chart.TimeWindow.Presets)
		{
			chart.TimeWindow.SetWidthCommand.Execute(preset.Width).Subscribe();

			(chart.Navigation.To - chart.Navigation.From).Should().Be(preset.Width);
			chart.Navigation.To.Should().Be(To);
			chart.Navigation.IsSticky.Should().BeTrue();
			chart.TimeWindow.Form.WidthDiffersFromSeed.Should().BeFalse();
		}
	}

	[AvaloniaFact]
	public void Close_CommitsPendingEdgesThenThePendingWidth()
	{
		var (chart, _, _, _) = OpenedStand();

		chart.TimeWindow.Form.StartTimeText = "08:30:00";
		chart.TimeWindow.Form.WidthText = "0:10:00";
		chart.TimeWindow.Close();

		chart.Navigation.To.Should().Be(To);
		chart.Navigation.From.Should().Be(To.AddMinutes(-10), "the width is written after the edges");
		chart.Navigation.IsSticky.Should().BeFalse();
	}

	[AvaloniaFact]
	public void Close_WithNothingPending_WritesNothing()
	{
		var (chart, _, _, _) = OpenedStand();
		var changes = chart.CountWindowChanges();

		chart.TimeWindow.Close();

		changes().Should().Be(0);
	}

	[AvaloniaFact]
	public void Cancel_RequestsACloseAndTheCloseCommitsNothing()
	{
		var (chart, _, _, _) = OpenedStand();
		var changes = chart.CountWindowChanges();
		var closes = 0;
		using var subscription = chart.TimeWindow.CloseRequests.Subscribe(_ => closes++);

		chart.TimeWindow.Form.StartTimeText = "08:30:00";
		chart.TimeWindow.Form.WidthText = "0:10:00";
		chart.TimeWindow.CancelCommand.Execute().Subscribe();
		chart.TimeWindow.Close();

		closes.Should().Be(1);
		changes().Should().Be(0);
	}

	[AvaloniaFact]
	public void TheNextOpenAfterACancel_CommitsOnCloseAgain()
	{
		var (chart, _, _, _) = OpenedStand();
		chart.TimeWindow.CancelCommand.Execute().Subscribe();
		chart.TimeWindow.Close();

		chart.TimeWindow.Seed();
		chart.TimeWindow.Opened();
		chart.TimeWindow.Form.WidthText = "0:05:00";
		chart.TimeWindow.Close();

		(chart.Navigation.To - chart.Navigation.From).Should().Be(TimeSpan.FromMinutes(5));
	}

	[AvaloniaFact]
	public void Opened_ReadsFromTheFirstDayToTheNewestSampleDay()
	{
		var (_, _, _, provider) = OpenedStand();

		provider.DaysQueryCount.Should().Be(1);
		provider.LastDaysRange.Should().Be((DateOnly.FromDateTime(_firstDay), DateOnly.FromDateTime(_windowDay)));
	}

	[AvaloniaFact]
	public void BlackoutDays_AreTheReadDaysWithoutARowAndNeverASelectedDate()
	{
		var (chart, _, _, _) = OpenedStand(
			daysWithRows: [new DateOnly(2026, 6, 9), new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12)]);

		chart.TimeWindow.Days.BlackoutDays.Should().Equal(
			new DateTime(2026, 6, 11),
			new DateTime(2026, 6, 13),
			new DateTime(2026, 6, 14));
	}

	[AvaloniaFact]
	public void BlackoutDays_LeaveOutTheSelectedDatesAndTheDaysAfterTheNewestSample()
	{
		var (chart, _, _, _) = OpenedStand(daysWithRows: [new DateOnly(2026, 6, 10)]);

		chart.TimeWindow.Form.StartDate = new DateTime(2026, 6, 11);
		chart.TimeWindow.Form.EndDate = new DateTime(2026, 6, 17);
		chart.TimeWindow.CommitCommand.Execute().Subscribe();

		chart.TimeWindow.Days.LastDay.Should().Be(new DateTime(2026, 6, 17));
		chart.TimeWindow.Days.BlackoutDays.Should().Equal(
			new DateTime(2026, 6, 12),
			new DateTime(2026, 6, 13),
			new DateTime(2026, 6, 14),
			new DateTime(2026, 6, 15));
	}

	[AvaloniaFact]
	public void TheDayRead_RunsOncePerOpen()
	{
		var (chart, _, _, provider) = OpenedStand();

		chart.TimeWindow.Form.WidthText = "0:05:00";
		chart.TimeWindow.CommitCommand.Execute().Subscribe();
		chart.TimeWindow.SetWidthCommand.Execute(TimeSpan.FromMinutes(10)).Subscribe();

		provider.DaysQueryCount.Should().Be(1);
	}

	[AvaloniaFact]
	public void AFailedDayRead_ReachesTheMessagePanelAndLeavesNoBlackoutDays()
	{
		var messagePanel = new MessagePanelViewModel();
		var (chart, _, _, _) = OpenedStand(messagePanel: messagePanel, failDays: true);

		messagePanel.Entries.Should().ContainSingle();
		chart.TimeWindow.Days.BlackoutDays.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void AThrowingDayRead_ReachesTheMessagePanelAndLeavesNoBlackoutDays()
	{
		var messagePanel = new MessagePanelViewModel();
		var (chart, _, _, _) = OpenedStand(
			messagePanel: messagePanel,
			daysReadException: new InvalidOperationException("day read defect"));

		messagePanel.Entries.Should().ContainSingle();
		chart.TimeWindow.Days.BlackoutDays.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void ADayReadWhoseApplyThrows_ReachesTheMessagePanel()
	{
		var messagePanel = new MessagePanelViewModel();
		var (chart, scheduler, _, provider) = CreateViewModelOnOneScheduler(messagePanel: messagePanel);
		chart.Navigation.TrackDataExtents(_firstSample, To);
		chart.TimeWindow.Seed();
		chart.TimeWindow.Opened();

		static void ThrowOnBlackoutDays(object? sender, PropertyChangedEventArgs args)
		{
			if (args.PropertyName == nameof(ArchiveDayCalendar.BlackoutDays))
			{
				throw new InvalidOperationException("blackout sync defect");
			}
		}

		chart.TimeWindow.Days.PropertyChanged += ThrowOnBlackoutDays;
		scheduler.AdvanceBy(1);
		chart.TimeWindow.Days.PropertyChanged -= ThrowOnBlackoutDays;

		provider.DaysQueryCount.Should().Be(1);
		messagePanel.Entries.Should().ContainSingle();
	}

	[AvaloniaFact]
	public void Opened_WithNoNewestSample_ReadsNothingAndOffersTheEndDay()
	{
		var (chart, scheduler, _, provider) = CreateViewModelOnOneScheduler();
		chart.Navigation.NewestSample.Should().BeNull();

		chart.TimeWindow.Seed();
		chart.TimeWindow.Opened();
		scheduler.AdvanceBy(1);

		provider.DaysQueryCount.Should().Be(0);
		chart.TimeWindow.Days.LastDay.Should().Be(chart.Navigation.To.Date);
		chart.TimeWindow.Days.BlackoutDays.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void Close_DropsADayResultAlreadyQueuedOnTheUiScheduler()
	{
		var (chart, scheduler, _, provider) = CreateViewModelOnOneScheduler();
		chart.Navigation.TrackDataExtents(_firstSample, To);
		provider.ArchiveDays = [DateOnly.FromDateTime(_firstDay)];
		chart.TimeWindow.Seed();
		chart.TimeWindow.Opened();
		provider.DaysQueryCount.Should().Be(1);

		chart.TimeWindow.Close();
		scheduler.AdvanceBy(1);
		chart.TimeWindow.Seed();

		chart.TimeWindow.Days.BlackoutDays.Should().BeEmpty("the closed open's read never lands");
	}

	[AvaloniaFact]
	public void Close_CancelsAPendingReadAndTheNextOpenReadsAgain()
	{
		var (chart, scheduler, _, provider) = CreateViewModelOnOneScheduler();
		chart.Navigation.TrackDataExtents(_firstSample, To);
		provider.GateDays = true;
		chart.TimeWindow.Seed();
		chart.TimeWindow.Opened();

		chart.TimeWindow.Close();

		provider.LastDaysCancellationToken.IsCancellationRequested.Should().BeTrue();

		provider.GateDays = false;
		provider.ArchiveDays = [DateOnly.FromDateTime(_firstDay)];
		chart.TimeWindow.Seed();
		chart.TimeWindow.Opened();
		scheduler.AdvanceBy(1);

		provider.DaysQueryCount.Should().Be(2);
		chart.TimeWindow.Days.BlackoutDays.Should().NotBeEmpty();
	}

	[AvaloniaFact]
	public void Close_ForgetsTheDayRead()
	{
		var (chart, _, _, _) = OpenedStand(daysWithRows: [DateOnly.FromDateTime(_firstDay)]);

		chart.TimeWindow.Close();
		chart.TimeWindow.Seed();

		chart.TimeWindow.Days.BlackoutDays.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void Seed_RaisesBlackoutDaysEmptyBeforeItWritesTheDates()
	{
		var (chart, _, _, _) = OpenedStand(daysWithRows: [DateOnly.FromDateTime(_firstDay)]);
		var panel = chart.TimeWindow;
		panel.Days.BlackoutDays.Should().Contain(new DateTime(2026, 6, 12));
		chart.Navigation.SetWindow(new DateTime(2026, 6, 12, 8, 0, 0, DateTimeKind.Utc), To);
		var raised = new List<string>();

		void OnPanelChanged(object? sender, PropertyChangedEventArgs args)
		{
			if (args.PropertyName == nameof(panel.Days.BlackoutDays))
			{
				raised.Add($"{nameof(panel.Days.BlackoutDays)}:{panel.Days.BlackoutDays.Count}");
			}
		}

		void OnFormChanged(object? sender, PropertyChangedEventArgs args)
		{
			if (args.PropertyName is nameof(panel.Form.StartDate) or nameof(panel.Form.EndDate))
			{
				raised.Add(args.PropertyName);
			}
		}

		panel.Days.PropertyChanged += OnPanelChanged;
		panel.Form.PropertyChanged += OnFormChanged;

		panel.Seed();

		raised[0].Should().Be($"{nameof(panel.Days.BlackoutDays)}:0");
		raised.Should().Contain(nameof(panel.Form.StartDate));
		panel.Days.BlackoutDays.Should().NotContain(new DateTime(2026, 6, 12));
		panel.Days.BlackoutDays.Should().NotBeEmpty();
	}

	// The window [08:00, 09:00] UTC of 2026-06-15, sticky, first sample five days earlier; seeded, opened, and
	// its day read landed.
	private static (TrendChartViewModel Chart, TestScheduler Scheduler, MessagePanelViewModel MessagePanel,
		FakeDataProvider Provider) OpenedStand(
		IReadOnlyList<DateOnly>? daysWithRows = null,
		MessagePanelViewModel? messagePanel = null,
		bool failDays = false,
		Exception? daysReadException = null)
	{
		messagePanel ??= new MessagePanelViewModel();
		var (chart, scheduler, _, provider) = CreateViewModelOnOneScheduler(messagePanel: messagePanel);
		provider.ArchiveDays = daysWithRows ?? [];
		provider.FailDays = failDays;
		provider.DaysReadException = daysReadException;
		chart.Navigation.TrackDataExtents(_firstSample, To);

		chart.TimeWindow.Seed();
		chart.TimeWindow.Opened();
		scheduler.AdvanceBy(1);

		return (chart, scheduler, messagePanel, provider);
	}
}
