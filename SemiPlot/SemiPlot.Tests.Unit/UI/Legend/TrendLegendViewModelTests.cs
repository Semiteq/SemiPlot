using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Legend;
using SemiPlot.UI.Localization;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Legend;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendLegendViewModelTests
{
	private const string CyrillicSmallGhe = "\u0433";
	private const string CyrillicCapitalDe = "\u0414";
	private const string CyrillicCapitalIo = "\u0401";
	private const string CyrillicCapitalEn = "\u041d";

	private static readonly TimeSpan _historyDebounceWindow = TimeSpan.FromMilliseconds(150);
	private static readonly DateTime _from = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _to = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);
	private readonly TestScheduler _scheduler = new();

	[AvaloniaFact]
	public void Groups_AreKeyedByPenGroup()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Heaters"], "#00ff00"));
		chart.AddPen(new Pen(3, "Pen 3", ["Pressures"], "#0000ff"));

		using var legend = new TrendLegendViewModel(chart);

		legend.Groups.Should().HaveCount(2);
		legend.Groups.Single(group => group.Name == "Heaters").Rows.Should().HaveCount(2);
		legend.Groups.Single(group => group.Name == "Pressures").Rows.Should().HaveCount(1);
	}

	// The pen draws once and is listed twice, so the two headers have to hold the same instance: that is
	// what makes switching it off under one header switch it off under the other.
	[AvaloniaFact]
	public void APenInTwoGroups_IsOneRowListedUnderBothHeaders()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters", "Pressures"], "#ff0000"));

		using var legend = new TrendLegendViewModel(chart);

		legend.Groups.Select(group => group.Name).Should().Equal("Heaters", "Pressures");
		var underHeaters = legend.Groups.Single(group => group.Name == "Heaters").Rows.Single();
		var underPressures = legend.Groups.Single(group => group.Name == "Pressures").Rows.Single();
		underPressures.Should().BeSameAs(underHeaters);

		underHeaters.IsVisible = false;

		underPressures.IsVisible.Should().BeFalse();
		chart.FindPen(1)!.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void AnUngroupedPen_LandsUnderTheUngroupedHeader()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", [], "#00ff00"));

		using var legend = new TrendLegendViewModel(chart);

		legend.Groups.Select(group => group.Name).Should().Equal("Heaters", Resources.LegendUngroupedHeader);
		legend.Groups[^1].Rows.Single().Name.Should().Be("Pen 2");
	}

	// The rows arrive ordered by pen name, so first-appearance order and name order only agree by
	// accident: the pen that sorts first here declares the group that sorts last.
	[AvaloniaFact]
	public void Headers_ReadInNameOrderRatherThanInTheOrderTheRowsDeclareThem()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Watchlist"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Dampers"], "#00ff00"));
		chart.AddPen(new Pen(3, "Pen 3", ["Heaters"], "#0000ff"));

		using var legend = new TrendLegendViewModel(chart);

		legend.Groups.Select(group => group.Name).Should().Equal("Dampers", "Heaters", "Watchlist");
	}

	// The viewer ships a Russian resource set, and the alphabet runs ghe, de, io, en. An ordinal order
	// reads io, de, en, ghe: it draws every capital-initial name ahead of every lowercase-initial one and
	// puts io ahead of a. Source is ASCII only, so the four names are escaped above.
	[AvaloniaFact]
	public void Headers_ReadInTheAlphabeticalOrderOfMixedCaseCyrillicNames()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", [CyrillicCapitalEn], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", [CyrillicSmallGhe], "#00ff00"));
		chart.AddPen(new Pen(3, "Pen 3", [CyrillicCapitalDe], "#0000ff"));
		chart.AddPen(new Pen(4, "Pen 4", [CyrillicCapitalIo], "#ffff00"));

		using var legend = new TrendLegendViewModel(chart);

		legend.Groups.Select(group => group.Name).Should().Equal(
			CyrillicSmallGhe,
			CyrillicCapitalDe,
			CyrillicCapitalIo,
			CyrillicCapitalEn);
	}

	// The ungrouped header is a resource string and a catalogue may hold a group named exactly that;
	// two headers of one text would read as a duplicate.
	[AvaloniaFact]
	public void AGroupNamedLikeTheUngroupedHeader_TakesTheUngroupedRowsRatherThanASecondHeader()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", [Resources.LegendUngroupedHeader], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", [], "#00ff00"));

		using var legend = new TrendLegendViewModel(chart);

		legend.Groups.Should().ContainSingle();
		legend.Groups.Single().Name.Should().Be(Resources.LegendUngroupedHeader);
		legend.Groups.Single().Rows.Select(row => row.Name).Should().Equal("Pen 1", "Pen 2");
	}

	// Dispose walks the distinct rows, not the flattened ones, and a row listed under two headers is one
	// instance: it is reached once and its mirror subscription goes with it.
	[AvaloniaFact]
	public void Dispose_ReachesARowListedUnderTwoHeaders()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters", "Watchlist"], "#ff0000"));
		var legend = new TrendLegendViewModel(chart);
		var row = legend.Groups[0].Rows.Single();

		legend.Dispose();
		chart.SetPenVisibility(1, false);

		row.IsVisible.Should().BeTrue("a disposed row no longer mirrors the chart");
	}

	// The row's own notification still fires after the legend is gone, so only the group's disposal stops
	// the header re-deriving from it.
	[AvaloniaFact]
	public void Dispose_StopsEveryHeaderFollowingItsRows()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		var legend = new TrendLegendViewModel(chart);
		var heaters = Group(legend, "Heaters");

		legend.Dispose();
		heaters.Rows.Single().IsVisible = false;

		heaters.SwitchState.Should().BeTrue("a disposed header no longer follows its rows");
	}

	[AvaloniaFact]
	public void TheSwitchOnAGroupNamedLikeTheUngroupedHeader_SwitchesItsGroupedAndItsUngroupedPens()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", [Resources.LegendUngroupedHeader], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", [], "#00ff00"));
		using var legend = new TrendLegendViewModel(chart);
		var merged = legend.Groups.Single();

		merged.SwitchGroupCommand.Execute().Subscribe();

		chart.FindPen(1)!.IsVisible.Should().BeFalse();
		chart.FindPen(2)!.IsVisible.Should().BeFalse();
		merged.SwitchState.Should().BeFalse();
	}

	[AvaloniaFact]
	public void SwitchState_IsOnWhenAllRowsAreOnOffWhenAllAreOffAndIndeterminateWhenMixed()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Heaters"], "#00ff00"));
		using var legend = new TrendLegendViewModel(chart);
		var heaters = Group(legend, "Heaters");

		heaters.SwitchState.Should().BeTrue("every pen starts visible");

		chart.SetPenVisibility(1, false);

		heaters.SwitchState.Should().BeNull();

		chart.SetPenVisibility(2, false);

		heaters.SwitchState.Should().BeFalse();
	}

	[AvaloniaFact]
	public void SwitchingOnePen_ReDerivesEveryHeaderItSitsUnder()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters", "Watchlist"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Heaters"], "#00ff00"));
		chart.AddPen(new Pen(3, "Pen 3", ["Watchlist"], "#0000ff"));
		using var legend = new TrendLegendViewModel(chart);

		SingleRow(legend, 1).IsVisible = false;

		Group(legend, "Heaters").SwitchState.Should().BeNull();
		Group(legend, "Watchlist").SwitchState.Should().BeNull();
	}

	[AvaloniaFact]
	public void TheSwitchOnAMixedHeader_SwitchesEveryPenOn()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Heaters"], "#00ff00"));
		using var legend = new TrendLegendViewModel(chart);
		var heaters = Group(legend, "Heaters");
		chart.SetPenVisibility(1, false);

		heaters.SwitchGroupCommand.Execute().Subscribe();

		chart.FindPen(1)!.IsVisible.Should().BeTrue();
		chart.FindPen(2)!.IsVisible.Should().BeTrue();
		heaters.SwitchState.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TheSwitchOnAnOffHeader_SwitchesEveryPenOn()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Heaters"], "#00ff00"));
		using var legend = new TrendLegendViewModel(chart);
		var heaters = Group(legend, "Heaters");
		chart.SetPenVisibility(1, false);
		chart.SetPenVisibility(2, false);

		heaters.SwitchGroupCommand.Execute().Subscribe();

		chart.FindPen(1)!.IsVisible.Should().BeTrue();
		chart.FindPen(2)!.IsVisible.Should().BeTrue();
		heaters.SwitchState.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TheSwitchOnAnOnHeader_SwitchesEveryPenOffAndTheSharedPensOtherHeaderFollows()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters", "Watchlist"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Heaters"], "#00ff00"));
		chart.AddPen(new Pen(3, "Pen 3", ["Watchlist"], "#0000ff"));
		using var legend = new TrendLegendViewModel(chart);
		var heaters = Group(legend, "Heaters");

		heaters.SwitchGroupCommand.Execute().Subscribe();

		chart.FindPen(1)!.IsVisible.Should().BeFalse();
		chart.FindPen(2)!.IsVisible.Should().BeFalse();
		chart.FindPen(3)!.IsVisible.Should().BeTrue("the pen sits under Watchlist alone");
		heaters.SwitchState.Should().BeFalse();
		Group(legend, "Watchlist").SwitchState.Should().BeNull();
	}

	[AvaloniaFact]
	public void TheUngroupedHeader_CarriesItsOwnSwitch()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", [], "#00ff00"));
		using var legend = new TrendLegendViewModel(chart);
		var ungrouped = Group(legend, Resources.LegendUngroupedHeader);

		ungrouped.SwitchGroupCommand.Execute().Subscribe();

		chart.FindPen(2)!.IsVisible.Should().BeFalse();
		chart.FindPen(1)!.IsVisible.Should().BeTrue();
		ungrouped.SwitchState.Should().BeFalse();
		ungrouped.SwitchName.Should().Be(Resources.FormatLegendGroupSwitch(Resources.LegendUngroupedHeader));
	}

	// The handle sits on the panel's left edge, so a negative delta widens it.
	[AvaloniaFact]
	public void ResizePanel_WritesTheSlotOfTheShownStateAndTheToggleSwitchesSlots()
	{
		const double MaximumWidth = 900;
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart);
		legend.FitPanel(MaximumWidth);

		legend.ResizePanel(-40);

		legend.PanelWidth.Should().Be(TrendLegendViewModel.ExpandedWidth + 40);

		legend.ToggleExpandedCommand.Execute().Subscribe();

		legend.PanelWidth.Should().Be(TrendLegendViewModel.CollapsedWidth, "the collapsed slot was never dragged");

		legend.ResizePanel(20);

		legend.PanelWidth.Should().Be(TrendLegendViewModel.CollapsedWidth - 20);

		legend.ToggleExpandedCommand.Execute().Subscribe();

		legend.PanelWidth.Should().Be(TrendLegendViewModel.ExpandedWidth + 40, "the expanded slot kept its drag");

		legend.ToggleExpandedCommand.Execute().Subscribe();

		legend.PanelWidth.Should().Be(TrendLegendViewModel.CollapsedWidth - 20, "the collapsed slot kept its drag");
	}

	[AvaloniaFact]
	public void ResizePanel_StopsAtThePanelFloorAndAtTheMaximumWidth()
	{
		const double MaximumWidth = 500;
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart);
		legend.FitPanel(MaximumWidth);

		legend.ResizePanel(1000);

		legend.PanelWidth.Should().Be(TrendLegendViewModel.PanelMinWidth);

		legend.ResizePanel(-1000);

		legend.PanelWidth.Should().Be(MaximumWidth);

		legend.FitPanel(TrendLegendViewModel.PanelMinWidth / 2);

		legend.PanelWidth.Should().Be(
			TrendLegendViewModel.PanelMinWidth,
			"a window too narrow for both floors keeps the panel's");
	}

	[AvaloniaFact]
	public void FitPanel_NarrowsTheShownWidthAndKeepsBothSlots()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart);
		legend.FitPanel(900);
		legend.ResizePanel(-400);

		legend.FitPanel(500);

		legend.PanelWidth.Should().Be(500);

		legend.ToggleExpandedCommand.Execute().Subscribe();

		legend.PanelWidth.Should().Be(TrendLegendViewModel.CollapsedWidth, "the collapsed slot fits the room");

		legend.ToggleExpandedCommand.Execute().Subscribe();
		legend.FitPanel(900);

		legend.PanelWidth.Should().Be(TrendLegendViewModel.ExpandedWidth + 400);
	}

	[AvaloniaFact]
	public void TogglingRowCheckbox_FlipsChartPenVisibility()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart);
		var row = SingleRow(legend, 1);

		row.IsVisible = false;

		chart.FindPen(1)!.IsVisible.Should().BeFalse();

		row.IsVisible = true;

		chart.FindPen(1)!.IsVisible.Should().BeTrue();
	}

	[AvaloniaFact]
	public void RowVisibility_MirrorsChartDrivenVisibilityChange()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart);
		var row = SingleRow(legend, 1);

		chart.SetPenVisibility(1, false);

		row.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void SelectingRow_SetsTheActivePenOnChart()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Pressures"], "#00ff00"));
		using var legend = new TrendLegendViewModel(chart);
		var secondRow = SingleRow(legend, 2);

		secondRow.Select();

		chart.ActivePenId.Should().Be(2);
		secondRow.IsActive.Should().BeTrue();
		SingleRow(legend, 1).IsActive.Should().BeFalse();
	}

	[AvaloniaFact]
	public void CurrentValue_ReflectsChartHistoryLoad()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart);
		var row = SingleRow(legend, 1);

		LoadInitialHistory(chart, _from, _to);

		row.CurrentValue.Should().Be(2.0);
	}

	[AvaloniaFact]
	public void CurrentValueText_RendersTheReadingInThePensOwnMask()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000", "kPa", "0.000"));
		using var legend = new TrendLegendViewModel(chart);
		var row = SingleRow(legend, 1);

		LoadInitialHistory(chart, _from, _to);

		row.CurrentValueText.Should().Be(PenValueFormat.Format(2.0, "0.000"));
		row.CurrentValueText.Should().NotBe(PenValueFormat.Format(2.0, PenValueFormat.FallbackMask));
		row.Unit.Should().Be("kPa");
	}

	[AvaloniaFact]
	public void RowText_WithoutAValue_ReadsTheNoValuePlaceholder()
	{
		var chart = CreateChart();
		var penState = new TrendPenState(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"), new EnvelopeLine());

		using var row = new TrendLegendRowViewModel(chart, penState);

		row.CurrentValueText.Should().Be(Resources.NoValuePlaceholder);
		row.Unit.Should().BeEmpty("a pen with no stored unit renders no unit run");
	}

	private void LoadInitialHistory(TrendChartViewModel chart, DateTime from, DateTime to)
	{
		chart.Navigation.TrackDataExtents(from, to);
		chart.RequestInitialHistory();
		_scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);
	}

	private static TrendLegendRowViewModel SingleRow(TrendLegendViewModel legend, int penId)
	{
		return legend.Groups
			.SelectMany(group => group.Rows)
			.Distinct()
			.Single(row => row.Name == $"Pen {penId}");
	}

	private static TrendLegendGroupViewModel Group(TrendLegendViewModel legend, string name)
	{
		return legend.Groups.Single(group => group.Name == name);
	}

	private TrendChartViewModel CreateChart()
	{
		return LegendChartBuilder.CreateChart(_scheduler);
	}
}
