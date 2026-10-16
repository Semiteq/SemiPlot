using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;

using AwesomeAssertions;

using SemiPlot.UI.Navigation;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Navigation;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TimeFieldInputTests
{
	[AvaloniaTheory]
	[InlineData("083000", "08:30:00")]
	[InlineData("08:30:00", "08:30:00")]
	[InlineData("0830", "08:30")]
	[InlineData("0830001", "08:30:00")]
	[InlineData("8a3", "83")]
	public void AClockTimeField_PutsAColonAfterEveryTwoDigitsFromTheLeft(string typed, string shown)
	{
		var (window, field) = Show(TimeFieldLayout.ClockTime);

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
		var (window, field) = Show(TimeFieldLayout.Date);

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
		var (window, field) = Show(TimeFieldLayout.Duration);

		HeadlessInput.Type(window, field, typed);

		field.Text.Should().Be(shown);
	}

	[AvaloniaFact]
	public void ADigitThatOpensAGroup_LandsAfterTheNewColonWithTheCaretAtTheEnd()
	{
		var (window, field) = Show(TimeFieldLayout.ClockTime);
		field.Text = "08:30";
		Dispatcher.UIThread.RunJobs();

		HeadlessInput.Type(window, field, "0");

		field.Text.Should().Be("08:30:0");
		field.CaretIndex.Should().Be(field.Text.Length);
	}

	[AvaloniaFact]
	public void SelectAllAndBackspace_EmptiesAFilledField()
	{
		var (window, field) = Show(TimeFieldLayout.ClockTime);
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

	private static (Window Window, TextBox Field) Show(TimeFieldLayout layout)
	{
		var field = new TextBox { Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
		TimeFieldInput.SetLayout(field, layout);
		var window = new Window { Width = 400, Height = 60, Content = field };
		window.Show();

		return (window, field);
	}
}
