using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
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

/// <summary>The sidebar's top row, the group chevron's paint and the trimmed group caption, realised.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendLegendTopRowViewTests
{
	// docs/architecture/overview.md#the-sidebar-fold
	private const double TextThreshold = 263;

	private readonly TestScheduler _scheduler = new();

	[AvaloniaTheory]
	[InlineData(TrendLegendViewModel.ExpandedWidth, true)]
	[InlineData(TextThreshold, true)]
	[InlineData(TextThreshold - 1, false)]
	[InlineData(TrendLegendViewModel.CollapsedWidth, false)]
	[InlineData(TrendLegendViewModel.PanelMinWidth, false)]
	public void TheTopRow_ShowsTextFromTheThresholdUpAndIconsBelowIt(double width, bool showsText)
	{
		using var legend = TwoGroupLegend(CreateChart());
		var window = Realize(legend, width);

		foreach (var button in TopRowButtons(window))
		{
			LabelOf(button).IsEffectivelyVisible.Should().Be(showsText, "'{0}' at {1} px", button.Name, width);
			IconOf(button).IsEffectivelyVisible.Should().Be(!showsText, "'{0}' at {1} px", button.Name, width);
			button.Bounds.Height.Should().Be(showsText ? 24 : 20, "'{0}' at {1} px", button.Name, width);
		}

		if (!showsText)
		{
			TopRowButtons(window).Should().AllSatisfy(button => button.Bounds.Width.Should().Be(24));
		}

		ToolTip.GetTip(Descendant<Button>(window, "PanelStateToggle")).Should().Be(Resources.LegendHideValuesTip);
		ToolTip.GetTip(Descendant<Button>(window, "FoldAllButton")).Should().Be(Resources.LegendFoldAllGroups);
	}

	[AvaloniaFact]
	public void AResizeAcrossTheThreshold_SwitchesTheRowAndLeavesTheListWhereItWas()
	{
		using var legend = TwoGroupLegend(CreateChart());
		var window = Realize(legend, TrendLegendViewModel.ExpandedWidth);
		var list = Descendants<ScrollViewer>(window).First();
		var listTop = TopOf(list, window);

		window.Width = TrendLegendViewModel.PanelMinWidth;
		Dispatcher.UIThread.RunJobs();

		TopRowButtons(window).Should().AllSatisfy(button => IconOf(button).IsEffectivelyVisible.Should().BeTrue());
		TopOf(list, window).Should().Be(listTop);

		window.Width = TrendLegendViewModel.ExpandedWidth;
		Dispatcher.UIThread.RunJobs();

		TopRowButtons(window).Should().AllSatisfy(button => LabelOf(button).IsEffectivelyVisible.Should().BeTrue());
		TopOf(list, window).Should().Be(listTop);
	}

	[AvaloniaFact]
	public void TheValuesButton_SitsLeftOfFoldAllAndPointsTheIconTheWayThePanelEdgeMoves()
	{
		using var legend = TwoGroupLegend(CreateChart());
		var window = Realize(legend, TrendLegendViewModel.CollapsedWidth);
		var values = Descendant<Button>(window, "PanelStateToggle");
		var foldAll = Descendant<Button>(window, "FoldAllButton");

		RightOf(values, window).Should().BeLessThan(LeftOf(foldAll, window));
		IconOf(values).Data.Should().BeSameAs(Icon("ChevronDoubleRightGeometry"));
		LabelOf(values).Text.Should().Be(Resources.LegendHideValues);

		HeadlessInput.Click(window, values);

		legend.IsExpanded.Should().BeFalse();
		IconOf(values).Data.Should().BeSameAs(Icon("ChevronDoubleLeftGeometry"));
		LabelOf(values).Text.Should().Be(Resources.LegendShowValues);
		ToolTip.GetTip(values).Should().Be(Resources.LegendShowValuesTip);
	}

	[AvaloniaFact]
	public void AClickOnFoldAll_FoldsEveryGroupAndASecondUnfoldsThem()
	{
		using var legend = TwoGroupLegend(CreateChart());
		var window = Realize(legend, TrendLegendViewModel.CollapsedWidth);
		var foldAll = Descendant<Button>(window, "FoldAllButton");
		foldAll.Command.Should().BeSameAs(legend.ToggleAllFoldsCommand);
		LabelOf(foldAll).Text.Should().Be(Resources.LegendFoldAll);
		IconOf(foldAll).Data.Should().BeSameAs(Icon("ChevronDoubleUpGeometry"));

		HeadlessInput.Click(window, foldAll);

		legend.Groups.Should().AllSatisfy(group => group.IsFolded.Should().BeTrue());
		RowRoots(window).Should().AllSatisfy(row => row.IsEffectivelyVisible.Should().BeFalse());
		LabelOf(foldAll).Text.Should().Be(Resources.LegendUnfoldAll);
		ToolTip.GetTip(foldAll).Should().Be(Resources.LegendUnfoldAllGroups);
		IconOf(foldAll).Data.Should().BeSameAs(Icon("ChevronDoubleDownGeometry"));

		HeadlessInput.Click(window, foldAll);

		legend.Groups.Should().AllSatisfy(group => group.IsFolded.Should().BeFalse());
		RowRoots(window).Should().AllSatisfy(row => row.IsEffectivelyVisible.Should().BeTrue());
		LabelOf(foldAll).Text.Should().Be(Resources.LegendFoldAll);
	}

	[AvaloniaFact]
	public void FoldAll_IsDisabledOnAListWithNoHeader()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", [], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart, []);
		var window = Realize(legend, TrendLegendViewModel.ExpandedWidth);

		Descendant<Button>(window, "FoldAllButton").IsEffectivelyEnabled.Should().BeFalse();
	}

	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light)]
	[InlineData(AppThemeVariant.Dark)]
	public void TheRowSeparator_IsTheSubtleLine(AppThemeVariant theme)
	{
		var variant = App.VariantFor(theme);
		using var scope = ThemeProbe.ApplyVariant(variant);
		using var legend = TwoGroupLegend(CreateChart());
		var window = Realize(legend, TrendLegendViewModel.ExpandedWidth);
		var separator = Descendant<Border>(window, "TopRowSeparator");
		var toggle = Descendant<Button>(window, "PanelStateToggle");

		BottomOf(toggle, window).Should().BeLessThanOrEqualTo(TopOf(separator, window));
		ColourOf(separator.Background).Should().Be(ThemeProbe.Colour("AppSubtleLineBrush", variant));
	}

	// docs/architecture/overview.md#the-sidebar-fold
	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light)]
	[InlineData(AppThemeVariant.Dark)]
	public void TheGroupChevron_PaintsNoFillAndASecondaryGlyphThatTurnsAccentUnderThePointer(AppThemeVariant theme)
	{
		var variant = App.VariantFor(theme);
		using var scope = ThemeProbe.ApplyVariant(variant);
		using var legend = TwoGroupLegend(CreateChart());
		var window = Realize(legend, TrendLegendViewModel.ExpandedWidth);
		var secondary = ThemeProbe.Colour("AppSecondaryForegroundBrush", variant);
		var accent = ThemeProbe.Colour("AppAccentBrush", variant);
		var chevron = FoldToggles(window)[1];

		foreach (var folded in new[] { false, true })
		{
			chevron.IsChecked.Should().Be(folded);
			ShouldPaint(chevron, secondary, $"at rest, folded {folded}");

			var centre = Hover(window, chevron);
			ShouldPaint(chevron, accent, $"under the pointer, folded {folded}");

			// The click is split around the held button so the pressed state can be read.
			window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
			Dispatcher.UIThread.RunJobs();
			ShouldPaint(chevron, accent, $"pressed, folded {folded}");

			window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None);
			Dispatcher.UIThread.RunJobs();
			Hover(window, Descendant<Button>(window, "PanelStateToggle"));
			ShouldPaint(chevron, secondary, $"after the pointer left, folded {!folded}");
		}

		legend.Groups[1].IsFolded.Should().BeFalse("the second release unfolded what the first folded");
	}

	[AvaloniaTheory]
	[InlineData(TrendLegendViewModel.ExpandedWidth)]
	[InlineData(TrendLegendViewModel.CollapsedWidth)]
	public void EveryTooltipOfTheSidebar_ShowsItsText(double width)
	{
		using var legend = TwoGroupLegend(CreateChart());
		var window = Realize(legend, width);
		Control[] holders =
		[
			.. TopRowButtons(window),
			Descendants<TextBlock>(window).First(text => text.Name == "GroupHeader"),
			FoldToggles(window)[0]
		];

		foreach (var holder in holders)
		{
			var text = OpenTooltipText(holder);

			text.Text.Should().Be(ToolTip.GetTip(holder) as string, "'{0}' at {1} px", holder.Name, width);
			text.IsEffectivelyVisible.Should().BeTrue("'{0}' at {1} px shows its tooltip text", holder.Name, width);
			text.Bounds.Width.Should().BePositive("'{0}' at {1} px lays its tooltip text out", holder.Name, width);

			ToolTip.SetIsOpen(holder, false);
			Dispatcher.UIThread.RunJobs();
		}
	}

	[AvaloniaFact]
	public void ALongGroupCaption_TrimsAndCarriesTheFullNameAsItsTooltip()
	{
		const string LongName = "Chamber pressure gauges of the second process module";
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", [LongName], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, LongName, [1])]);
		var window = Realize(legend, TrendLegendViewModel.PanelMinWidth);
		var caption = Descendant<TextBlock>(window, "GroupHeader");

		caption.TextTrimming.Should().Be(TextTrimming.CharacterEllipsis);
		caption.TextLayout.TextLines.Should().ContainSingle().Which.HasCollapsed.Should().BeTrue();
		RightOf(caption, window).Should().BeLessThanOrEqualTo(LeftOf(FoldToggles(window).Single(), window));
		ToolTip.GetTip(caption).Should().Be(LongName);
	}

	private static TrendLegendViewModel TwoGroupLegend(TrendChartViewModel chart)
	{
		chart.AddPen(new Pen(1, "Heater 01", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Chamber pressure", ["Pressures"], "#0000ff"));

		return new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1]), new StoredGroup(2, "Pressures", [2])]);
	}

	private static Window Realize(TrendLegendViewModel legend, double width)
	{
		var window = new Window
		{
			Width = width,
			Height = 400,
			Content = new TrendLegendView { DataContext = legend }
		};

		window.Show();
		Dispatcher.UIThread.RunJobs();

		return window;
	}

	private static Point Hover(Window window, Control control)
	{
		var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
			?? throw new InvalidOperationException("The control is not in the window's visual tree.");

		window.MouseMove(centre, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();

		return centre;
	}

	private static void ShouldPaint(ToggleButton chevron, Color glyph, string state)
	{
		var presenter = Descendant<ContentPresenter>(chevron, "PART_ContentPresenter");

		AlphaOf(presenter.Background).Should().Be(0, "the presenter paints no fill {0}", state);
		AlphaOf(presenter.BorderBrush).Should().Be(0, "the presenter paints no border {0}", state);
		ColourOf(IconOf(chevron).Foreground).Should().Be(glyph, "the glyph {0}", state);
	}

	private static byte AlphaOf(IBrush? brush)
	{
		return brush switch
		{
			null => 0,
			ISolidColorBrush solid => (byte)(solid.Color.A * solid.Opacity),
			_ => throw new InvalidOperationException($"The presenter paints a {brush.GetType().Name}.")
		};
	}

	// Avalonia keeps the open ToolTip behind an internal property; no public member reaches it.
	private static TextBlock OpenTooltipText(Control holder)
	{
		var property = typeof(ToolTip).GetField("ToolTipProperty", BindingFlags.NonPublic | BindingFlags.Static)
			?.GetValue(null) as AvaloniaProperty
			?? throw new InvalidOperationException("Avalonia no longer exposes ToolTip.ToolTipProperty.");

		ToolTip.SetIsOpen(holder, true);
		Dispatcher.UIThread.RunJobs();

		var tooltip = holder.GetValue(property) as ToolTip
			?? throw new InvalidOperationException($"'{holder.Name}' opened no tooltip.");

		return Descendants<TextBlock>(tooltip).Single();
	}

	private static IReadOnlyList<Button> TopRowButtons(Window window)
	{
		return [Descendant<Button>(window, "PanelStateToggle"), Descendant<Button>(window, "FoldAllButton")];
	}

	private static TextBlock LabelOf(Control button)
	{
		return Descendants<TextBlock>(button).Single();
	}

	private static PathIcon IconOf(Control button)
	{
		return Descendants<PathIcon>(button).Single();
	}

	private static IReadOnlyList<ToggleButton> FoldToggles(Window window)
	{
		return [.. Descendants<ToggleButton>(window).Where(toggle => toggle.Name == "GroupFoldToggle")];
	}

	private static IReadOnlyList<Border> RowRoots(Window window)
	{
		return [.. Descendants<Border>(window).Where(border => border.Name == "RowRoot")];
	}

	private static object? Icon(string key)
	{
		return Application.Current!.FindResource(key);
	}

	private TrendChartViewModel CreateChart()
	{
		return ChartTestBuilder.CreateChart(_scheduler);
	}
}
