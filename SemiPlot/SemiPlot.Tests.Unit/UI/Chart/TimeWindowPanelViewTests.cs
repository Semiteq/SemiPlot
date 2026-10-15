using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Navigation;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.ChartTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Chart;

// The time window panel through Avalonia's real pipeline, hosted in a flyout the way the chart hosts it: the
// operator's clicks, typed text and keys are the only input.
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
[Collection(ProcessGlobalStateCollection.Name)]
public sealed class TimeWindowPanelViewTests
{
	private static readonly DateTime _emptyDay = new(2026, 6, 12);
	private static readonly DateTime _emptyDayMorning = new(2026, 6, 12, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _windowDay = new(2026, 6, 15);

	// Every day the stand's calendar offers but the 12th and the 14th holds a row.
	private static readonly DateOnly[] _daysWithRows =
	[
		new(2026, 6, 10),
		new(2026, 6, 11),
		new(2026, 6, 13),
		new(2026, 6, 15)
	];

	[AvaloniaFact]
	public void TypingAStartTimeAndEnter_MovesTheLeftEdgeOnly()
	{
		using var stand = PanelStand.Create();
		stand.Open();

		stand.TypeInto("TimeWindowStartTime", "08:30:00");
		HeadlessInput.Press(stand.Window, PhysicalKey.Enter);

		stand.Chart.Navigation.From.Should().Be(From.AddMinutes(30));
		stand.Chart.Navigation.To.Should().Be(To);
		stand.Chart.Navigation.IsSticky.Should().BeFalse("typed edges leave the live edge");
		stand.Flyout.IsOpen.Should().BeTrue("Enter writes the pair and keeps the panel open");
	}

	[AvaloniaFact]
	public void TypingADateAndEnter_MovesTheLeftEdgeToThatDate()
	{
		using var stand = PanelStand.Create(_daysWithRows);
		stand.Open();

		stand.TypeDate("TimeWindowStartDate", new DateTime(2026, 6, 13));
		HeadlessInput.Press(stand.Window, PhysicalKey.Enter);

		stand.Chart.Navigation.From.Should().Be(
			new DateTime(2026, 6, 13, 8, 0, 0, DateTimeKind.Utc),
			"the picker parses the typed date before the panel commits the pair");
		stand.Chart.Navigation.To.Should().Be(To);
		stand.Flyout.IsOpen.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TypingAnEndTimeAndEnter_MovesTheRightEdgeOnly()
	{
		using var stand = PanelStand.Create();
		stand.Open();

		stand.TypeInto("TimeWindowEndTime", "08:45:00");
		HeadlessInput.Press(stand.Window, PhysicalKey.Enter);

		stand.Chart.Navigation.From.Should().Be(From);
		stand.Chart.Navigation.To.Should().Be(From.AddMinutes(45));
		stand.Chart.Navigation.IsSticky.Should().BeFalse();
		stand.Flyout.IsOpen.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TypingAWidthAndEnter_SetsTheWidthAndKeepsSticky()
	{
		using var stand = PanelStand.Create();
		stand.Open();

		stand.TypeInto("TimeWindowWidth", "0:20:00");
		HeadlessInput.Press(stand.Window, PhysicalKey.Enter);

		stand.Chart.Navigation.From.Should().Be(To.AddMinutes(-20));
		stand.Chart.Navigation.To.Should().Be(To);
		stand.Chart.Navigation.IsSticky.Should().BeTrue("a width keeps the right edge");
		stand.Panel.FindControl<TextBox>("TimeWindowStartTime")!.Text.Should().Be("08:40:00");
		stand.Flyout.IsOpen.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TypingAnEndDateAfterTheLastDayAndEnter_MovesTheRightEdgeToThatDate()
	{
		using var stand = PanelStand.Create(_daysWithRows);
		stand.Open();
		stand.Chart.TimeWindow.LastDay.Should().Be(_windowDay);
		var laterDay = _windowDay.AddDays(2);

		stand.TypeDate("TimeWindowEndDate", laterDay);
		HeadlessInput.Press(stand.Window, PhysicalKey.Enter);

		stand.Chart.Navigation.From.Should().Be(From);
		stand.Chart.Navigation.To.Should().Be(DateTime.SpecifyKind(laterDay.AddHours(9), DateTimeKind.Utc));
		stand.Chart.TimeWindow.LastDay.Should().Be(laterDay, "the re-seed offers the selected end day");
		stand.Picker("TimeWindowEndDate").SelectedDate.Should().Be(laterDay);
		stand.MessagePanel.Entries.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void TypingADateAndClickingOutside_MovesTheLeftEdgeToThatDate()
	{
		using var stand = PanelStand.Create(_daysWithRows);
		stand.Open();

		stand.TypeDate("TimeWindowStartDate", new DateTime(2026, 6, 13));
		stand.ClickOutsideThePanel();

		stand.Flyout.IsOpen.Should().BeFalse();
		stand.Chart.Navigation.From.Should().Be(
			new DateTime(2026, 6, 13, 8, 0, 0, DateTimeKind.Utc),
			"a focused picker parses its text before the panel's close commits the pair");
		stand.Chart.Navigation.To.Should().Be(To);
	}

	[AvaloniaFact]
	public void OpeningTheStartCalendar_WritesNothing()
	{
		using var stand = PanelStand.Create(_daysWithRows);
		stand.Open();
		var changes = stand.Chart.CountWindowChanges();

		stand.Picker("TimeWindowStartDate").IsDropDownOpen = true;
		Dispatcher.UIThread.RunJobs();

		changes().Should().Be(0, "focus moving into the calendar commits nothing");
		stand.Chart.Navigation.IsSticky.Should().BeTrue();
		stand.Flyout.IsOpen.Should().BeTrue();
	}

	[AvaloniaFact]
	public void PickingADateInTheCalendar_WritesNothingAndKeepsThePanelOpen()
	{
		using var stand = PanelStand.Create(_daysWithRows);
		stand.Open();
		var changes = stand.Chart.CountWindowChanges();
		var picker = stand.Picker("TimeWindowStartDate");
		var day = new DateTime(2026, 6, 13);

		picker.IsDropDownOpen = true;
		Dispatcher.UIThread.RunJobs();
		var dayButton = DayButton(picker, day);
		HeadlessInput.Click(TopLevel.GetTopLevel(dayButton)!, dayButton);

		picker.SelectedDate.Should().Be(day);
		stand.Chart.TimeWindow.Form.StartDate.Should().Be(day);
		changes().Should().Be(0, "a picked date waits for Enter or the close");
		stand.Flyout.IsOpen.Should().BeTrue();
	}

	[AvaloniaFact]
	public void PickingTheFiveMinutePreset_SetsTheWidthAndKeepsSticky()
	{
		using var stand = PanelStand.Create();
		stand.Open();
		var presets = stand.Panel.FindControl<DropDownButton>("TimeWindowPresets")!;

		presets.Content.Should().Be(Resources.TimeWindowPresets);
		HeadlessInput.Click(stand.Window, presets);
		var item = ((MenuFlyout)presets.Flyout!).Popup.Child!
			.GetVisualDescendants()
			.OfType<MenuItem>()
			.Single(menuItem => Equals(menuItem.Header, Resources.TimeWindowPresetFiveMinutes));
		HeadlessInput.Click(TopLevel.GetTopLevel(item)!, item);

		(stand.Chart.Navigation.To - stand.Chart.Navigation.From).Should().Be(TimeSpan.FromMinutes(5));
		stand.Chart.Navigation.To.Should().Be(To);
		stand.Chart.Navigation.IsSticky.Should().BeTrue("a width keeps the right edge");
		stand.Panel.FindControl<TextBox>("TimeWindowWidth")!.Text.Should().Be("0:05:00");
		stand.Flyout.IsOpen.Should().BeTrue();
	}

	[AvaloniaFact]
	public void Escape_ClosesThePanelAndWritesNothing()
	{
		using var stand = PanelStand.Create();
		stand.Open();
		var changes = stand.Chart.CountWindowChanges();

		stand.TypeInto("TimeWindowStartTime", "08:30:00");
		HeadlessInput.Press(stand.Window, PhysicalKey.Escape);

		stand.Flyout.IsOpen.Should().BeFalse();
		changes().Should().Be(0);
		stand.Chart.Navigation.From.Should().Be(From);
	}

	[AvaloniaFact]
	public void AClickOutsideThePanel_WritesThePendingPairOnceAndClosesIt()
	{
		using var stand = PanelStand.Create();
		stand.Open();
		var changes = stand.Chart.CountWindowChanges();

		stand.TypeInto("TimeWindowStartTime", "08:30:00");
		stand.ClickOutsideThePanel();

		stand.Flyout.IsOpen.Should().BeFalse("a click outside dismisses the panel");
		changes().Should().Be(1);
		stand.Chart.Navigation.From.Should().Be(From.AddMinutes(30));
		stand.Chart.Navigation.IsSticky.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ADayWithoutRows_IsABlackoutDateOfThePicker()
	{
		using var stand = PanelStand.Create(_daysWithRows);
		stand.Open();

		foreach (var name in new[] { "TimeWindowStartDate", "TimeWindowEndDate" })
		{
			var blackoutDates = stand.Picker(name).BlackoutDates!;
			blackoutDates.Contains(_emptyDay).Should().BeTrue();
			blackoutDates.Contains(new DateTime(2026, 6, 14)).Should().BeTrue();
			blackoutDates.Contains(new DateTime(2026, 6, 13)).Should().BeFalse("the 13th holds a row");
			blackoutDates.Contains(_windowDay).Should().BeFalse("the selected date is never a blackout date");
		}
	}

	[AvaloniaFact]
	public void AWindowStartingOnAnEmptyDay_SeedsWithoutThrowing()
	{
		using var stand = PanelStand.Create(_daysWithRows);
		stand.Chart.Navigation.SetWindow(_emptyDayMorning, To);

		stand.Open();

		var startPicker = stand.Picker("TimeWindowStartDate");
		startPicker.SelectedDate.Should().Be(_emptyDay);
		startPicker.BlackoutDates!.Contains(_emptyDay).Should().BeFalse("the selected date is never a blackout date");
		startPicker.BlackoutDates.Contains(new DateTime(2026, 6, 14)).Should().BeTrue();
		stand.MessagePanel.Entries.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void AReopenOntoABlackoutDay_SeedsWithoutThrowing()
	{
		using var stand = PanelStand.Create(_daysWithRows);
		stand.Open();
		var startPicker = stand.Picker("TimeWindowStartDate");
		startPicker.BlackoutDates!.Contains(_emptyDay).Should().BeTrue("the first open's read landed");
		stand.ClickOutsideThePanel();
		stand.Flyout.IsOpen.Should().BeFalse();

		stand.Chart.Navigation.SetWindow(_emptyDayMorning, To);
		stand.Open();

		startPicker.SelectedDate.Should().Be(_emptyDay);
		startPicker.BlackoutDates.Contains(_emptyDay).Should().BeFalse();
		startPicker.BlackoutDates.Contains(new DateTime(2026, 6, 14)).Should().BeTrue("the reopen read landed");
		stand.Provider.DaysQueryCount.Should().Be(2);
		stand.MessagePanel.Entries.Should().BeEmpty();
	}

	[AvaloniaTheory]
	[InlineData("en-US")]
	[InlineData("ru-RU")]
	public void TheMessageLine_KeepsThePanelsSize_EmptyAndWithTheLongestRule(string cultureName)
	{
		using var culture = new CultureScope(cultureName);
		var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
		// The window ends at 08:00 local time on the day the clocks move forward.
		using var stand = PanelStand.Create(
			timeZone: berlin,
			lastSample: new DateTime(2026, 3, 29, 6, 0, 0, DateTimeKind.Utc));
		stand.Open();
		var message = stand.Panel.FindControl<TextBlock>("TimeWindowMessage")!;
		message.Text.Should().BeEmpty();
		var emptySizes = stand.Sizes(message);

		stand.TypeInto("TimeWindowStartTime", "8:3");
		message.Text.Should().Be(Resources.FormatTimeWindowMessageStartUnreadable(WallClockRangeForm.TimeFieldFormat));
		var unreadableSizes = stand.Sizes(message);

		stand.TypeInto("TimeWindowStartTime", "02:30:00");
		message.Text.Should().Be(Resources.TimeWindowMessageTimeSkipped);
		stand.Panel.FindControl<TextBox>("TimeWindowStartTime")!.Classes.Should().Contain("invalid");
		var skippedSizes = stand.Sizes(message);

		unreadableSizes.Should().Be(emptySizes);
		skippedSizes.Should().Be(emptySizes);
	}

	private static CalendarDayButton DayButton(CalendarDatePicker picker, DateTime day)
	{
		var popup = ViewProbe.Descendant<Popup>(picker, "PART_Popup");
		var calendar = ViewProbe.Descendant<Control>(popup.Child!, "PART_Calendar");

		return calendar.GetVisualDescendants()
			.OfType<CalendarDayButton>()
			.First(button => button.DataContext is DateTime buttonDay && buttonDay == day);
	}

	/// <summary>A chart view model whose time window panel a test window shows in a flyout.</summary>
	private sealed class PanelStand : IDisposable
	{
		private readonly IDisposable _closeRequests;
		private readonly Control _anchor;
		private readonly TestScheduler _scheduler;

		private PanelStand(
			TrendChartViewModel chart,
			TestScheduler scheduler,
			FakeDataProvider provider,
			MessagePanelViewModel messagePanel)
		{
			Chart = chart;
			_scheduler = scheduler;
			Provider = provider;
			MessagePanel = messagePanel;
			Panel = new TimeWindowPanel
			{
				DataContext = chart.TimeWindow
			};
			Flyout = new Flyout
			{
				Content = Panel,
				Placement = PlacementMode.BottomEdgeAlignedLeft
			};
			_anchor = new Border
			{
				Width = 10,
				Height = 10,
				Margin = new Thickness(20),
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top
			};
			Window = ViewProbe.Realise(new Window
			{
				Width = 900,
				Height = 600,
				Content = _anchor
			});
			_closeRequests = chart.TimeWindow.CloseRequests.Subscribe(_ => Flyout.Hide());
		}

		public TrendChartViewModel Chart { get; }

		public FakeDataProvider Provider { get; }

		public MessagePanelViewModel MessagePanel { get; }

		public TimeWindowPanel Panel { get; }

		public Flyout Flyout { get; }

		public Window Window { get; }

		// A sticky one-hour window ending at the last sample, the first sample five days earlier.
		public static PanelStand Create(
			IReadOnlyList<DateOnly>? daysWithRows = null,
			TimeZoneInfo? timeZone = null,
			DateTime? lastSample = null)
		{
			var messagePanel = new MessagePanelViewModel();
			var (chart, scheduler, _, provider) = CreateViewModelOnOneScheduler(
				messagePanel: messagePanel,
				timeZone: timeZone);
			provider.ArchiveDays = daysWithRows ?? [];
			var last = lastSample ?? To;
			chart.Navigation.TrackDataExtents(last.AddDays(-5), last);

			return new PanelStand(chart, scheduler, provider, messagePanel);
		}

		/// <summary>Seeds and shows the panel, lets the posted Loaded run, and lands the day read.</summary>
		public void Open()
		{
			Chart.TimeWindow.Seed();
			Flyout.ShowAt(_anchor);
			Dispatcher.UIThread.RunJobs();
			_scheduler.AdvanceBy(1);
			Flyout.IsOpen.Should().BeTrue();
		}

		public CalendarDatePicker Picker(string name)
		{
			return Panel.FindControl<CalendarDatePicker>(name)!;
		}

		public void TypeInto(string fieldName, string text)
		{
			var field = Panel.FindControl<TextBox>(fieldName)!;

			HeadlessInput.Clear(Window, field);
			HeadlessInput.Type(Window, field, text);
		}

		/// <summary>Types the date into the picker's own text box, in the format the picker shows.</summary>
		public void TypeDate(string pickerName, DateTime day)
		{
			var field = ViewProbe.Descendant<TextBox>(Picker(pickerName), "PART_TextBox");

			HeadlessInput.Clear(Window, field);
			HeadlessInput.Type(Window, field, day.ToString(Resources.ExportDateFormat, CultureInfo.InvariantCulture));
		}

		public void ClickOutsideThePanel()
		{
			HeadlessInput.Click(Window, Window, new Point(Window.Bounds.Width - 20.0, Window.Bounds.Height - 20.0));
		}

		public (Size Panel, Size Message, Size Presenter) Sizes(TextBlock message)
		{
			var presenter = Panel.GetVisualAncestors().OfType<FlyoutPresenter>().First();

			return (Panel.Bounds.Size, message.Bounds.Size, presenter.Bounds.Size);
		}

		public void Dispose()
		{
			Flyout.Hide();
			Dispatcher.UIThread.RunJobs();
			_closeRequests.Dispose();
			Window.Close();
			Chart.Dispose();
		}
	}
}
