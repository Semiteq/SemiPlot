using System.Globalization;
using System.Reactive.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Reactive.Testing;

using ReactiveUI.Avalonia;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.UI;
using SemiPlot.UI.Export;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Startup;

using SkiaSharp;

using Xunit;

using static SemiPlot.Tests.Unit.UI.ViewProbe;

namespace SemiPlot.Tests.Unit.UI.Export;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class CsvExportWindowTests : IDisposable
{
	private const string InvalidClass = "invalid";
	private const string ErrorClass = "error";

	private static readonly DateTime _startDay = new(2026, 10, 13);
	private static readonly DateTime _newestSampleUtc = new(2026, 10, 15, 5, 0, 0, DateTimeKind.Utc);

	// The sample days run from the 8th to the 15th; the 10th, the 12th, the 13th and the 14th hold no row.
	private static readonly DateOnly[] _daysWithRows = [new(2026, 10, 8), new(2026, 10, 9), new(2026, 10, 11), new(2026, 10, 15)];
	private static readonly DateTime[] _emptyDays = [new(2026, 10, 10), new(2026, 10, 12), new(2026, 10, 14)];

	private readonly TestScheduler _scheduler = new();
	private readonly MessagePanelViewModel _messagePanel = new();

	public void Dispose()
	{
		_messagePanel.Dispose();
	}

