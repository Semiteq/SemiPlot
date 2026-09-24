using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Legend;
using SemiPlot.UI.Localization;

using Xunit;

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
	public void TheRequestedWidthAndTheToggleLabel_FollowTheState()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Chamber pressure", ["Pressures"], "#ff0000", "kPa", "0.000"));
		using var legend = new TrendLegendViewModel(chart);
		var window = Realize(legend);

		legend.IsExpanded.Should().BeTrue("every start opens expanded");
		legend.RequestedWidth.Should().Be(TrendLegendViewModel.ExpandedWidth);
		ToggleLabel(window).Should().Be(Resources.LegendCollapsePanel);

		PressTheToggle(window);

		legend.IsExpanded.Should().BeFalse();
		legend.RequestedWidth.Should().Be(TrendLegendViewModel.CollapsedWidth);
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

	// The allowlist above covers the row; the panel's own header is the other half of "nothing here
	// accepts typing", and every text editor Avalonia ships puts a TextBox in its template.
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

		HeaderTexts(window).Should().Equal("Heaters", "Pressures");
		var boxes = Descendants<CheckBox>(window).ToList();
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
		Descendants<Border>(window).Count(border => border.Name == "RowRoot").Should().Be(2);
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
				.Where(block => block.Name == "GroupHeader" && block.IsVisible)
				.Select(block => block.Text ?? string.Empty)
		];
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
