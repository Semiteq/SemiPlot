using System.Globalization;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Trends;
using SemiPlot.UI;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Legend;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Startup;

using Xunit;

using Pen = SemiPlot.Core.Trends.Pen;

namespace SemiPlot.Tests.Unit.UI.Legend;

/// <summary>The realised sidebar: nothing here is a compiled binding, so the row is read off the tree.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendLegendViewTests
{
	private static readonly TimeSpan _historyDebounceWindow = TimeSpan.FromMilliseconds(150);
	private static readonly DateTime _from = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _to = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);
	private readonly TestScheduler _scheduler = new();

	[AvaloniaFact]
	public void TheRow_ShowsTheNameTheMaskedValueAndTheUnit()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000", "kPa", "0.000"));
		using var legend = new TrendLegendViewModel(chart);
		LoadInitialHistory(chart);
		var window = Realize(legend);

		var texts = RowTexts(SingleRow(window));

		texts.Should().Equal("Chamber pressure", PenValueFormat.Format(2.0, "0.000"), "kPa");
	}

	[AvaloniaFact]
	public void TheRow_WithNeitherMaskNorUnit_ShowsTheValueUnderTheFallbackMask()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart);
		LoadInitialHistory(chart);
		var window = Realize(legend);

		var texts = RowTexts(SingleRow(window));

		texts.Should().Equal("Chamber pressure", PenValueFormat.Format(2.0, PenValueFormat.FallbackMask));
	}

	// The state sits on the panel and the row reads it off the view's data context.
	[AvaloniaFact]
	public void TheCollapsedRow_ShowsTheNameAlone()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000", "kPa", "0.000"));
		using var legend = new TrendLegendViewModel(chart);
		LoadInitialHistory(chart);
		var window = Realize(legend);

		PressTheToggle(window);

		RowTexts(SingleRow(window)).Should().Equal("Chamber pressure");
	}

	[AvaloniaFact]
	public void TheRowExpandedAgain_CarriesTheMaskedValueAndTheUnitBack()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000", "kPa", "0.000"));
		using var legend = new TrendLegendViewModel(chart);
		LoadInitialHistory(chart);
		var window = Realize(legend);

		PressTheToggle(window);
		PressTheToggle(window);

		RowTexts(SingleRow(window))
			.Should()
			.Equal("Chamber pressure", PenValueFormat.Format(2.0, "0.000"), "kPa");
	}

	// The label is read off the realised button rather than off the flag, because the button is what
	// tells the operator which way the next press goes.
	[AvaloniaFact]
	public void ThePanelWidthAndTheToggleLabel_FollowTheState()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000", "kPa", "0.000"));
		using var legend = new TrendLegendViewModel(chart);
		var window = Realize(legend);

		legend.IsExpanded.Should().BeTrue("every start opens expanded");
		legend.PanelWidth.Should().Be(TrendLegendViewModel.ExpandedWidth);
		ToggleLabel(window).Should().Be(Resources.LegendCollapsePanel);

		PressTheToggle(window);

		legend.IsExpanded.Should().BeFalse();
		legend.PanelWidth.Should().Be(TrendLegendViewModel.CollapsedWidth);
		ToggleLabel(window).Should().Be(Resources.LegendExpandPanel);
	}

	// An allowlist, not a denylist: a control type nobody thought of is how a second editor arrives in a
	// panel that is meant to be read-only but for the visibility box.
	[AvaloniaFact]
	public void TheRowTemplate_CarriesOnlyReadOnlyControlsAndOneVisibilityBox()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000", "kPa", "0.000"));
		using var legend = new TrendLegendViewModel(chart);
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
	public void TheGroupHeaderTemplate_CarriesOnlyReadOnlyControlsAndOneSwitch()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000", "kPa", "0.000"));
		using var legend = new TrendLegendViewModel(chart);
		var window = Realize(legend);
		var header = Descendants<Grid>(window).Single(grid => grid.Name == "GroupHeaderRow");

		var controls = header
			.GetLogicalDescendants()
			.OfType<Control>()
			.Prepend(header)
			.ToList();

		controls.Should().AllSatisfy(control =>
			(control is Grid or TextBlock or CheckBox)
				.Should()
				.BeTrue(
					"'{0}' is not one of the three read-only controls the group header is allowed",
					control.GetType().Name));
		controls.OfType<CheckBox>().Should().ContainSingle();
	}

	// Every text editor Avalonia ships puts a TextBox in its template.
	[AvaloniaFact]
	public void ThePanel_RealisesNoTextEditor()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000", "kPa", "0.000"));
		using var legend = new TrendLegendViewModel(chart);
		var window = Realize(legend);

		SingleRow(window).GetLogicalDescendants().OfType<TextBlock>().Should().NotBeEmpty();
		Descendants<TextBox>(window).Should().BeEmpty();
	}

	[AvaloniaFact]
	public void TheColourSwatch_IsARoundDot()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart);
		var window = Realize(legend);

		var swatch = SingleRow(window)
			.GetLogicalDescendants()
			.OfType<Border>()
			.Single(border => border.Width == 12);

		swatch.Height.Should().Be(12);
		swatch.CornerRadius.Should().Be(new CornerRadius(6));
	}

	[AvaloniaFact]
	public void APenInTwoGroups_RendersUnderBothHeadersAndSwitchesOffInBoth()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Heaters", "Pressures"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart);
		var window = Realize(legend);

		HeaderTexts(window).Should().Equal(Caption("Heaters"), Caption("Pressures"));
		var boxes = RowBoxes(window);
		boxes.Should().HaveCount(2);

		boxes[0].IsChecked = false;
		Dispatcher.UIThread.RunJobs();

		boxes[1].IsChecked.Should().BeFalse();
		chart.FindPen(1)!.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ACatalogueWithNoGroupAtAll_RendersNoHeader()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", [], "#ff0000"));
		chart.AddPen(new Pen(2, "Spare", [], "#00ff00"));
		using var legend = new TrendLegendViewModel(chart);
		var window = Realize(legend);

		HeaderTexts(window).Should().BeEmpty();
		HeaderSwitches(window).Should().BeEmpty();
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
		using var legend = new TrendLegendViewModel(chart);
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
		using var legend = new TrendLegendViewModel(chart);
		chart.SetPenVisibility(1, false);
		var window = Realize(legend);
		var headerSwitch = HeaderSwitches(window).Single();
		headerSwitch.IsChecked.Should().BeNull();

		Click(window, headerSwitch);

		headerSwitch.IsChecked.Should().BeTrue();
		chart.Pens.Should().AllSatisfy(pen => pen.IsVisible.Should().BeTrue());

		Click(window, headerSwitch);

		headerSwitch.IsChecked.Should().BeFalse();
		chart.Pens.Should().AllSatisfy(pen => pen.IsVisible.Should().BeFalse());
		RowBoxes(window).Should().AllSatisfy(box => box.IsChecked.Should().BeFalse());

		chart.SetPenVisibility(1, true);
		Dispatcher.UIThread.RunJobs();

		headerSwitch.IsChecked.Should().BeNull();

		Click(window, headerSwitch);

		headerSwitch.IsChecked.Should().BeTrue();
		chart.Pens.Should().AllSatisfy(pen => pen.IsVisible.Should().BeTrue());
	}

	[AvaloniaFact]
	public void APenSwitchedOnTheChart_ReDerivesEveryRealisedHeaderItSitsUnder()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Heater 01", ["Heaters", "Watchlist"], "#ff0000"));
		chart.AddPen(new Pen(2, "Heater 02", ["Heaters"], "#00ff00"));
		using var legend = new TrendLegendViewModel(chart);
		var window = Realize(legend);

		chart.SetPenVisibility(1, false);
		Dispatcher.UIThread.RunJobs();

		HeaderTexts(window).Should().Equal(Caption("Heaters"), Caption("Watchlist"));
		HeaderSwitches(window).Select(box => box.IsChecked).Should().Equal(null, false);
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
		using var legend = new TrendLegendViewModel(chart);
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
		using var legend = new TrendLegendViewModel(chart);
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
		using var legend = new TrendLegendViewModel(chart);
		var window = Realize(legend);
		var header = Descendants<TextBlock>(window).Single(block => block.Name == "GroupHeader");
		var rowBox = RowBoxes(window).Single();

		LeftEdge(rowBox, window).Should().Be(LeftEdge(header, window));
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
		using var legend = new TrendLegendViewModel(chart);
		var window = Realize(legend);

		MarkedRows(window, variant).Should().Equal("Damper 01");

		chart.SetActivePen(2).Should().BeTrue();
		Dispatcher.UIThread.RunJobs();

		MarkedRows(window, variant).Should().Equal("Damper 02");
	}

	private static Window Realize(TrendLegendViewModel legend)
	{
		var window = new Window { Width = 320, Height = 400, Content = new TrendLegendView { DataContext = legend } };

		window.Show();
		Dispatcher.UIThread.RunJobs();

		return window;
	}

	// Through the button's own command, so the header's wiring is under the assertion and not the flag alone.
	private static void PressTheToggle(Window window)
	{
		var toggle = Descendants<Button>(window).Single(button => button.Name == "PanelStateToggle");
		toggle.Command.Should().NotBeNull("the header button carries the command that flips the state");

		toggle.Command.Execute(toggle.CommandParameter);
		Dispatcher.UIThread.RunJobs();
	}

	private static string? ToggleLabel(Window window)
	{
		return Descendants<Button>(window).Single(button => button.Name == "PanelStateToggle").Content as string;
	}

	private static void Click(Window window, Control control)
	{
		var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
			?? throw new InvalidOperationException("The control is not in the window's visual tree.");

		window.MouseDown(center, MouseButton.Left);
		window.MouseUp(center, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();
	}

	private static IReadOnlyList<CheckBox> HeaderSwitches(Window window)
	{
		return [.. Descendants<CheckBox>(window).Where(box => box.Name == "GroupSwitch" && box.IsEffectivelyVisible)];
	}

	private static IReadOnlyList<CheckBox> RowBoxes(Window window)
	{
		return [.. Descendants<CheckBox>(window).Where(box => box.Name != "GroupSwitch")];
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
		return row.GetLogicalDescendants().OfType<TextBlock>().Single(block => Grid.GetColumn(block) == 2);
	}

	private static Color? ColourOf(IBrush? brush)
	{
		return (brush as ISolidColorBrush)?.Color;
	}

	private static double LeftEdge(Control control, Window window)
	{
		return control.TranslatePoint(default, window)?.X
			?? throw new InvalidOperationException("The control is not in the window's visual tree.");
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

	private static IEnumerable<T> Descendants<T>(Window window)
		where T : Control
	{
		return window.GetVisualDescendants().OfType<T>();
	}

	private void LoadInitialHistory(TrendChartViewModel chart)
	{
		chart.Navigation.TrackDataExtents(_from, _to);
		chart.RequestInitialHistory();
		_scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);
	}

	private TrendChartViewModel CreateChart()
	{
		return LegendChartBuilder.CreateChart(_scheduler);
	}
}
