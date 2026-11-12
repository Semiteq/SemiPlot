using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Navigation;

using SkiaSharp;

using Xunit;

using static SemiPlot.Tests.Unit.UI.ViewProbe;

namespace SemiPlot.Tests.Unit.UI.Navigation;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class NavigationBarViewTests
{
	private const string SeparatorClass = "groupSeparator";
	private const double DefaultWindowWidth = 1280.0;
	private const double NavigationBarPadding = 8.0;
	private const double StepWidth = 32.0;

	private static readonly Type[] _barControlTypes =
	[
		typeof(StackPanel), typeof(Button), typeof(ToggleButton), typeof(DropDownButton), typeof(Border),
		typeof(TextBlock), typeof(AccessText), typeof(PathIcon)
	];

	[AvaloniaFact]
	public void BarCaptions_ComeFromTheGeneratedResourceAccessor()
	{
		using var stand = NavigationBarStand.Show();

		var captions = stand.View.GetLogicalDescendants()
			.OfType<ContentControl>()
			.Select(control => control.Content as string)
			.ToList();

		captions.Should()
			.Contain(Resources.NavigationJumpToNow)
			.And.Contain(Resources.NavigationSticky)
			.And.Contain(Resources.NavigationLatestRun)
			.And.Contain(Resources.NavigationRecipeRun)
			.And.Contain(Resources.NavigationMarkers)
			.And.Contain(Resources.NavigationDelta);
	}

	// No text box and no axis caption may return to the bar; a control outside the list fails here first.
	[AvaloniaFact]
	public void TheBar_CarriesNoAxisEditor()
	{
		using var stand = NavigationBarStand.Show();
		var controls = stand.View.GetLogicalDescendants().OfType<Control>().ToList();

		controls.OfType<TextBox>().Should().BeEmpty();
		controls.Select(control => control.GetType()).Should().OnlyContain(type => _barControlTypes.Contains(type));
		controls.OfType<ContentControl>()
			.Should()
			.HaveCount(
				8, "Jump to now, Sticky, Recipe run, two run steps, Latest run, Markers and Delta are the whole bar");
		controls.OfType<DropDownButton>().Select(button => button.Name).Should().Equal(
			"RecipeRunsButton", "MarkersButton");
	}

	[AvaloniaFact]
	public void TheThreeGroups_AreSeparatedByHairlinesOfOneStyleClass()
	{
		using var stand = NavigationBarStand.Show();
		var controls = stand.View.GetLogicalDescendants().OfType<Control>().ToList();

		var separators = controls.OfType<Border>().Where(border => border.Classes.Contains(SeparatorClass)).ToList();

		separators.Should().HaveCount(2, "the time group, the run group and the tool group");
		separators.Should().AllSatisfy(separator => separator.Width.Should().Be(1.0));
		var names = controls.Select(control => control.Name).OfType<string>().ToList();
		names.Should().OnlyHaveUniqueItems();
	}

	[AvaloniaFact]
	public void TheHairlines_StandFourPixelsInsideTheBar()
	{
		using var stand = NavigationBarStand.Show();
		var bar = (StackPanel)stand.View.Content!;

		var separators = Descendants<Border>(stand.View).Where(border => border.Classes.Contains(SeparatorClass));

		foreach (var separator in separators)
		{
			TopOf(separator, bar).Should().Be(4, "an inset inside the bar");
			(bar.Bounds.Height - BottomOf(separator, bar)).Should().Be(4, "an inset inside the bar");
		}
	}

	[AvaloniaFact]
	public void TheRunSteps_AreChevronsOfTheBarsHeight_AndLatestRunTakesTheThemesInsets()
	{
		using var stand = NavigationBarStand.Show();

		stand.Named<Button>("PreviousRunButton").Bounds.Size.Should().Be(new Size(StepWidth, StepWidth));
		stand.Named<Button>("NextRunButton").Bounds.Size.Should().Be(new Size(StepWidth, StepWidth));
		ShouldTakeTheThemesButtonInsets(stand.Named<Button>("LatestRunButton"), "a text button");
	}

