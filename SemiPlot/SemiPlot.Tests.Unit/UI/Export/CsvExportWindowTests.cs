using System.Globalization;
using System.Reactive.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.UI;
using SemiPlot.UI.Export;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Startup;

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
			HeadlessInput.Type(window, box, "not a date");
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

	[AvaloniaFact]
	public void EachRangeRow_PutsTheTimeBoxLeftOfTheDatePicker_AndTheTabOrderFollows()
	{
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			foreach (var (time, date) in new[] { ("ExportStartTime", "ExportStartDate"), ("ExportEndTime", "ExportEndDate") })
			{
				var timeBox = Named<TextBox>(window, time);
				var datePicker = Named<CalendarDatePicker>(window, date);

				RightOf(window, timeBox).Should().BeLessThan(LeftOf(window, datePicker), time);
				timeBox.Bounds.Top.Should().Be(datePicker.Bounds.Top, "one row holds both");
			}

			var order = window.GetVisualDescendants().OfType<Control>()
				.Select(control => control.Name)
				.Where(name => name is "ExportStartTime" or "ExportStartDate" or "ExportEndTime" or "ExportEndDate");
			order.Should().Equal("ExportStartTime", "ExportStartDate", "ExportEndTime", "ExportEndDate");
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void TheListAndTheMessage_EndAtTheRightEdgeOfTheDatePickers()
	{
		using var viewModel = Build();
		var window = Realise(new CsvExportWindow { DataContext = viewModel });
		try
		{
			var edge = RightOf(window, Named<CalendarDatePicker>(window, "ExportStartDate"));

			RightOf(window, Named<CalendarDatePicker>(window, "ExportEndDate")).Should().Be(edge);
			RightOf(window, Named<ListBox>(window, "ExportPens")).Should().Be(edge);
			RightOf(window, Named<TextBlock>(window, "ExportMessage")).Should().Be(edge);
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
		using var viewModel = Build(reader);
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
				Named<ListBox>(window, "ExportPens")
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

	[AvaloniaTheory]
	[InlineData("ru")]
	[InlineData("en")]
	[InlineData("th-TH")]
	[InlineData("fa-IR")]
	public void TheSuggestedFileName_UsesInvariantDigitsUnderEveryInterfaceCulture(string culture)
	{
		using var cultureScope = new CultureScope(culture);

		CsvFileExporter.SuggestedFileName(new DateTime(2026, 10, 13, 8, 5, 9), new DateTime(2026, 10, 14, 7, 0, 0))
			.Should().Be("semiplot_20261013_080509-20261014_070000.csv");
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

	private CsvExportViewModel Build(FakeArchiveRowReader? reader = null)
	{
		return CsvExportStand.Build(
			reader ?? new FakeArchiveRowReader(),
			_messagePanel,
			_scheduler,
			[new ExportPenChoice(CsvExportStand.Pressure, true), new ExportPenChoice(CsvExportStand.Argon, false)]);
	}
}