	[AvaloniaFact]
	public void TheWindow_KeepsItsSizeAndItsButtonsWhenAFieldTurnsInvalid()
	{
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var saveAs = Named<Button>(window, "ExportSaveAs");
			var validBounds = window.Bounds;
			var validSaveBounds = saveAs.Bounds;

			HeadlessInput.Clear(window, Named<TextBox>(window, "ExportStartTime"));
			HeadlessInput.Type(window, Named<TextBox>(window, "ExportStartTime"), "tomorrow");

			var message = Named<TextBlock>(window, "ExportMessage");

			message.Text.Should().Be(Resources.FormatExportMessageStartUnreadable("HH:mm:ss"));
			window.Bounds.Should().Be(validBounds, "a message never resizes the form");
			saveAs.Bounds.Should().Be(validSaveBounds, "a message never moves the buttons");
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void TheInvalidControls_CarryTheInvalidClass_AndTheOthersDoNot()
	{
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var startDate = Named<CalendarDatePicker>(window, "ExportStartDate");
			var startTime = Named<TextBox>(window, "ExportStartTime");
			var endDate = Named<CalendarDatePicker>(window, "ExportEndDate");
			var endTime = Named<TextBox>(window, "ExportEndTime");
			Control[] all = [startDate, startTime, endDate, endTime];
			all.Should().OnlyContain(control => !control.Classes.Contains(InvalidClass));

			SetDate(window, endDate, _startDay.AddDays(1));

			endDate.Classes.Should().Contain(InvalidClass, "the range is longer than 24 hours");
			endTime.Classes.Should().Contain(InvalidClass, "either end control can be the cause");
			startDate.Classes.Should().NotContain(InvalidClass);
			startTime.Classes.Should().NotContain(InvalidClass);
			Named<TextBlock>(window, "ExportMessage").Text.Should()
				.Be(Resources.FormatExportMessageSpanOverLimit(24));
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void AnUnreadableEndTime_ShowsTheTimeFormatMessage_AndMarksTheEndTimeBoxOnly()
	{
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var endTime = Named<TextBox>(window, "ExportEndTime");
			var validBounds = window.Bounds;

			HeadlessInput.Clear(window, endTime);
			HeadlessInput.Type(window, endTime, "25:00:00");

			Named<TextBlock>(window, "ExportMessage").Text.Should()
				.Be(Resources.FormatExportMessageEndUnreadable("HH:mm:ss"));
			endTime.Classes.Should().Contain(InvalidClass);
			Named<CalendarDatePicker>(window, "ExportEndDate").Classes.Should().NotContain(InvalidClass);
			window.Bounds.Should().Be(validBounds);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void AnEndTimeBeforeTheStartTime_ShowsTheEndNotAfterStartMessage()
	{
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var endTime = Named<TextBox>(window, "ExportEndTime");

			HeadlessInput.Clear(window, endTime);
			HeadlessInput.Type(window, endTime, "07:00:00");

			Named<TextBlock>(window, "ExportMessage").Text.Should().Be(Resources.ExportMessageEndNotAfterStart);
			Named<Button>(window, "ExportSaveAs").IsEffectivelyEnabled.Should().BeFalse();
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void TheSaveAsButton_IsDisabledWhileInvalid_AndAValidRangeEnablesIt()
	{
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var saveAs = Named<Button>(window, "ExportSaveAs");
			var endDate = Named<CalendarDatePicker>(window, "ExportEndDate");
			saveAs.IsEffectivelyEnabled.Should().BeTrue();

			SetDate(window, endDate, _startDay.AddDays(1));

			saveAs.IsEffectivelyEnabled.Should().BeFalse("a range over 24 hours cannot be exported");

			SetDate(window, endDate, _startDay);

			saveAs.IsEffectivelyEnabled.Should().BeTrue();
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaTheory]
	[InlineData("ru")]
	[InlineData("en")]
	public void AClearedStartDate_ShowsTheMissingDateMessage_MarksThePicker_AndKeepsTheBounds(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var saveAs = Named<Button>(window, "ExportSaveAs");
			var startDate = Named<CalendarDatePicker>(window, "ExportStartDate");
			var validBounds = window.Bounds;
			var validSaveBounds = saveAs.Bounds;

			HeadlessInput.Clear(window, TextOf(startDate));
			HeadlessInput.Press(window, PhysicalKey.Enter);

			startDate.SelectedDate.Should().BeNull();
			Named<TextBlock>(window, "ExportMessage").Text.Should().Be(Resources.ExportMessageStartDateMissing);
			startDate.Classes.Should().Contain(InvalidClass);
			Named<TextBox>(window, "ExportStartTime").Classes.Should().NotContain(InvalidClass);
			saveAs.IsEffectivelyEnabled.Should().BeFalse();
			window.Bounds.Should().Be(validBounds);
			saveAs.Bounds.Should().Be(validSaveBounds);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaTheory]
	[InlineData("ru")]
	[InlineData("en")]
	public void AClearedEndDate_ShowsTheMissingDateMessage_AndMarksTheEndPicker(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var endDate = Named<CalendarDatePicker>(window, "ExportEndDate");

			HeadlessInput.Clear(window, TextOf(endDate));
			HeadlessInput.Press(window, PhysicalKey.Enter);

			Named<TextBlock>(window, "ExportMessage").Text.Should().Be(Resources.ExportMessageEndDateMissing);
			endDate.Classes.Should().Contain(InvalidClass);
			Named<CalendarDatePicker>(window, "ExportStartDate").Classes.Should().NotContain(InvalidClass);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaTheory]
	[InlineData("ru")]
	[InlineData("en")]
	public void AnUnparseableDate_RestoresThePrefilledDate_AndLeavesTheFormValid(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var startDate = Named<CalendarDatePicker>(window, "ExportStartDate");
			var box = TextOf(startDate);

			HeadlessInput.Clear(window, box);
			HeadlessInput.Type(window, box, "99999999");
			HeadlessInput.Press(window, PhysicalKey.Enter);

			box.Text.Should().Be(DateText(_startDay));
			startDate.SelectedDate.Should().Be(_startDay);
			viewModel.Form.IsValid.Should().BeTrue();
			startDate.Classes.Should().NotContain(InvalidClass);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void ADayWithoutRows_IsABlackoutDateOfBothPickers_WithinTheSampleDays()
	{
		using var viewModel = BuildOverArchiveDays([.. _daysWithRows, DateOnly.FromDateTime(_startDay)]);
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			_scheduler.AdvanceBy(1);

			foreach (var name in new[] { "ExportStartDate", "ExportEndDate" })
			{
				var picker = Named<CalendarDatePicker>(window, name);
				picker.BlackoutDates!.Select(range => range.Start).Should().Equal(_emptyDays);
				picker.DisplayDateStart.Should().Be(new DateTime(2026, 10, 8));
				picker.DisplayDateEnd.Should().Be(new DateTime(2026, 10, 15));
			}

			_messagePanel.Entries.Should().BeEmpty();
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void AVisibleRangeOnADayWithoutRows_KeepsThatDaySelectable()
	{
		using var viewModel = BuildOverArchiveDays(_daysWithRows);
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			_scheduler.AdvanceBy(1);

			var startDate = Named<CalendarDatePicker>(window, "ExportStartDate");
			startDate.SelectedDate.Should().Be(_startDay);
			startDate.BlackoutDates!.Contains(_startDay).Should().BeFalse("the selected date is never a blackout date");
			startDate.BlackoutDates.Select(range => range.Start).Should().Equal(_emptyDays);
			_messagePanel.Entries.Should().BeEmpty();
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void AFailedDayRead_ReachesTheMessagePanelAndLeavesNoBlackoutDates()
	{
		using var viewModel = CsvExportStand.Build(
			new FakeArchiveRowReader(),
			_messagePanel,
			_scheduler,
			[new ExportPenChoice(CsvExportStand.Pressure, true)],
			_newestSampleUtc,
			(_, _, _) => Task.FromResult(Result.Fail<IReadOnlyList<DateOnly>>(new ArchiveError(ArchiveFault.Unreachable, "scada-host", 5432, "semiplot"))));
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			_scheduler.AdvanceBy(1);

			Named<CalendarDatePicker>(window, "ExportStartDate").BlackoutDates.Should().BeEmpty();
			_messagePanel.Entries.Should().ContainSingle();
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaTheory]
	[InlineData("ru")]
	[InlineData("en")]
	public void ThePrefilledControls_ShowTheirDateInTheInterfaceLanguagesFormat_AndTheirTimeAsClockTime(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			TextOf(Named<CalendarDatePicker>(window, "ExportStartDate")).Text.Should().Be(DateText(_startDay));
			TextOf(Named<CalendarDatePicker>(window, "ExportEndDate")).Text.Should().Be(DateText(_startDay));
			Named<TextBox>(window, "ExportStartTime").Text.Should().Be("08:00:00");
			Named<TextBox>(window, "ExportEndTime").Text.Should().Be("09:00:00");
		}
		finally
		{
			Dismiss(window);
		}
	}

	// Semi paints the focused border from its own style: docs/architecture/ui-theme.md#a-form-never-resizes-on-validation
	[AvaloniaFact]
	public void AnInvalidPicker_PaintsItsBorderWithTheErrorBrushFocusedOrNot()
	{
		var variant = App.VariantFor(AppThemeVariant.Light);
		using var scope = ThemeProbe.ApplyVariant(variant);
		var error = ThemeProbe.Colour("AppSeverityErrorBrush", variant);
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var startDate = Named<CalendarDatePicker>(window, "ExportStartDate");
			var endDate = Named<CalendarDatePicker>(window, "ExportEndDate");
			var startBox = TextOf(startDate);
			BorderColourOf(startDate).Should().NotBe(error);

			HeadlessInput.Clear(window, startBox);
			HeadlessInput.Press(window, PhysicalKey.Enter);
			HeadlessInput.Click(window, startBox);

			startBox.IsFocused.Should().BeTrue();
			BorderColourOf(startDate).Should().Be(error, "the invalid border wins over Semi's focus border");

			HeadlessInput.Click(window, Named<TextBox>(window, "ExportEndTime"));

			startBox.IsFocused.Should().BeFalse();
			BorderColourOf(startDate).Should().Be(error, "the invalid border wins over Semi's resting border");
			BorderColourOf(endDate).Should().NotBe(error);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void ThePenList_GrowsWithItsRowsUpToItsMaxHeight()
	{
		using var viewModel = BuildWithPens(12);
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var list = Named<ListBox>(window, "ExportPens");

			list.Bounds.Height.Should().Be(list.MaxHeight);
			list.BorderThickness.Should().Be(new Thickness(1));
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void ThePenList_MaxHeight_ShowsExactlyEightRowsOf28Pixels()
	{
		using var viewModel = BuildWithPens(12);
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var list = Named<ListBox>(window, "ExportPens");
			var rowHeight = list.GetVisualDescendants().OfType<ListBoxItem>().First().Bounds.Height;
			var innerHeight = list.MaxHeight - list.BorderThickness.Top - list.BorderThickness.Bottom;

			rowHeight.Should().Be(28);
			(innerHeight / rowHeight).Should().Be(8);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void ThePenCheckboxes_StandAtLeastSixPixelsOffTheListBorder()
	{
		using var viewModel = BuildWithPens(12);
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var list = Named<ListBox>(window, "ExportPens");
			var box = window.GetVisualDescendants().OfType<CheckBox>().First();
			var innerLeft = list.BorderThickness.Left;
			var boxLeft = box.TranslatePoint(new Point(0, 0), list)!.Value.X;

			(boxLeft - innerLeft).Should().BeGreaterThanOrEqualTo(6);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void TwoPensSharingAName_ShowTheirIdsInTheChecklist()
	{
		var pens = ExportPenChoice.Checklist(
		[
			(CsvExportStand.Pressure with { PenId = 17, Name = "Damper 01" }, true),
			(CsvExportStand.Pressure with { PenId = 18, Name = "Damper 01" }, false),
			(CsvExportStand.Argon, false)
		]);
		using var viewModel = CsvExportStand.Build(new FakeArchiveRowReader(), _messagePanel, _scheduler, pens);
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			window.GetVisualDescendants().OfType<CheckBox>().Select(ToolTip.GetTip)
				.Should().Equal("Damper 01 (17)", "Damper 01 (18)", "Argon flow");
			window.GetVisualDescendants().OfType<CheckBox>().Select(box => LabelOf(box).Text)
				.Should().Equal("Damper 01 (17)", "Damper 01 (18)", "Argon flow");
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void ThePenList_WithTwoPens_IsBelowItsMaxHeight()
	{
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var list = Named<ListBox>(window, "ExportPens");

			list.Bounds.Height.Should().BeLessThan(list.MaxHeight);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void ThePenList_NeverAutoHidesItsScrollBar()
	{
		using var viewModel = BuildWithPens(12);
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var scroller = Descendant<ScrollViewer>(Named<ListBox>(window, "ExportPens"), "PART_ScrollViewer");

			scroller.AllowAutoHide.Should().BeFalse();
			scroller.GetVisualDescendants().OfType<ScrollBar>()
				.Where(bar => bar.Orientation == Orientation.Vertical)
				.Should().OnlyContain(bar => !bar.AllowAutoHide);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void ThePensHeader_ReadsTheTickedAndTotalCounts_AndFollowsAClick()
	{
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var header = Named<TextBlock>(window, "ExportPensHeader");
			header.Text.Should().Be(Resources.FormatExportPensHeader(1, 2));

			HeadlessInput.Click(window, window.GetVisualDescendants().OfType<CheckBox>().Last());

			header.Text.Should().Be(Resources.FormatExportPensHeader(2, 2));
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void TheMessageRow_SitsAboveTheButtons_AndSpansTheContentWidth()
	{
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var message = Named<TextBlock>(window, "ExportMessage");
			var saveAs = Named<Button>(window, "ExportSaveAs");
			var list = Named<ListBox>(window, "ExportPens");

			var buttonRow = saveAs.GetVisualParent().Should().BeAssignableTo<Control>().Subject;
			message.Bounds.Bottom.Should().BeLessThanOrEqualTo(buttonRow.Bounds.Top);
			message.Bounds.Width.Should().Be(list.Bounds.Width);
			window.Width.Should().Be(390);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaTheory]
	[InlineData("ru")]
	[InlineData("en")]
	public void EachEdge_PutsTheDateOverTheTimeAndTheMarkerBox_AndTheTabOrderFollows(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		using var viewModel = BuildWithMarkers();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			foreach (var (label, date, time, marker) in new[]
				{
					(Resources.ExportStartLabel, "ExportStartDate", "ExportStartTime", "ExportStartMarker"),
					(Resources.ExportEndLabel, "ExportEndDate", "ExportEndTime", "ExportEndMarker")
				})
			{
				var labelBlock = window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == label);
				var datePicker = Named<CalendarDatePicker>(window, date);
				var timeBox = Named<TextBox>(window, time);
				var markerBox = Named<ComboBox>(window, marker);

				LeftOf(window, timeBox).Should().Be(LeftOf(window, datePicker), time);
				timeBox.Bounds.Width.Should().Be(110, time);
				(LeftOf(window, markerBox) - RightOf(window, timeBox)).Should().Be(8, marker);
				RightOf(window, markerBox).Should().Be(RightOf(window, datePicker), marker);
				(TopOf(window, timeBox) - BottomOf(window, datePicker)).Should().Be(10, "the second line is 10 px under");
				TopOf(window, markerBox).Should().Be(TopOf(window, timeBox), "one line holds both");
				CentreOf(window, labelBlock).Should().Be(CentreOf(window, datePicker), "the label is centred on the date");
				new Control[] { datePicker, timeBox, markerBox }
					.Should().OnlyContain(control => control.Bounds.Height == 32, "the controls keep Semi's height");
			}

			(TopOf(window, Named<CalendarDatePicker>(window, "ExportEndDate"))
					- BottomOf(window, Named<ComboBox>(window, "ExportStartMarker")))
				.Should().Be(20, "the edges stand 20 px apart");

			string[] edgeControls =
			[
				"ExportStartDate", "ExportStartTime", "ExportStartMarker",
				"ExportEndDate", "ExportEndTime", "ExportEndMarker"
			];
			var order = window.GetVisualDescendants().OfType<Control>()
				.Select(control => control.Name)
				.Where(name => edgeControls.Contains(name));
			order.Should().Equal(edgeControls);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaTheory]
	[InlineData("ru")]
	[InlineData("en")]
	public void TheDatesTheMarkerBoxesTheListAndTheMessage_EndAtOneRightEdge(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		using var viewModel = BuildWithMarkers();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var edge = RightOf(window, Named<CalendarDatePicker>(window, "ExportStartDate"));

			RightOf(window, Named<CalendarDatePicker>(window, "ExportEndDate")).Should().Be(edge);
			RightOf(window, Named<ComboBox>(window, "ExportStartMarker")).Should().Be(edge);
			RightOf(window, Named<ComboBox>(window, "ExportEndMarker")).Should().Be(edge);
			RightOf(window, Named<ListBox>(window, "ExportPens")).Should().Be(edge);
			RightOf(window, Named<TextBlock>(window, "ExportMessage")).Should().Be(edge);
			edge.Should().Be(370);
			window.Width.Should().Be(edge + 20, "the margin is the only space right of the form");
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void ALongPenLabel_IsTrimmedInsideTheList_AndItsToolTipCarriesTheWholeLabel()
	{
		var longName = new string('W', 120);
		var pens = ExportPenChoice.Checklist([(CsvExportStand.Pressure with { Name = longName }, true)]);
		using var viewModel = CsvExportStand.Build(new FakeArchiveRowReader(), _messagePanel, _scheduler, pens);
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var list = Named<ListBox>(window, "ExportPens");
			var scroller = Descendant<ScrollViewer>(list, "PART_ScrollViewer");
			var box = window.GetVisualDescendants().OfType<CheckBox>().Single();
			var label = LabelOf(box);

			label.TextTrimming.Should().Be(TextTrimming.CharacterEllipsis);
			label.Text.Should().Be(longName);
			scroller.Extent.Width.Should().BeLessThanOrEqualTo(scroller.Viewport.Width);
			scroller.GetVisualDescendants().OfType<ScrollBar>()
				.Where(bar => bar.Orientation == Orientation.Horizontal)
				.Should().OnlyContain(bar => !bar.IsVisible);
			RightOf(window, label).Should().BeLessThanOrEqualTo(RightOf(window, list));
			ToolTip.GetTip(box).Should().Be(longName);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void TheSaveAsButton_IsDisabledWhenNoPenIsTicked()
	{
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var saveAs = Named<Button>(window, "ExportSaveAs");
			var boxes = window.GetVisualDescendants().OfType<CheckBox>().ToList();
			boxes.Select(box => box.IsChecked).Should().Equal(true, false);

			HeadlessInput.Click(window, boxes[0]);

			saveAs.IsEffectivelyEnabled.Should().BeFalse();
			Named<TextBlock>(window, "ExportMessage").Text.Should().Be(Resources.ExportMessageNoPenTicked);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public async Task WhileAnExportRuns_SaveAsIsDisabledAndCancelIsEnabled_AndTheReverseHoldsAfterIt()
	{
		using var directory = new TemporaryDirectory("csv-export-gate");
		var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var reader = new FakeArchiveRowReader
		{
			OnSeeds = async _ =>
			{
				reached.TrySetResult();
				await release.Task;

				return Result.Ok<IReadOnlyList<StoredArchiveRow>>([]);
			}
		};
		using var viewModel = BuildWithMarkers(reader);
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var saveAs = Named<Button>(window, "ExportSaveAs");
			var cancel = Named<Button>(window, "ExportCancel");
			saveAs.IsEffectivelyEnabled.Should().BeTrue();
			cancel.IsEffectivelyEnabled.Should().BeFalse("there is nothing to cancel before an export starts");

			var export = viewModel.ExportCommand.Execute(Path.Combine(directory.Path, "out.csv")).ToTask();
			await reached.Task;
			await HeadlessWait.Until(() => !saveAs.IsEffectivelyEnabled && cancel.IsEffectivelyEnabled);
			var formControls = new Control[]
			{
				Named<CalendarDatePicker>(window, "ExportStartDate"),
				Named<TextBox>(window, "ExportEndTime"),
				Named<ListBox>(window, "ExportPens"),
				Named<ComboBox>(window, "ExportStartMarker"),
				Named<ComboBox>(window, "ExportEndMarker")
			};
			formControls.Should().OnlyContain(
				control => !control.IsEffectivelyEnabled, "the form is frozen while it exports");

			release.SetResult();
			await export;
			await HeadlessWait.Until(() => saveAs.IsEffectivelyEnabled && !cancel.IsEffectivelyEnabled);
			formControls.Should().OnlyContain(control => control.IsEffectivelyEnabled);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public async Task AFailedExport_PaintsTheMessageLineAsAnError_AndASuccessfulOneDoesNot()
	{
		using var directory = new TemporaryDirectory("csv-export-class");
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var message = Named<TextBlock>(window, "ExportMessage");

			await viewModel.ExportCommand.Execute(Path.Combine(directory.Path, "out.csv")).ToTask();
			message.Classes.Should().NotContain(ErrorClass);

			await viewModel.ExportCommand.Execute(Path.Combine(directory.Path, "missing", "out.csv")).ToTask();
			await HeadlessWait.Until(() => message.Classes.Contains(ErrorClass), "the error class");
			message.Text.Should().Be(Resources.FailureExportFileTitle);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public async Task AfterASuccess_TheMessageLineToolTipIsTheFullPath()
	{
		using var directory = new TemporaryDirectory("csv-export-tip");
		var path = Path.Combine(directory.Path, "out.csv");
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var message = Named<TextBlock>(window, "ExportMessage");

			ToolTip.GetTip(message).Should().BeNull();

			await viewModel.ExportCommand.Execute(path).ToTask();
			await HeadlessWait.Until(() => ToolTip.GetTip(message) is not null, "the tooltip");

			ToolTip.GetTip(message).Should().Be(path);
			message.Text.Should().Contain("out.csv").And.NotContain(directory.Path);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public async Task ChoosingAStartAndAnEndMarker_FillsBothEdges_AndTheCountAndTheExportReadThatSpan()
	{
		using var directory = new TemporaryDirectory("csv-export-markers");
		var reader = new FakeArchiveRowReader();
		using var viewModel = BuildWithMarkers(reader);
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var startBox = Named<ComboBox>(window, "ExportStartMarker");
			var endBox = Named<ComboBox>(window, "ExportEndMarker");

			HeadlessInput.Pick(window, startBox, viewModel.StartMarkers.Single());
			HeadlessInput.Pick(window, endBox, viewModel.EndMarkers.Single());

			Named<TextBox>(window, "ExportStartTime").Text.Should().Be("08:15:00");
			Named<TextBox>(window, "ExportEndTime").Text
				.Should().Be("08:45:01", "the end keeps every row of its second");
			Named<CalendarDatePicker>(window, "ExportStartDate").SelectedDate.Should().Be(_startDay);
			Named<CalendarDatePicker>(window, "ExportEndDate").SelectedDate.Should().Be(_startDay);
			ShownText(startBox).Should().Be("Etch.csv", "the time box beside it shows the time");
			ShownText(endBox).Should().Be("Etch.csv");
			viewModel.Form.IsValid.Should().BeTrue();
			Named<Button>(window, "ExportSaveAs").IsEffectivelyEnabled.Should().BeTrue();

			_scheduler.AdvanceBy(CsvExportViewModel.CountThrottle.Ticks);

			var start = _startDay.AddHours(8.25);
			var end = _startDay.AddHours(8.75).AddSeconds(1);
			var count = reader.CountCalls.Should().ContainSingle().Subject;
			count.From.Should().Be(start);
			count.To.Should().Be(end);

			await viewModel.ExportCommand.Execute(Path.Combine(directory.Path, "out.csv")).ToTask();

			reader.SeedCalls.Should().ContainSingle().Which.From.Should().Be(start);
			reader.SliceCalls.Should().NotBeEmpty();
			reader.SliceCalls[0].From.Should().Be(start);
			reader.SliceCalls[^1].To.Should().Be(end);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaTheory]
	[InlineData("ru")]
	[InlineData("en")]
	public void TheClosedBox_ShowsTheLabelAlone_AndTheOpenListTheTimeAndTheLabel(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		StoredMarker[] markers =
		[
			new(1, CsvExportStand.ToUtc(_startDay.AddHours(8.25)), "Etch.csv", MarkerKind.RecipeStart),
			new(2, CsvExportStand.ToUtc(_startDay.AddHours(8.5)), null, MarkerKind.User)
		];
		using var viewModel = CsvExportStand.Build(
			new FakeArchiveRowReader(),
			_messagePanel,
			_scheduler,
			[new ExportPenChoice(CsvExportStand.Pressure, true)],
			markers: markers);
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var startBox = Named<ComboBox>(window, "ExportStartMarker");

			HeadlessInput.Click(window, startBox);

			viewModel.StartChoices.Select(choice => ListedText(startBox, choice))
				.Should().Equal(Resources.ExportNoMarker, "08:15:00 Etch.csv", $"08:30:00 {Resources.ExportMarkerUnnamed}");

			HeadlessInput.Press(window, PhysicalKey.Escape);
			HeadlessInput.Pick(window, startBox, viewModel.StartMarkers[0]);

			ShownText(startBox).Should().Be("Etch.csv");

			HeadlessInput.Pick(window, startBox, viewModel.StartMarkers[1]);

			ShownText(startBox).Should().Be(Resources.ExportMarkerUnnamed);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void AHandTimeEdit_ReturnsTheBoxToNoMarker_AndChoosingTheMarkerAgainRefillsTheTime()
	{
		using var viewModel = BuildWithMarkers();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var startTime = Named<TextBox>(window, "ExportStartTime");
			var startBox = Named<ComboBox>(window, "ExportStartMarker");
			var marker = viewModel.StartMarkers.Single();
			HeadlessInput.Pick(window, startBox, marker);

			HeadlessInput.Clear(window, startTime);
			HeadlessInput.Type(window, startTime, "08:20:00");

			startTime.Text.Should().Be("08:20:00");
			startBox.SelectedItem.Should().Be(viewModel.StartChoices[0]);
			ShownText(startBox).Should().Be(Resources.ExportNoMarker);

			HeadlessInput.Pick(window, startBox, marker);

			startTime.Text.Should().Be("08:15:00");
			startBox.SelectedItem.Should().Be(marker);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void ADateChange_ReturnsThatEdgesBoxToNoMarker()
	{
		using var viewModel = BuildWithMarkers();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var startBox = Named<ComboBox>(window, "ExportStartMarker");
			var endBox = Named<ComboBox>(window, "ExportEndMarker");
			HeadlessInput.Pick(window, startBox, viewModel.StartMarkers.Single());
			HeadlessInput.Pick(window, endBox, viewModel.EndMarkers.Single());

			SetDate(window, Named<CalendarDatePicker>(window, "ExportStartDate"), _startDay.AddDays(-1));

			startBox.SelectedItem.Should().Be(viewModel.StartChoices[0], "the box shows no marker, not an empty box");
			ShownText(startBox).Should().Be(Resources.ExportNoMarker);
			endBox.SelectedItem.Should().Be(viewModel.EndMarkers.Single(), "the end edge keeps its marker");
			Named<TextBox>(window, "ExportStartTime").Text.Should().Be("08:15:00");
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaTheory]
	[InlineData("ru")]
	[InlineData("en")]
	public void ChoosingAStartMarker_LeavesTheEndBoxOnNoMarker(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		using var viewModel = BuildWithMarkers();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var endBox = Named<ComboBox>(window, "ExportEndMarker");

			HeadlessInput.Pick(window, Named<ComboBox>(window, "ExportStartMarker"), viewModel.StartMarkers.Single());

			endBox.SelectedItem.Should().Be(viewModel.EndChoices[0], "the updated end list leaves the box on no marker");
			endBox.SelectionBoxItem.Should().Be(viewModel.EndChoices[0]);
			ShownTextOrNull(endBox).Should().Be(Resources.ExportNoMarker);
			HeadlessInput.Click(window, endBox);
			endBox.ContainerFromIndex(0).Should().BeOfType<ComboBoxItem>().Which.IsSelected.Should().BeTrue();
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaTheory]
	[InlineData("ru")]
	[InlineData("en")]
	public void ChoosingAnEndMarker_LeavesTheStartBoxOnNoMarker(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		using var viewModel = BuildWithMarkers();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var startBox = Named<ComboBox>(window, "ExportStartMarker");

			HeadlessInput.Pick(window, Named<ComboBox>(window, "ExportEndMarker"), viewModel.EndMarkers.Single());

			startBox.SelectedItem.Should().Be(viewModel.StartChoices[0]);
			startBox.SelectionBoxItem.Should().Be(viewModel.StartChoices[0]);
			ShownTextOrNull(startBox).Should().Be(Resources.ExportNoMarker);
			HeadlessInput.Click(window, startBox);
			startBox.ContainerFromIndex(0).Should().BeOfType<ComboBoxItem>().Which.IsSelected.Should().BeTrue();
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void AnEndMarkerHeld_StaysInItsBox_WhenAStartMarkerIsChosen()
	{
		using var viewModel = BuildWithMarkers();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var endBox = Named<ComboBox>(window, "ExportEndMarker");
			var endMarker = viewModel.EndMarkers.Single();
			HeadlessInput.Pick(window, endBox, endMarker);

			HeadlessInput.Pick(window, Named<ComboBox>(window, "ExportStartMarker"), viewModel.StartMarkers.Single());

			viewModel.SelectedEndMarker.Should().Be(endMarker, "the end still reads the marker's value and it is listed");
			endBox.SelectedItem.Should().Be(endMarker);
			endBox.SelectionBoxItem.Should().Be(endMarker);
			ShownText(endBox).Should().Be(endMarker.Label);
			Named<TextBox>(window, "ExportEndTime").Text.Should().Be("08:45:01");
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void AnEndMarkerChosenBeforeTheWindowBinds_StaysInItsBox_WhenAStartMarkerIsChosen()
	{
		using var viewModel = BuildWithMarkers();
		var endMarker = viewModel.EndMarkers.Single();
		viewModel.SelectedEndMarker = endMarker;
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var endBox = Named<ComboBox>(window, "ExportEndMarker");

			HeadlessInput.Pick(window, Named<ComboBox>(window, "ExportStartMarker"), viewModel.StartMarkers.Single());

			viewModel.SelectedEndMarker.Should().Be(endMarker);
			endBox.SelectedItem.Should().Be(endMarker);
			endBox.SelectionBoxItem.Should().Be(endMarker);
			ShownText(endBox).Should().Be(endMarker.Label);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void AnEndMarkerInTheDaysLastSecond_MovesTheEndOntoTheNextDate_AndTheBoxShowsNoMarker()
	{
		StoredMarker[] markers =
		[
			new(3, CsvExportStand.ToUtc(_startDay.AddHours(8.5)), "Etch.csv", MarkerKind.RecipeStart),
			new(4, CsvExportStand.ToUtc(_startDay.AddDays(1).AddMilliseconds(-500)), "Etch.csv", MarkerKind.RecipeEnd)
		];
		using var viewModel = CsvExportStand.Build(
			new FakeArchiveRowReader(),
			_messagePanel,
			_scheduler,
			[new ExportPenChoice(CsvExportStand.Pressure, true)],
			markers: markers);
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var endBox = Named<ComboBox>(window, "ExportEndMarker");

			HeadlessInput.Pick(window, endBox, viewModel.EndMarkers.Single());
			_scheduler.AdvanceBy(1);
			Dispatcher.UIThread.RunJobs();

			Named<CalendarDatePicker>(window, "ExportEndDate").SelectedDate.Should().Be(_startDay.AddDays(1));
			Named<TextBox>(window, "ExportEndTime").Text.Should().Be("00:00:00");
			endBox.SelectedItem.Should().Be(viewModel.EndChoices[0], "the next date's list does not hold the marker");
			endBox.SelectionBoxItem.Should().Be(viewModel.EndChoices[0]);
			ShownText(endBox).Should().Be(Resources.ExportNoMarker);
			endBox.IsEffectivelyEnabled.Should().BeFalse("the next date holds no end marker");
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public async Task AnEndMarkerInTheDaysLastSecond_LeavesTheClosedBoxOnNoMarker_UnderTheAvaloniaScheduler()
	{
		StoredMarker[] markers =
		[
			new(3, CsvExportStand.ToUtc(_startDay.AddHours(8.5)), "Etch.csv", MarkerKind.RecipeStart),
			new(4, CsvExportStand.ToUtc(_startDay.AddDays(1).AddMilliseconds(-500)), "Etch.csv", MarkerKind.RecipeEnd)
		];
		using var viewModel = CsvExportStand.Build(
			new FakeArchiveRowReader(),
			_messagePanel,
			AvaloniaScheduler.Instance,
			[new ExportPenChoice(CsvExportStand.Pressure, true)],
			markers: markers);
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var endBox = Named<ComboBox>(window, "ExportEndMarker");

			HeadlessInput.Pick(window, endBox, viewModel.EndMarkers.Single());
			await HeadlessWait.Until(() => viewModel.SelectedEndMarker.IsNoMarker, "the release after the pick");

			Named<TextBox>(window, "ExportEndTime").Text.Should().Be("00:00:00");
			endBox.SelectedItem.Should().Be(viewModel.EndChoices[0]);
			endBox.SelectionBoxItem.Should().Be(viewModel.EndChoices[0]);
			ShownText(endBox).Should().Be(Resources.ExportNoMarker);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void AMarkerOnOneEdgeAndATypedTimeOnTheOther_IsAValidRangeWithNoMessage()
	{
		using var viewModel = BuildWithMarkers();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var endTime = Named<TextBox>(window, "ExportEndTime");
			HeadlessInput.Pick(window, Named<ComboBox>(window, "ExportStartMarker"), viewModel.StartMarkers.Single());

			HeadlessInput.Clear(window, endTime);
			HeadlessInput.Type(window, endTime, "08:30:00");

			viewModel.Form.IsValid.Should().BeTrue();
			Named<TextBlock>(window, "ExportMessage").Text.Should().BeEmpty();
			Named<ComboBox>(window, "ExportStartMarker").SelectedItem.Should().Be(viewModel.StartMarkers.Single());
			Named<ComboBox>(window, "ExportEndMarker").SelectedItem.Should().Be(viewModel.EndChoices[0]);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void ADayWithoutMarkers_DisablesItsBoxOnNoMarker_InTheSecondaryForeground()
	{
		var variant = App.VariantFor(AppThemeVariant.Light);
		using var scope = ThemeProbe.ApplyVariant(variant);
		var secondary = ThemeProbe.Colour("AppSecondaryForegroundBrush", variant);
		using var viewModel = BuildWithMarkers();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var startBox = Named<ComboBox>(window, "ExportStartMarker");
			var endBox = Named<ComboBox>(window, "ExportEndMarker");
			startBox.IsEffectivelyEnabled.Should().BeTrue();
			endBox.IsEffectivelyEnabled.Should().BeTrue();
			ShownForeground(startBox).Should().Be(secondary);

			HeadlessInput.Pick(window, startBox, viewModel.StartMarkers.Single());

			ShownForeground(startBox).Should().NotBe(secondary, "a marker reads in the box's own foreground");

			SetDate(window, Named<CalendarDatePicker>(window, "ExportStartDate"), _startDay.AddDays(-1));

			startBox.IsEffectivelyEnabled.Should().BeFalse("the day before holds no marker");
			ShownText(startBox).Should().Be(Resources.ExportNoMarker);
			ShownForeground(startBox).Should().Be(secondary);
			endBox.IsEffectivelyEnabled.Should().BeTrue("the end date still holds its marker");
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void AWindowWithoutMarkers_DisablesBothMarkerBoxes()
	{
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			Named<ComboBox>(window, "ExportStartMarker").IsEffectivelyEnabled.Should().BeFalse();
			Named<ComboBox>(window, "ExportEndMarker").IsEffectivelyEnabled.Should().BeFalse();
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void UnderRussian_TheWidestDateInInter_FitsTheStartPickersTextViewportOnTheStand()
	{
		// The headless shaper advances every glyph by the font size, so the texts are measured in Inter through
		// Skia and the realised layout is corrected by the label column's real width.
		const double FormWidth = 350;
		const double FormColumnSpacing = 14;
		const double CaretAndRoundingAllowance = 2;
		using var cultureScope = new CultureScope("ru");
		using var viewModel = BuildWithMarkers();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var picker = Named<CalendarDatePicker>(window, "ExportStartDate");
			var box = TextOf(picker);
			var label = window.GetVisualDescendants().OfType<TextBlock>()
				.Single(block => block.Text == Resources.ExportStartLabel);
			using var typeface = InterTypeface();
			using var labelFont = InterFont(typeface, label.FontSize);
			using var dateFont = InterFont(typeface, box.FontSize);

			var headlessLabelColumn = LeftOf(window, picker) - LeftOf(window, label) - FormColumnSpacing;
			var standLabelColumn = Math.Ceiling(Math.Max(
				labelFont.MeasureText(Resources.ExportStartLabel), labelFont.MeasureText(Resources.ExportEndLabel)));
			var labelCorrection = headlessLabelColumn - standLabelColumn;
			var standDateCell = picker.Bounds.Width + labelCorrection;
			var standViewport = Descendant<ScrollViewer>(box, "PART_ScrollViewer").Viewport.Width + labelCorrection;

			box.Text.Should().Be("13.10.2026");
			standDateCell.Should().BeApproximately(
				FormWidth - FormColumnSpacing - standLabelColumn, 1, "the date takes the whole field column");
			standViewport.Should().BeGreaterThanOrEqualTo(
				WidestDateWidth(dateFont, Resources.ExportDateFormat) + CaretAndRoundingAllowance);
		}
		finally
		{
			Dismiss(window);
		}
	}

	private static SKTypeface InterTypeface()
	{
		using var stream = AssetLoader.Open(new Uri("avares://Avalonia.Fonts.Inter/Assets/Inter-Regular.ttf"));
		using var data = SKData.Create(stream);

		return SKTypeface.FromData(data);
	}

	private static SKFont InterFont(SKTypeface typeface, double fontSize)
	{
		return new SKFont(typeface, (float)fontSize) { LinearMetrics = true, Subpixel = true };
	}

	// Each digit repeated over the mask bounds every date the mask can show.
	private static double WidestDateWidth(SKFont font, string format)
	{
		return Enumerable.Range(0, 10)
			.Select(digit => new string(
				[.. format.Select(character => char.IsLetter(character) ? (char)('0' + digit) : character)]))
			.Max(text => font.MeasureText(text));
	}

	private static TextBox TextOf(CalendarDatePicker picker)
	{
		return Descendant<TextBox>(picker, "PART_TextBox");
	}

	private static TextBlock LabelOf(CheckBox box)
	{
		return box.Content.Should().BeOfType<TextBlock>().Subject;
	}

	private static double LeftOf(Visual root, Control control)
	{
		return control.TranslatePoint(new Point(0, 0), root)!.Value.X;
	}

	private static double TopOf(Visual root, Control control)
	{
		return control.TranslatePoint(new Point(0, 0), root)!.Value.Y;
	}

	private static double BottomOf(Visual root, Control control)
	{
		return control.TranslatePoint(new Point(0, control.Bounds.Height), root)!.Value.Y;
	}

	private static double CentreOf(Visual root, Control control)
	{
		return control.TranslatePoint(new Point(0, control.Bounds.Height / 2), root)!.Value.Y;
	}

	private static double RightOf(Visual root, Control control)
	{
		return control.TranslatePoint(new Point(control.Bounds.Width, 0), root)!.Value.X;
	}

	private static string DateText(DateTime date)
	{
		return date.ToString(Resources.ExportDateFormat, CultureInfo.InvariantCulture);
	}

	private static void SetDate(TopLevel window, CalendarDatePicker picker, DateTime date)
	{
		var box = TextOf(picker);

		HeadlessInput.Clear(window, box);
		HeadlessInput.Type(window, box, DateText(date));
		HeadlessInput.Press(window, PhysicalKey.Enter);
	}

	private static TextBlock ShownBlock(ComboBox box)
	{
		return box.GetVisualDescendants().OfType<TextBlock>().Single(block => block.DataContext is ExportMarkerChoice);
	}

	private static string? ShownText(ComboBox box)
	{
		return ShownBlock(box).Text;
	}

	private static string? ListedText(ComboBox box, ExportMarkerChoice choice)
	{
		var item = box.ContainerFromItem(choice)
			?? throw new InvalidOperationException("The open list realised no container for the choice.");

		return item.GetVisualDescendants().OfType<TextBlock>().Single(block => Equals(block.DataContext, choice)).Text;
	}

	private static string? ShownTextOrNull(ComboBox box)
	{
		return box.GetVisualDescendants().OfType<TextBlock>()
			.SingleOrDefault(block => block.DataContext is ExportMarkerChoice)?.Text;
	}

	private static Color ShownForeground(ComboBox box)
	{
		return ShownBlock(box).Foreground.Should().BeAssignableTo<ISolidColorBrush>().Subject.Color;
	}

	private static Color BorderColourOf(CalendarDatePicker picker)
	{
		var border = Descendant<Border>(picker, "Background");

		return border.BorderBrush.Should().BeAssignableTo<ISolidColorBrush>().Subject.Color;
	}

	private CsvExportViewModel BuildWithPens(int count)
	{
		var pens = ExportPenChoice.Checklist(
		[
			.. Enumerable.Range(1, count).Select(index =>
				(CsvExportStand.Pressure with { PenId = index, Name = $"Damper {index:00}" }, index == 1))
		]);

		return CsvExportStand.Build(new FakeArchiveRowReader(), _messagePanel, _scheduler, pens);
	}

	private CsvExportViewModel BuildOverArchiveDays(IReadOnlyList<DateOnly> daysWithRows)
	{
		return CsvExportStand.Build(
			new FakeArchiveRowReader(),
			_messagePanel,
			_scheduler,
			[new ExportPenChoice(CsvExportStand.Pressure, true)],
			_newestSampleUtc,
			(_, _, _) => Task.FromResult(Result.Ok(daysWithRows)));
	}

	// A recipe start at 08:15 and its end at 08:45 on the prefilled day, in the stand's +3 zone.
	private CsvExportViewModel BuildWithMarkers(FakeArchiveRowReader? reader = null)
	{
		StoredMarker[] markers =
		[
			new(1, CsvExportStand.ToUtc(_startDay.AddHours(8.25)), "Etch.csv", MarkerKind.RecipeStart),
			new(2, CsvExportStand.ToUtc(_startDay.AddHours(8.75)), "Etch.csv", MarkerKind.RecipeEnd)
		];

		return CsvExportStand.Build(
			reader ?? new FakeArchiveRowReader(),
			_messagePanel,
			_scheduler,
			[new ExportPenChoice(CsvExportStand.Pressure, true)],
			markers: markers);
	}

	private CsvExportViewModel Build(FakeArchiveRowReader? reader = null)
	{
		return CsvExportStand.Build(
			reader ?? new FakeArchiveRowReader(),
			_messagePanel,
			_scheduler,
			[new ExportPenChoice(CsvExportStand.Pressure, true), new ExportPenChoice(CsvExportStand.Argon, false)]);
	}
}
