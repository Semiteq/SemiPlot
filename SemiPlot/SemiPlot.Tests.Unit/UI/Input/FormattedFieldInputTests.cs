using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Threading;

using AwesomeAssertions;

using SemiPlot.UI.Input;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Input;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class FormattedFieldInputTests
{
	[AvaloniaTheory]
	[InlineData("083000", "08:30:00")]
	[InlineData("08:30:00", "08:30:00")]
	[InlineData("0830", "08:30")]
	[InlineData("0830001", "08:30:00")]
	[InlineData("8a3", "83")]
	public void AClockTimeField_PutsAColonAfterEveryTwoDigitsFromTheLeft(string typed, string shown)
	{
		var (window, field) = Show(FieldLayout.ClockTime);

		HeadlessInput.Type(window, field, typed);

		field.Text.Should().Be(shown);
	}

	[AvaloniaTheory]
	[InlineData("en-US", "20260615", "2026-06-15")]
	[InlineData("en-US", "202606", "2026-06")]
	[InlineData("ru-RU", "15062026", "15.06.2026")]
	[InlineData("ru-RU", "1506", "15.06")]
	[InlineData("ru-RU", "150620261", "15.06.2026")]
	public void ADateField_FillsTheSlotsOfTheCulturesDateFormat(string cultureName, string typed, string shown)
	{
		using var culture = new CultureScope(cultureName);
		var (window, field) = Show(FieldLayout.Date);

		HeadlessInput.Type(window, field, typed);

		field.Text.Should().Be(shown);
	}

	[AvaloniaTheory]
	[InlineData("5", "5")]
	[InlineData("500", "5:00")]
	[InlineData("50000", "5:00:00")]
	[InlineData("1680000", "168:00:00")]
	[InlineData("168:00:00", "168:00:00")]
	public void ADurationField_GroupsSecondsAndMinutesFromTheRightAndLeavesTheHoursUnbounded(string typed, string shown)
	{
		var (window, field) = Show(FieldLayout.Duration);

		HeadlessInput.Type(window, field, typed);

		field.Text.Should().Be(shown);
	}

	[AvaloniaFact]
	public void ADigitThatOpensAGroup_LandsAfterTheNewColonWithTheCaretAtTheEnd()
	{
		var (window, field) = Show(FieldLayout.ClockTime);
		field.Text = "08:30";
		Dispatcher.UIThread.RunJobs();

		HeadlessInput.Type(window, field, "0");

		field.Text.Should().Be("08:30:0");
		field.CaretIndex.Should().Be(field.Text.Length);
	}

	[AvaloniaFact]
	public void SelectAllAndBackspace_EmptiesAFilledField()
	{
		var (window, field) = Show(FieldLayout.ClockTime);
		field.Text = "08:30:00";
		Dispatcher.UIThread.RunJobs();

		HeadlessInput.Clear(window, field);

		field.Text.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void AFieldWithoutALayout_KeepsWhatWasTyped()
	{
		var field = new TextBox { Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
		var window = new Window { Width = 400, Height = 60, Content = field };
		window.Show();

		HeadlessInput.Type(window, field, "083000");

		field.Text.Should().Be("083000");
	}

	[AvaloniaTheory]
	[InlineData("1f77b4", "#1F77B4")]
	[InlineData("#1f77b4", "#1F77B4")]
	[InlineData("#1F77B4", "#1F77B4")]
	[InlineData("1g2h3", "#123")]
	[InlineData("1F77B4AA", "#1F77B4")]
	[InlineData("#", "")]
	public void AHexColorField_KeepsHexDigitsUpperCasesThemAndPutsTheHash(string typed, string shown)
	{
		var (window, field) = Show(FieldLayout.HexColor);

		HeadlessInput.Type(window, field, typed);

		field.Text.Should().Be(shown);
	}

	[AvaloniaFact]
	public void AHexDigitTypedAfterTheHash_KeepsTheCaretAfterThatDigit()
	{
		var (window, field) = ShowHex("#ABCDEF");

		HeadlessInput.Type(window, field, "1", caretIndex: 1);

		field.Text.Should().Be("#1ABCDE");
		field.CaretIndex.Should().Be(2);
	}

	[AvaloniaFact]
	public void ANonHexCharacterTypedAfterTheHash_LeavesTheTextAndTheCaretAfterTheHash()
	{
		var (window, field) = ShowHex("#ABCDEF");

		HeadlessInput.Type(window, field, "g", caretIndex: 1);

		field.Text.Should().Be("#ABCDEF");
		field.CaretIndex.Should().Be(1);
	}

	[AvaloniaFact]
	public void BackspaceOnTheHash_KeepsTheTextAndTheCaretAfterTheHash()
	{
		var (window, field) = ShowHex("#1F77B4");
		HeadlessInput.Click(window, field);
		field.CaretIndex = 1;

		HeadlessInput.Press(window, PhysicalKey.Backspace);

		field.Text.Should().Be("#1F77B4");
		field.CaretIndex.Should().Be(1);
	}

	[AvaloniaFact]
	public void BackspaceOnTheOnlyDigit_EmptiesTheBox()
	{
		var (window, field) = ShowHex("#1");
		HeadlessInput.Click(window, field);
		field.CaretIndex = field.Text!.Length;

		HeadlessInput.Press(window, PhysicalKey.Backspace);

		field.Text.Should().BeEmpty();
		field.CaretIndex.Should().Be(0);
	}

	[AvaloniaFact]
	public void SelectAllAndBackspace_EmptiesAFilledHexBox()
	{
		var (window, field) = ShowHex("#1F77B4");

		HeadlessInput.Clear(window, field);

		field.Text.Should().BeEmpty();
	}

	[AvaloniaTheory]
	[InlineData("1f77b4")]
	[InlineData("#1f77b4")]
	public async Task APasteWithOrWithoutTheHash_IsNormalised(string pasted)
	{
		var (_, field) = Show(FieldLayout.HexColor);
		await TopLevel.GetTopLevel(field)!.Clipboard!.SetTextAsync(pasted);

		field.Paste();
		Dispatcher.UIThread.RunJobs();

		field.Text.Should().Be("#1F77B4");
	}

	[AvaloniaFact]
	public void DeleteOnTheHash_KeepsTheTextAndPutsTheCaretAfterTheHash()
	{
		var (window, field) = ShowHex("#1F77B4");
		HeadlessInput.Click(window, field);
		field.CaretIndex = 0;

		HeadlessInput.Press(window, PhysicalKey.Delete);

		field.Text.Should().Be("#1F77B4");
		field.CaretIndex.Should().Be(1);
	}

	[AvaloniaFact]
	public void ACutOfTheHash_PutsTheHashBack()
	{
		var (_, field) = ShowHex("#1F77B4");
		field.SelectionStart = 0;
		field.SelectionEnd = 1;

		field.Cut();
		Dispatcher.UIThread.RunJobs();

		field.Text.Should().Be("#1F77B4");
	}

	[AvaloniaFact]
	public void UndoAfterTypedDigits_LeavesANormalisedText()
	{
		var (window, field) = Show(FieldLayout.HexColor);
		HeadlessInput.Type(window, field, "1f77b4");

		HeadlessInput.Press(window, PhysicalKey.Z, RawInputModifiers.Control);

		field.Text.Should().Be("#1F77B4", "the undo restores the typed 1f77b4, which the box normalises again");
	}

	[AvaloniaFact]
	public async Task ACtrlVPaste_IsNormalised()
	{
		var (window, field) = Show(FieldLayout.HexColor);
		HeadlessInput.Click(window, field);
		await window.Clipboard!.SetTextAsync("1f77b4");

		HeadlessInput.Press(window, PhysicalKey.V, RawInputModifiers.Control);

		field.Text.Should().Be("#1F77B4");
	}

	[AvaloniaFact]
	public async Task AValueSetInCodeAfterAnEmptyPasteAndAFocusLoss_StaysAsSet()
	{
		var field = new TextBox { Width = 300 };
		FormattedFieldInput.SetLayout(field, FieldLayout.HexColor);
		var other = new TextBox { Width = 300 };
		var window = new Window { Width = 400, Height = 120, Content = new StackPanel { Children = { field, other } } };
		window.Show();
		HeadlessInput.Click(window, field);
		await window.Clipboard!.SetTextAsync(string.Empty);
		HeadlessInput.Press(window, PhysicalKey.V, RawInputModifiers.Control);
		HeadlessInput.Click(window, other);

		field.Text = "red";
		Dispatcher.UIThread.RunJobs();

		field.Text.Should().Be("red");
	}

	[AvaloniaTheory]
	[InlineData("red", false)]
	[InlineData("red", true)]
	[InlineData("#1f77b4", false)]
	[InlineData("#1f77b4", true)]
	public void AValueSetInCode_StaysAsSet(string value, bool focused)
	{
		var (window, field) = Show(FieldLayout.HexColor);

		if (focused)
		{
			HeadlessInput.Click(window, field);
		}

		field.Text = value;
		Dispatcher.UIThread.RunJobs();

		field.Text.Should().Be(value);
	}

	[AvaloniaFact]
	public void AValueSetInCodeAfterBackspaceAtTheStart_StaysAsSet()
	{
		var (window, field) = ShowHex("#1F77B4");
		HeadlessInput.Click(window, field);
		HeadlessInput.Press(window, PhysicalKey.Home);
		HeadlessInput.Press(window, PhysicalKey.Backspace);

		field.Text = "red";
		Dispatcher.UIThread.RunJobs();

		field.Text.Should().Be("red");
	}

	private static (Window Window, TextBox Field) ShowHex(string text)
	{
		var (window, field) = Show(FieldLayout.HexColor);
		field.Text = text;
		Dispatcher.UIThread.RunJobs();

		return (window, field);
	}

	private static (Window Window, TextBox Field) Show(FieldLayout layout)
	{
		var field = new TextBox { Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
		FormattedFieldInput.SetLayout(field, layout);
		var window = new Window { Width = 400, Height = 60, Content = field };
		window.Show();

		return (window, field);
	}
}