	[AvaloniaFact]
	public void AClickOnNextRun_FramesTheRunWithATenthOfItsLengthOnEachSide()
	{
		using var stand = NavigationBarStand.Show();
		stand.Push(ThreeRuns());
		stand.Chart.Navigation.SetWindow(At(7, 0), At(7, 30));

		HeadlessInput.Click(stand.Window, stand.Named<Button>("NextRunButton"));

		stand.WindowOfChart().Should().Be((At(7, 58), At(8, 22)));
	}

	[AvaloniaFact]
	public void AClickOnLatestRun_FramesTheNewestRun()
	{
		using var stand = NavigationBarStand.Show();
		stand.Push(ThreeRuns());
		stand.Chart.Navigation.SetWindow(At(6, 0), At(7, 0));

		HeadlessInput.Click(stand.Window, stand.Named<Button>("LatestRunButton"));

		stand.WindowOfChart().Should().Be((At(9, 58), At(10, 22)));
	}

	[AvaloniaFact]
	public void AListItem_CentresItsMarker_KeepingTheWidth()
	{
		using var stand = NavigationBarStand.Show();
		var older = Marker(1, At(9, 0), MarkerKind.User);
		var newer = Marker(3, At(10, 0), MarkerKind.User);
		stand.Push(older, Marker(2, At(9, 30), MarkerKind.RecipeStart), newer);

		var rows = stand.OpenMarkerList();
		rows.Should().HaveCount(2, "recipe markers never reach the list");
		HeadlessInput.Click(TopLevel.GetTopLevel(rows[1])!, rows[1]);

		stand.WindowOfChart().Should().Be((At(8, 30), At(9, 30)));
		stand.MarkerList.IsOpen.Should().BeFalse("a click on a row closes the list");
	}

	[AvaloniaFact]
	public void TheListShowsTheNewestMarkerFirst_AsLabelAndDate_AndAMarkerWithNoLabelAsItsDateAlone()
	{
		using var culture = new CultureScope("en-US");
		using var stand = NavigationBarStand.Show();
		var labelled = Marker(1, At(9, 0), MarkerKind.User);
		var unlabelled = new StoredMarker(2, At(10, 30), Label: null, MarkerKind.User, IsLocked: false);
		stand.Push(labelled, unlabelled);

		var rows = stand.OpenMarkerList().Select(row => row.DataContext).Cast<NavigationListItem>().ToList();

		rows.Select(row => (row.Name, row.DateText)).Should().Equal(
			(string.Empty, LocalDate(At(10, 30))), ("#1", LocalDate(At(9, 0))));
	}

	[AvaloniaFact]
	public void WithNoMarkers_EveryRunControlAndTheListAreDisabled_AndSayWhy()
	{
		using var stand = NavigationBarStand.Show();

		foreach (var name in new[] { "PreviousRunButton", "NextRunButton", "LatestRunButton" })
		{
			var button = stand.Named<Button>(name);
			button.IsEffectivelyEnabled.Should().BeFalse(name);
			ToolTip.GetTip(button).Should().Be(Resources.NavigationNoRecipeRunsTip, name);
			ToolTip.GetShowOnDisabled(button).Should().BeTrue(name);
		}

		var markers = stand.Named<DropDownButton>("MarkersButton");
		markers.IsEffectivelyEnabled.Should().BeFalse();
		ToolTip.GetTip(markers).Should().Be(Resources.NavigationNoMarkersTip);
		ToolTip.GetShowOnDisabled(markers).Should().BeTrue();
		var runs = stand.Named<DropDownButton>("RecipeRunsButton");
		runs.IsEffectivelyEnabled.Should().BeFalse();
		ToolTip.GetTip(runs).Should().Be(Resources.NavigationNoRecipeRunsTip);
		ToolTip.GetShowOnDisabled(runs).Should().BeTrue();
	}

