using System.Globalization;

using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using SemiPlot.UI;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Messages;

/// <summary>
/// The panel as it renders: the severity dot's colour, the repeat count and the local-time stamp are read
/// back off realised controls, which is the only thing that catches a class nothing selects.
/// </summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Messages")]
[Trait("Category", "Unit")]
public sealed class MessagePanelViewTests
{
	private static readonly DateTimeOffset _stamp = new(2026, 9, 15, 21, 30, 15, TimeSpan.Zero);

	// Neither UTC nor any zone a developer machine or a CI runner is likely to sit in, so the rendered row
	// differs from the UTC rendering wherever the test runs.
	private static readonly TimeZoneInfo _operatorZone = TimeZoneInfo.CreateCustomTimeZone(
		"semiplot-operator", TimeSpan.FromHours(7), "SemiPlot operator", "SemiPlot operator");

	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light, MessageSeverity.Error, "AppSeverityErrorBrush")]
	[InlineData(AppThemeVariant.Light, MessageSeverity.Warning, "AppSeverityWarningBrush")]
	[InlineData(AppThemeVariant.Light, MessageSeverity.Info, "AppSeverityInfoBrush")]
	[InlineData(AppThemeVariant.Dark, MessageSeverity.Error, "AppSeverityErrorBrush")]
	[InlineData(AppThemeVariant.Dark, MessageSeverity.Warning, "AppSeverityWarningBrush")]
	[InlineData(AppThemeVariant.Dark, MessageSeverity.Info, "AppSeverityInfoBrush")]
	public void TheSeverityDot_TakesItsColourFromThePaletteKeyForThatSeverity(
		AppThemeVariant theme, MessageSeverity severity, string key)
	{
		var variant = App.VariantFor(theme);
		using var scope = ThemeProbe.ApplyVariant(variant);
		using var panel = new MessagePanelViewModel(new FixedClock(_stamp));
		panel.Report(new ArchiveFailureView("title", "detail", "remedy", severity));
		var view = new MessagePanelView { DataContext = panel };
		var window = new Window { Content = view };
		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();

			var dot = view.GetVisualDescendants().OfType<Ellipse>().Should().ContainSingle().Subject;

			dot.Fill.Should().BeAssignableTo<ISolidColorBrush>()
				.Which.Color.Should().Be(ThemeProbe.Colour(key, variant));
		}
		finally
		{
			window.Close();
		}
	}

	// The clock decides the operator's zone, so the stamp the row shows is pinned without TimeZoneInfo.Local.
	[AvaloniaFact]
	public void TheEntryRow_ShowsTheLocalTimeAndHidesTheRepeatCountUntilItRepeats()
	{
		using var scope = ThemeProbe.ApplyVariant(ThemeVariant.Light);
		using var panel = new MessagePanelViewModel(new FixedClock(_stamp));
		var failure = new ArchiveFailureView("title", "detail", "remedy", MessageSeverity.Warning);
		panel.Report(failure);
		var view = new MessagePanelView { DataContext = panel };
		var window = new Window { Content = view };
		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();

			var repeated = string.Format(
				CultureInfo.CurrentCulture, Resources.MessagePanelRepeatFormat, 2);

			var operatorClock = TimeZoneInfo.ConvertTime(_stamp, _operatorZone)
				.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
			var utcClock = _stamp.UtcDateTime.ToString("HH:mm:ss", CultureInfo.CurrentCulture);

			VisibleTexts(view).Should().Contain(operatorClock);
			VisibleTexts(view).Should().NotContain(utcClock, "the row shows the operator's clock, not UTC");
			VisibleTexts(view).Should().NotContain(repeated, "one occurrence shows no count");

			panel.Report(failure);
			Dispatcher.UIThread.RunJobs();

			VisibleTexts(view).Should().Contain(repeated, "the second occurrence shows the count");
		}
		finally
		{
			window.Close();
		}
	}

	// The row stands whenever the operator has it open, so an empty list has to say so itself rather than
	// leave a titled strip with nothing under it.
	[AvaloniaFact]
	public void TheEmptyList_ShowsItsOwnLineAndYieldsToTheFirstEntry()
	{
		using var scope = ThemeProbe.ApplyVariant(ThemeVariant.Light);
		using var panel = new MessagePanelViewModel(new FixedClock(_stamp));
		var view = new MessagePanelView { DataContext = panel };
		var window = new Window { Content = view };
		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();

			var empty = view.FindControl<TextBlock>("MessagePanelEmpty");

			empty.Should().NotBeNull();
			empty!.IsVisible.Should().BeTrue();
			empty.Text.Should().Be(Resources.MessagePanelEmpty);

			panel.Report(new ArchiveFailureView("title", "detail", "remedy", MessageSeverity.Error));
			Dispatcher.UIThread.RunJobs();

			empty.IsVisible.Should().BeFalse("the list speaks for itself once it has an entry");
		}
		finally
		{
			window.Close();
		}
	}

	private static IEnumerable<string> VisibleTexts(MessagePanelView view)
	{
		return view.GetVisualDescendants()
			.OfType<TextBlock>()
			.Where(text => text.IsVisible && text.Text is not null)
			.Select(text => text.Text!);
	}

	private sealed class FixedClock(DateTimeOffset now) : TimeProvider
	{
		public override TimeZoneInfo LocalTimeZone => _operatorZone;

		public override DateTimeOffset GetUtcNow()
		{
			return now;
		}
	}
}
