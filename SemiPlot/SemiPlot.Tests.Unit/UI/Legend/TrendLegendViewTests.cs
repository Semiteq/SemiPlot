using System.Globalization;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Chart;
using SemiPlot.UI;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Legend;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Startup;

using Xunit;

using static SemiPlot.Tests.Unit.UI.ViewProbe;

using Pen = SemiPlot.Core.Trends.Pen;

namespace SemiPlot.Tests.Unit.UI.Legend;

/// <summary>The realised sidebar: nothing here is a compiled binding, so the row is read off the tree.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendLegendViewTests
{
	private readonly TestScheduler _scheduler = new();

	[AvaloniaFact]
	public void TheRow_ShowsTheNameTheMaskedValueAndTheUnit()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000", "kPa", "0.000"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Pressures", [1])]);
		ChartTestBuilder.LoadInitialHistory(chart, _scheduler, ChartTestBuilder.From, ChartTestBuilder.To);
		var window = Realize(legend);

		var texts = RowTexts(SingleRow(window));

		texts.Should().Equal("Chamber pressure", PenValueFormat.Format(2.0, "0.000"), "kPa");
	}

	[AvaloniaFact]
	public void TheRow_WithNeitherMaskNorUnit_ShowsTheValueUnderTheFallbackMask()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Pressures", [1])]);
		ChartTestBuilder.LoadInitialHistory(chart, _scheduler, ChartTestBuilder.From, ChartTestBuilder.To);
		var window = Realize(legend);

		var texts = RowTexts(SingleRow(window));

		texts.Should().Equal("Chamber pressure", PenValueFormat.Format(2.0, PenValueFormat.FallbackMask));
	}

	// The state sits on the panel and the row reads it off the view's data context. The label is read off the
	// realised button rather than off the flag, because the button is what tells the operator which way the
	// next press goes.
	[AvaloniaFact]
	public void ThePanelToggle_HidesTheRowsValuesAndShowsThemBack()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000", "kPa", "0.000"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Pressures", [1])]);
		ChartTestBuilder.LoadInitialHistory(chart, _scheduler, ChartTestBuilder.From, ChartTestBuilder.To);
		var window = Realize(legend);

		legend.IsExpanded.Should().BeTrue("every start opens expanded");
		legend.PanelWidth.Should().Be(TrendLegendViewModel.ExpandedWidth);
		ToggleLabel(window).Should().Be(Resources.LegendHideValues);
		ToggleTip(window).Should().Be(Resources.LegendHideValuesTip);

		PressTheToggle(window);

		legend.IsExpanded.Should().BeFalse();
		legend.PanelWidth.Should().Be(TrendLegendViewModel.CollapsedWidth);
		ToggleLabel(window).Should().Be(Resources.LegendShowValues);
		ToggleTip(window).Should().Be(Resources.LegendShowValuesTip);
		RowTexts(SingleRow(window)).Should().Equal("Chamber pressure");

		PressTheToggle(window);

		RowTexts(SingleRow(window))
			.Should()
			.Equal("Chamber pressure", PenValueFormat.Format(2.0, "0.000"), "kPa");
	}

	// An allowlist, not a denylist: a control type nobody thought of is how a second editor arrives in a
	// panel that is meant to be read-only but for the visibility box.
	[AvaloniaFact]
	public void TheRowTemplate_CarriesOnlyReadOnlyControlsAndOneVisibilityBox()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000", "kPa", "0.000"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Pressures", [1])]);
		var window = Realize(legend);
		var row = SingleRow(window);

		var controls = row
			.GetLogicalDescendants()
			.OfType<Control>()
			.Prepend(row)
			.ToList();

		controls.Should().AllSatisfy(control =>
			(control is Border or Grid or TextBlock or CheckBox)
				.Should()
				.BeTrue(
					"'{0}' is not one of the four read-only controls the row is allowed",
					control.GetType().Name));
		controls.OfType<CheckBox>().Should().ContainSingle();
	}

	[AvaloniaFact]
	public void TheGroupHeaderTemplate_CarriesOnlyReadOnlyControlsOneSwitchAndOneFoldToggle()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000", "kPa", "0.000"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Pressures", [1])]);
		var window = Realize(legend);
		var header = Descendants<Grid>(window).Single(grid => grid.Name == "GroupHeaderRow");

		var controls = header
			.GetLogicalDescendants()
			.OfType<Control>()
			.Prepend(header)
			.ToList();

		controls.Should().AllSatisfy(control =>
			(control is Border or Grid or TextBlock or PathIcon or CheckBox or ToggleButton)
				.Should()
				.BeTrue(
					"'{0}' is not one of the controls the group header is allowed",
					control.GetType().Name));
		controls.OfType<CheckBox>().Should().ContainSingle();
		controls.OfType<ToggleButton>().Where(control => control is not CheckBox).Should().ContainSingle();
	}

	// Every text editor Avalonia ships puts a TextBox in its template.
	[AvaloniaFact]
	public void ThePanel_RealisesNoTextEditor()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000", "kPa", "0.000"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Pressures", [1])]);
		var window = Realize(legend);

		SingleRow(window).GetLogicalDescendants().OfType<TextBlock>().Should().NotBeEmpty();
		Descendants<TextBox>(window).Should().BeEmpty();
	}

	[AvaloniaFact]
	public void TheColourSwatch_IsARoundDot()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Pressures", [1])]);
		var window = Realize(legend);

		var swatch = Descendants<Border>(window).Single(border => border.Name == "RowColor");

		swatch.Height.Should().Be(12);
		swatch.CornerRadius.Should().Be(new CornerRadius(6));
	}

	[AvaloniaFact]
	public void APenInTwoGroups_RendersUnderBothHeadersAndSwitchesOffInBoth()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Heaters", "Pressures"], "#ff0000"));
		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1]), new StoredGroup(2, "Pressures", [1])]);
		var window = Realize(legend);

		HeaderTexts(window).Should().Equal(Caption("Heaters"), Caption("Pressures"));
		var boxes = RowBoxes(window);
		boxes.Should().HaveCount(2);

		HeadlessInput.Click(window, boxes[0]);

		boxes[1].IsChecked.Should().BeFalse();
		chart.FindPen(1)!.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ACatalogueWithNoGroupAtAll_RendersNoHeader()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", [], "#ff0000"));
		chart.AddPen(new Pen(2, "Spare", [], "#00ff00"));
		using var legend = new TrendLegendViewModel(chart, []);
		var window = Realize(legend);

		HeaderTexts(window).Should().BeEmpty();
		HeaderSwitches(window).Should().BeEmpty();
		FoldToggles(window).Should().BeEmpty();
		Descendants<Border>(window).Count(border => border.Name == "RowRoot").Should().Be(2);
	}

	// Switching pen 3 off leaves one header of each state, and the ungrouped header carries a box of its own.
	[AvaloniaFact]
	public void EveryDrawnHeader_CarriesOneSwitchShowingItsDerivedState()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Heater 01", ["Heaters", "Watchlist"], "#ff0000"));
		chart.AddPen(new Pen(2, "Heater 02", ["Heaters"], "#00ff00"));
		chart.AddPen(new Pen(3, "Chamber pressure", ["Pressures", "Watchlist"], "#0000ff"));
		chart.AddPen(new Pen(4, "Spare", [], "#ffff00"));
		using var legend = new TrendLegendViewModel(
			chart,
			[
				new StoredGroup(1, "Heaters", [1, 2]),
				new StoredGroup(2, "Watchlist", [1, 3]),
				new StoredGroup(3, "Pressures", [3])
			]);
		chart.SetPenVisibility(3, false);
		var window = Realize(legend);

		HeaderTexts(window)
			.Should()
			.Equal(Caption("Heaters"), Caption("Pressures"), Caption("Watchlist"), Caption(Resources.LegendUngroupedHeader));
		HeaderSwitches(window)
			.Select(box => box.IsChecked)
			.Should()
			.Equal(true, false, null, true);
		HeaderSwitches(window)
			.Select(AutomationProperties.GetName)
			.Should()
			.Equal(
				Resources.FormatLegendGroupSwitch("Heaters"),
				Resources.FormatLegendGroupSwitch("Pressures"),
				Resources.FormatLegendGroupSwitch("Watchlist"),
				Resources.FormatLegendGroupSwitch(Resources.LegendUngroupedHeader));
	}

	// A click toggles the box before the command runs; only on a mixed box does that toggle (off) differ from
	// the derived result (on), so the first and the last click fail if a click replaced the one-way binding,
	// and the chart re-mixing the header between them shows the binding still live after two clicks.
	[AvaloniaFact]
	public void ClicksOnTheHeader_SwitchTheGroupOnThenOffAndAMixedHeaderOnAgain()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Heater 01", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Heater 02", ["Heaters"], "#00ff00"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1, 2])]);
		chart.SetPenVisibility(1, false);
		var window = Realize(legend);
		var headerSwitch = HeaderSwitches(window).Single();
		headerSwitch.IsChecked.Should().BeNull();

		HeadlessInput.Click(window, headerSwitch);

		headerSwitch.IsChecked.Should().BeTrue();
		chart.Pens.Should().AllSatisfy(pen => pen.IsVisible.Should().BeTrue());

		HeadlessInput.Click(window, headerSwitch);

		headerSwitch.IsChecked.Should().BeFalse();
		chart.Pens.Should().AllSatisfy(pen => pen.IsVisible.Should().BeFalse());
		RowBoxes(window).Should().AllSatisfy(box => box.IsChecked.Should().BeFalse());

		chart.SetPenVisibility(1, true);
		Dispatcher.UIThread.RunJobs();

		headerSwitch.IsChecked.Should().BeNull();

		HeadlessInput.Click(window, headerSwitch);

		headerSwitch.IsChecked.Should().BeTrue();
		chart.Pens.Should().AllSatisfy(pen => pen.IsVisible.Should().BeTrue());

		HeadlessInput.Click(window, headerSwitch);

		BoxStates(window).Should().Equal(false, false);
		PenStates(chart).Should().Equal(false, false);

		HeadlessInput.Click(window, headerSwitch);

		BoxStates(window).Should().Equal(true, true);
		PenStates(chart).Should().Equal(true, true);
	}

	// The chart switches the pen back after a click, so the box follows only if its one-way binding
	// outlived the click.
	[AvaloniaFact]
	public void APenSwitchedOnTheChart_MovesItsRowBoxBeforeAndAfterAClick()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Heater 01", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Heater 02", ["Heaters"], "#00ff00"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1, 2])]);
		var window = Realize(legend);

		HeadlessInput.Click(window, RowBoxes(window)[0]);

		BoxStates(window).Should().Equal(false, true);
		PenStates(chart).Should().Equal(false, true);

		HeadlessInput.Click(window, RowBoxes(window)[0]);

		BoxStates(window).Should().Equal(true, true);
		PenStates(chart).Should().Equal(true, true);

		chart.SetPenVisibility(2, false);
		Dispatcher.UIThread.RunJobs();

		BoxStates(window).Should().Equal(true, false);

		HeadlessInput.Click(window, RowBoxes(window)[1]);

		BoxStates(window).Should().Equal(true, true);
		PenStates(chart).Should().Equal(true, true);

		chart.SetPenVisibility(2, false);
		Dispatcher.UIThread.RunJobs();

		BoxStates(window).Should().Equal(true, false);
		PenStates(chart).Should().Equal(true, false);
	}

	[AvaloniaFact]
	public void ARowBoxSetInCode_WritesNothingToThePen()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Heater 01", ["Heaters"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1])]);
		var window = Realize(legend);

		RowBoxes(window).Single().IsChecked = false;
		Dispatcher.UIThread.RunJobs();

		chart.FindPen(1)!.IsVisible.Should().BeTrue("the box is not a writer of the pen's visibility");
	}

	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light)]
	[InlineData(AppThemeVariant.Dark)]
	public void TheGroupHeader_IsASmallerSecondaryCaptionInCapitalsAndNotBold(AppThemeVariant theme)
	{
		var variant = App.VariantFor(theme);
		using var scope = ThemeProbe.ApplyVariant(variant);
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Damper 01", ["Dampers"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Dampers", [1])]);
		var window = Realize(legend);
		var header = Descendants<TextBlock>(window).Single(block => block.Name == "GroupHeader");
		var rowName = RowName(SingleRow(window));

		header.Text.Should().Be(Caption("Dampers"));
		header.FontWeight.Should().Be(FontWeight.Normal);
		rowName.FontWeight.Should().Be(header.FontWeight, "only the row background marks the active pen");
		header.FontSize.Should().BeLessThan(rowName.FontSize);
		ColourOf(header.Foreground).Should().Be(ThemeProbe.Colour("AppSecondaryForegroundBrush", variant));
	}

	[AvaloniaFact]
	public void EveryHeaderButTheFirst_HasALineAboveIt()
	{
		using var scope = ThemeProbe.ApplyVariant(ThemeVariant.Light);
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Heater 01", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Chamber pressure", ["Pressures"], "#00ff00"));
		chart.AddPen(new Pen(3, "Damper 01", ["Dampers"], "#0000ff"));
		using var legend = new TrendLegendViewModel(
			chart,
			[
				new StoredGroup(1, "Heaters", [1]),
				new StoredGroup(2, "Pressures", [2]),
				new StoredGroup(3, "Dampers", [3])
			]);
		var window = Realize(legend);

		var separators = Descendants<Border>(window).Where(border => border.Name == "GroupSeparator").ToList();

		separators.Select(separator => separator.IsEffectivelyVisible).Should().Equal(false, true, true);
		separators.Should().AllSatisfy(separator =>
			ColourOf(separator.Background).Should().Be(ThemeProbe.Colour("AppSubtleLineBrush", ThemeVariant.Light)));
	}

	[AvaloniaFact]
	public void ARowUnderAHeader_StartsItsBoxWhereTheCaptionStarts()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Damper 01", ["Dampers"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Dampers", [1])]);
		var window = Realize(legend);
		var header = Descendants<TextBlock>(window).Single(block => block.Name == "GroupHeader");
		var rowBox = RowBoxes(window).Single();

		LeftOf(rowBox, window).Should().Be(LeftOf(header, window));
	}

	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light)]
	[InlineData(AppThemeVariant.Dark)]
	public void OnlyTheActiveRow_CarriesTheBarAndTheFill_AndAPenActivatedOnTheChartMovesThem(AppThemeVariant theme)
	{
		var variant = App.VariantFor(theme);
		using var scope = ThemeProbe.ApplyVariant(variant);
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Damper 01", ["Dampers"], "#ff0000"));
		chart.AddPen(new Pen(2, "Damper 02", ["Dampers"], "#00ff00"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Dampers", [1, 2])]);
		var window = Realize(legend);

		MarkedRows(window, variant).Should().Equal("Damper 01");

		chart.SetActivePen(2).Should().BeTrue();
		Dispatcher.UIThread.RunJobs();

		MarkedRows(window, variant).Should().Equal("Damper 02");
	}

	[AvaloniaFact]
	public void AClickOnTheFoldToggle_HidesTheRowsAndASecondShowsThem_AndTheChartDoesNotChange()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Heater 01", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Heater 02", ["Heaters"], "#00ff00"));
		chart.AddPen(new Pen(3, "Chamber pressure", ["Pressures"], "#0000ff"));
		chart.SetPenVisibility(2, false);
		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1, 2]), new StoredGroup(2, "Pressures", [3])]);
		var window = Realize(legend);
		var chartBefore = ChartState(chart);
		var toggle = FoldToggles(window)[0];
		toggle.Command.Should().NotBeNull("the toggle writes the fold through the group's command");
		toggle.IsChecked.Should().BeFalse();
		AutomationProperties.GetName(toggle).Should().Be(Resources.FormatLegendFoldGroup("Heaters"));
		ChevronOf(toggle).Should().BeSameAs(Icon("ChevronUpGeometry"));

		HeadlessInput.Click(window, toggle);

		VisibleRowNames(window).Should().Equal("Chamber pressure");
		HeaderTexts(window).Should().Equal(Caption("Heaters"), Caption("Pressures"));
		toggle.IsChecked.Should().BeTrue();
		AutomationProperties.GetName(toggle).Should().Be(Resources.FormatLegendUnfoldGroup("Heaters"));
		ToolTip.GetTip(toggle).Should().Be(Resources.FormatLegendUnfoldGroup("Heaters"));
		ChevronOf(toggle).Should().BeSameAs(Icon("ChevronDownGeometry"));
		ChartState(chart).Should().Equal(chartBefore);

		HeadlessInput.Click(window, toggle);

		VisibleRowNames(window).Should().Equal("Heater 01", "Heater 02", "Chamber pressure");
		toggle.IsChecked.Should().BeFalse();
		ChevronOf(toggle).Should().BeSameAs(Icon("ChevronUpGeometry"));
		ChartState(chart).Should().Equal(chartBefore);
	}

	[AvaloniaFact]
	public void AFoldToggleSetInCode_WritesNothing_AndTheToggleFollowsAnUnfoldThePickMakes()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Heater 01", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Chamber pressure", ["Pressures"], "#0000ff"));
		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1]), new StoredGroup(2, "Pressures", [2])]);
		var window = Realize(legend);
		var pressures = legend.Groups.Single(group => group.Name == "Pressures");
		var pressuresToggle = FoldToggles(window)[1];

		pressuresToggle.IsChecked = true;
		Dispatcher.UIThread.RunJobs();

		pressures.IsFolded.Should().BeFalse("the toggle is not a writer of the fold");
		VisibleRowNames(window).Should().Equal("Heater 01", "Chamber pressure");

		pressures.FoldCommand.Execute().Subscribe();
		Dispatcher.UIThread.RunJobs();

		pressuresToggle.IsChecked.Should().BeTrue();
		VisibleRowNames(window).Should().Equal("Heater 01");

		chart.SetActivePen(2).Should().BeTrue();
		Dispatcher.UIThread.RunJobs();

		pressures.IsFolded.Should().BeFalse();
		pressuresToggle.IsChecked.Should().BeFalse();
		VisibleRowNames(window).Should().Equal("Heater 01", "Chamber pressure");
	}

	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light)]
	[InlineData(AppThemeVariant.Dark)]
	public void AFoldedHeaderHoldingTheActivePen_ShowsTheBar(AppThemeVariant theme)
	{
		var variant = App.VariantFor(theme);
		using var scope = ThemeProbe.ApplyVariant(variant);
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Heater 01", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Chamber pressure", ["Pressures"], "#0000ff"));
		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1]), new StoredGroup(2, "Pressures", [2])]);
		var window = Realize(legend);
		chart.ActivePenId.Should().Be(1);
		var rowBarLeft = LeftOf(Descendants<Border>(window).First(border => border.Name == "ActiveBar"), window);

		HeaderBars(window).Select(bar => bar.IsEffectivelyVisible).Should().Equal(false, false);

		HeadlessInput.Click(window, FoldToggles(window)[1]);

		HeaderBars(window).Select(bar => bar.IsEffectivelyVisible).Should().Equal(false, false);

		HeadlessInput.Click(window, FoldToggles(window)[0]);

		HeaderBars(window).Select(bar => bar.IsEffectivelyVisible).Should().Equal(true, false);
		HeaderBars(window).Should().AllSatisfy(bar =>
			ColourOf(bar.Background).Should().Be(ThemeProbe.Colour("AppAccentBrush", variant)));
		LeftOf(HeaderBars(window)[0], window).Should().Be(rowBarLeft, "the header carries the row's own bar");
	}

	[AvaloniaFact]
	public void TheFoldToggle_ClearsTheScrollbarAtThePanelsNarrowestWidth()
	{
		var chart = CreateChart();
		var penIds = Enumerable.Range(1, 40).ToList();
		foreach (var penId in penIds)
		{
			chart.AddPen(new Pen(penId, $"Heater {penId:00}", ["Heaters"], "#ff0000"));
		}

		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", penIds)]);
		var window = Realize(legend, TrendLegendViewModel.PanelMinWidth, 200);
		var scrollBar = Descendants<ScrollBar>(window)
			.Single(bar => bar.Orientation == Orientation.Vertical && bar.IsEffectivelyVisible);
		var toggle = FoldToggles(window).Single();

		RightOf(toggle, window).Should().BeLessThanOrEqualTo(LeftOf(scrollBar, window));
		toggle.Bounds.Height.Should().BeLessThanOrEqualTo(
			HeaderSwitches(window).Single().Bounds.Height,
			"the toggle does not make the header taller than its switch");
	}

	[AvaloniaFact]
	public void TheRealisedSidebar_ShowsARenamedPensNewNameAfterARebuild()
	{
		var chart = CreateChart();
		var pen = new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000", "kPa", "0.000");
		chart.AddPen(pen);
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Pressures", [1])]);
		var window = Realize(legend);

		chart.ApplyCatalogue([pen with { Name = "Chamber pressure A" }]);
		legend.Rebuild([new StoredGroup(1, "Pressures", [1])]);
		Dispatcher.UIThread.RunJobs();

		RowName(SingleRow(window)).Text.Should().Be("Chamber pressure A");
		RowBoxes(window).Single().IsChecked.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TheRealisedRow_ShowsARevisedUnitWithoutARebuild()
	{
		var chart = CreateChart();
		var pen = new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000", "kPa", "0.000");
		chart.AddPen(pen);
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Pressures", [1])]);
		ChartTestBuilder.LoadInitialHistory(chart, _scheduler, ChartTestBuilder.From, ChartTestBuilder.To);
		var window = Realize(legend);

		chart.ApplyCatalogue([pen with { Unit = "Pa" }]);
		Dispatcher.UIThread.RunJobs();

		RowTexts(SingleRow(window)).Should().Equal("Chamber pressure", PenValueFormat.Format(2.0, "0.000"), "Pa");
	}

	private static Window Realize(TrendLegendViewModel legend, double width = 320, double height = 400)
	{
		var window = new Window
		{
			Width = width,
			Height = height,
			Content = new TrendLegendView { DataContext = legend }
		};

		window.Show();
		Dispatcher.UIThread.RunJobs();

		return window;
	}

	// Through the button's own command, so the header's wiring is under the assertion and not the flag alone.
	private static void PressTheToggle(Window window)
	{
		var toggle = Descendant<Button>(window, "PanelStateToggle");
		toggle.Command.Should().NotBeNull("the header button carries the command that flips the state");

		toggle.Command.Execute(toggle.CommandParameter);
		Dispatcher.UIThread.RunJobs();
	}

	private static string? ToggleLabel(Window window)
	{
		return Descendants<TextBlock>(Descendant<Button>(window, "PanelStateToggle")).Single().Text;
	}

	private static string? ToggleTip(Window window)
	{
		return ToolTip.GetTip(Descendant<Button>(window, "PanelStateToggle")) as string;
	}

	private static IReadOnlyList<CheckBox> HeaderSwitches(Window window)
	{
		return [.. Descendants<CheckBox>(window).Where(box => box.Name == "GroupSwitch" && box.IsEffectivelyVisible)];
	}

	private static IReadOnlyList<ToggleButton> FoldToggles(Window window)
	{
		return
		[
			.. Descendants<ToggleButton>(window)
				.Where(toggle => toggle.Name == "GroupFoldToggle" && toggle.IsEffectivelyVisible)
		];
	}

	private static IReadOnlyList<Border> HeaderBars(Window window)
	{
		return [.. Descendants<Border>(window).Where(border => border.Name == "HeaderActiveBar")];
	}

	private static Geometry? ChevronOf(ToggleButton toggle)
	{
		return toggle.GetLogicalDescendants().OfType<PathIcon>().Single().Data;
	}

	private static object? Icon(string key)
	{
		return Application.Current!.FindResource(key);
	}

	private static IReadOnlyList<string> VisibleRowNames(Window window)
	{
		return
		[
			.. Descendants<Border>(window)
				.Where(row => row.Name == "RowRoot" && row.IsEffectivelyVisible)
				.Select(row => RowName(row).Text ?? string.Empty)
		];
	}

	private static IReadOnlyList<(int PenId, bool IsVisible)> ChartState(TrendChartViewModel chart)
	{
		return [.. chart.Pens.Select(pen => (pen.Pen.PenId, pen.IsVisible)), (chart.ActivePenId, true)];
	}

	private static IReadOnlyList<CheckBox> RowBoxes(Window window)
	{
		return [.. Descendants<CheckBox>(window).Where(box => box.Name != "GroupSwitch")];
	}

	private static IReadOnlyList<bool?> BoxStates(Window window)
	{
		return [.. RowBoxes(window).Select(box => box.IsChecked)];
	}

	private static IReadOnlyList<bool> PenStates(TrendChartViewModel chart)
	{
		return [.. chart.Pens.Select(pen => pen.IsVisible)];
	}

	private static Border SingleRow(Window window)
	{
		return Descendants<Border>(window).Single(border => border.Name == "RowRoot");
	}

	private static IReadOnlyList<string> RowTexts(Border row)
	{
		return
		[
			.. row
				.GetLogicalDescendants()
				.OfType<TextBlock>()
				.Where(block => block.IsVisible)
				.Select(block => block.Text ?? string.Empty)
				.Where(text => text.Length > 0)
		];
	}

	private static IReadOnlyList<string> HeaderTexts(Window window)
	{
		return
		[
			.. Descendants<TextBlock>(window)
				.Where(block => block.Name == "GroupHeader" && block.IsEffectivelyVisible)
				.Select(block => block.Text ?? string.Empty)
		];
	}

	private static string Caption(string name)
	{
		return name.ToUpper(CultureInfo.CurrentCulture);
	}

	private static TextBlock RowName(Border row)
	{
		return row.GetLogicalDescendants().OfType<TextBlock>().Single(block => block.Name == "RowName");
	}

	// A row counts as marked only when both the bar shows and the fill paints the accent fill, so either half
	// left behind on the previous row fails the equality.
	private static IReadOnlyList<string> MarkedRows(Window window, ThemeVariant variant)
	{
		var fill = ThemeProbe.Brush("AppAccentFillBrush", variant);
		var bar = ThemeProbe.Colour("AppAccentBrush", variant);
		var rows = Descendants<Border>(window).Where(border => border.Name == "RowRoot").ToList();

		rows.Should().AllSatisfy(row =>
		{
			var barShows = ActiveBar(row).IsEffectivelyVisible;
			var fillShows = row.Background is ISolidColorBrush brush && brush.Color == fill.Color && brush.Opacity == fill.Opacity;
			fillShows.Should().Be(barShows, "the bar and the fill of '{0}' move together", RowName(row).Text);
		});
		rows.Select(ActiveBar).Should().AllSatisfy(activeBar => ColourOf(activeBar.Background).Should().Be(bar));

		return [.. rows.Where(row => ActiveBar(row).IsEffectivelyVisible).Select(row => RowName(row).Text ?? string.Empty)];
	}

	private static Border ActiveBar(Border row)
	{
		return row.GetLogicalDescendants().OfType<Border>().Single(border => border.Name == "ActiveBar");
	}

	private TrendChartViewModel CreateChart()
	{
		return ChartTestBuilder.CreateChart(_scheduler);
	}
}