	[AvaloniaFact]
	public void AtTheNewestRun_TheNextStepSaysNoLaterRun_AndTheOthersNameTheirGesture()
	{
		using var stand = NavigationBarStand.Show();
		stand.Push([.. ThreeRuns(), Marker(7, At(11, 0), MarkerKind.User)]);

		var next = stand.Named<Button>("NextRunButton");
		next.IsEffectivelyEnabled.Should().BeFalse("the window lies after every run");
		ToolTip.GetTip(next).Should().Be(Resources.NavigationNoLaterRunTip);
		ToolTip.GetTip(stand.Named<Button>("PreviousRunButton")).Should().Be(WindowGestures.PreviousRunTip);
		ToolTip.GetTip(stand.Named<Button>("LatestRunButton")).Should().Be(WindowGestures.LatestRunTip);
		ToolTip.GetTip(stand.Named<DropDownButton>("MarkersButton")).Should().Be(WindowGestures.MarkerListTip);
		ToolTip.GetTip(stand.Named<DropDownButton>("RecipeRunsButton")).Should().Be(WindowGestures.RecipeRunListTip);

		HeadlessInput.Click(stand.Window, stand.Named<Button>("PreviousRunButton"));
		HeadlessInput.Click(stand.Window, stand.Named<Button>("PreviousRunButton"));
		HeadlessInput.Click(stand.Window, stand.Named<Button>("PreviousRunButton"));

		var previous = stand.Named<Button>("PreviousRunButton");
		previous.IsEffectivelyEnabled.Should().BeFalse("the first run is framed");
		ToolTip.GetTip(previous).Should().Be(Resources.NavigationNoEarlierRunTip);
		ToolTip.GetTip(next).Should().Be(WindowGestures.NextRunTip);
	}

	// Measured in Inter, because the headless shaper advances every glyph by the font size. The readout is a sample:
	// the widest window's delta time and a five-digit difference.
	[AvaloniaTheory]
	[InlineData("en-US")]
	[InlineData("ru-RU")]
	public void TheWholeBar_FitsTheDefaultWindow_WithAWideDeltaReadout(string cultureName)
	{
		using var culture = new CultureScope(cultureName);
		using var stand = NavigationBarStand.Show();
		using var typeface = InterMeasure.InterTypeface();
		var bar = (StackPanel)stand.View.Content!;

		var children = bar.Children.Where(child => child.IsVisible).ToList();
		var width = bar.Spacing * children.Count;
		foreach (var child in children)
		{
			width += InterWidthOf(stand.Window, child, typeface);
		}

		var readout = ChartDeltaCursorReader.FormatReadout(
			new DeltaReadout(TrendNavigationModel.MaximumWidth - TimeSpan.FromSeconds(1.0), -99999.999), penMask: null);
		using var font = InterMeasure.InterFont(typeface, stand.Named<DropDownButton>("RecipeRunsButton").FontSize);
		width += font.MeasureText(readout);
		TestContext.Current.TestOutputHelper?.WriteLine($"{cultureName}: bar {width:0.0} px with '{readout}'");

		width.Should().BeLessThanOrEqualTo(DefaultWindowWidth - (2.0 * NavigationBarPadding));
	}

	private static double InterWidthOf(Window window, Control child, SKTypeface typeface)
	{
		switch (child)
		{
			case ContentControl { Content: string } control:
				return InterMeasure.ContentWidthInInter(window, control, typeface);
			case TextBlock text:
				using (var font = InterMeasure.InterFont(typeface, text.FontSize))
				{
					return font.MeasureText(text.Text);
				}

			default:
				return UnconstrainedWidthOf(window, child);
		}
	}

	/// <summary>08:00-08:20 ended, 09:00-09:30 interrupted, 10:00-10:20 ended.</summary>
	private static StoredMarker[] ThreeRuns()
	{
		return
		[
			Marker(1, At(8, 0), MarkerKind.RecipeStart),
			Marker(2, At(8, 20), MarkerKind.RecipeEnd),
			Marker(3, At(9, 0), MarkerKind.RecipeStart),
			Marker(4, At(9, 30), MarkerKind.RecipeInterrupted),
			Marker(5, At(10, 0), MarkerKind.RecipeStart),
			Marker(6, At(10, 20), MarkerKind.RecipeEnd)
		];
	}

	private static StoredMarker Marker(long id, DateTime timeUtc, MarkerKind kind)
	{
		return new StoredMarker(id, timeUtc, "#" + id, kind, IsLocked: kind != MarkerKind.User);
	}

	private static DateTime At(int hour, int minute)
	{
		return NavigationBarStand.At(hour, minute);
	}

	private static string LocalDate(DateTime timeUtc)
	{
		return timeUtc.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
	}
}
