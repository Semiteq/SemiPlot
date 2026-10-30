using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Chart;
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

	private readonly TestScheduler _scheduler = new();

	[AvaloniaFact]
	public void Groups_AreKeyedByStoredGroupMembership()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Heaters"], "#00ff00"));
		chart.AddPen(new Pen(3, "Pen 3", ["Pressures"], "#0000ff"));

		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1, 2]), new StoredGroup(2, "Pressures", [3])]);

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

		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1]), new StoredGroup(2, "Pressures", [1])]);

		legend.Groups.Select(group => group.Name).Should().Equal("Heaters", "Pressures");
		var underHeaters = legend.Groups.Single(group => group.Name == "Heaters").Rows.Single();
		var underPressures = legend.Groups.Single(group => group.Name == "Pressures").Rows.Single();
		underPressures.Should().BeSameAs(underHeaters);

		underHeaters.ToggleVisibilityCommand.Execute().Subscribe();

		underPressures.PenState.IsVisible.Should().BeFalse();
		chart.FindPen(1)!.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void Headers_FollowStoredMembershipRatherThanThePensGroupStrings()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));

		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Dampers", [1])]);

		legend.Groups.Select(group => group.Name).Should().Equal("Dampers");
	}

	[AvaloniaFact]
	public void AStoredGroupHoldingNoShownPen_DrawsNoHeader()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Dampers"], "#ff0000"));

		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", []), new StoredGroup(2, "Dampers", [1])]);

		legend.Groups.Select(group => group.Name).Should().Equal("Dampers");
	}

	[AvaloniaFact]
	public void AnUngroupedPen_LandsUnderTheUngroupedHeader()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", [], "#00ff00"));

		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1])]);

		legend.Groups.Select(group => group.Name).Should().Equal("Heaters", Resources.LegendUngroupedHeader);
		legend.Groups[^1].Rows.Single().PenState.Pen.Name.Should().Be("Pen 2");
	}

	// The rows arrive ordered by pen name, so first-appearance order and name order only agree by
	// accident: the pen that sorts first here is a member of the group that sorts last.
	[AvaloniaFact]
	public void Headers_ReadInNameOrderRatherThanInTheOrderTheRowsDeclareThem()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Watchlist"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Dampers"], "#00ff00"));
		chart.AddPen(new Pen(3, "Pen 3", ["Heaters"], "#0000ff"));

		using var legend = new TrendLegendViewModel(
			chart,
			[
				new StoredGroup(1, "Watchlist", [1]),
				new StoredGroup(2, "Dampers", [2]),
				new StoredGroup(3, "Heaters", [3])
			]);

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

		using var legend = new TrendLegendViewModel(
			chart,
			[
				new StoredGroup(1, CyrillicCapitalEn, [1]),
				new StoredGroup(2, CyrillicSmallGhe, [2]),
				new StoredGroup(3, CyrillicCapitalDe, [3]),
				new StoredGroup(4, CyrillicCapitalIo, [4])
			]);

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

		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, Resources.LegendUngroupedHeader, [1])]);

		legend.Groups.Should().ContainSingle();
		legend.Groups.Single().Name.Should().Be(Resources.LegendUngroupedHeader);
		legend.Groups.Single().Rows.Select(row => row.PenState.Pen.Name).Should().Equal("Pen 1", "Pen 2");
	}

	[AvaloniaFact]
	public void TwoStoredGroupsSharingAName_MergeIntoOneHeader()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Pressures"], "#00ff00"));

		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1]), new StoredGroup(2, "Heaters", [2])]);

		legend.Groups.Should().ContainSingle();
		legend.Groups.Single().Name.Should().Be("Heaters");
		legend.Groups.Single().Rows.Select(row => row.PenState.Pen.Name).Should().Equal("Pen 1", "Pen 2");
	}

	[AvaloniaFact]
	public void Dispose_ReachesARowListedUnderTwoHeaders()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters", "Watchlist"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Heaters"], "#00ff00"));
		var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1, 2]), new StoredGroup(2, "Watchlist", [1])]);
		var row = SingleRow(legend, 1);

		legend.Dispose();
		chart.SetActivePen(2);

		row.IsActive.Should().BeTrue("a disposed row no longer follows the chart");
	}

	// The pen state's notification still fires after the legend is gone, so only the group's disposal stops
	// the header re-deriving from it.
	[AvaloniaFact]
	public void Dispose_StopsEveryHeaderFollowingItsRows()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1])]);
		var heaters = Header(legend, "Heaters");

		legend.Dispose();
		chart.SetPenVisibility(1, false);

		heaters.SwitchState.Should().BeTrue("a disposed header no longer follows its rows");
	}

	[AvaloniaFact]
	public void TheSwitchOnAGroupNamedLikeTheUngroupedHeader_SwitchesItsGroupedAndItsUngroupedPens()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", [Resources.LegendUngroupedHeader], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", [], "#00ff00"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, Resources.LegendUngroupedHeader, [1])]);
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
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1, 2])]);
		var heaters = Header(legend, "Heaters");

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
		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1, 2]), new StoredGroup(2, "Watchlist", [1, 3])]);

		SingleRow(legend, 1).ToggleVisibilityCommand.Execute().Subscribe();

		Header(legend, "Heaters").SwitchState.Should().BeNull();
		Header(legend, "Watchlist").SwitchState.Should().BeNull();
	}

	[AvaloniaTheory]
	[InlineData(false)]
	[InlineData(true)]
	public void TheSwitchOnAHeaderThatIsMixedOrOff_SwitchesEveryPenOn(bool isSecondPenOff)
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Heaters"], "#00ff00"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1, 2])]);
		var heaters = Header(legend, "Heaters");
		chart.SetPenVisibility(1, false);
		chart.SetPenVisibility(2, !isSecondPenOff);

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
		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1, 2]), new StoredGroup(2, "Watchlist", [1, 3])]);
		var heaters = Header(legend, "Heaters");

		heaters.SwitchGroupCommand.Execute().Subscribe();

		chart.FindPen(1)!.IsVisible.Should().BeFalse();
		chart.FindPen(2)!.IsVisible.Should().BeFalse();
		chart.FindPen(3)!.IsVisible.Should().BeTrue("the pen sits under Watchlist alone");
		heaters.SwitchState.Should().BeFalse();
		Header(legend, "Watchlist").SwitchState.Should().BeNull();
	}

	[AvaloniaFact]
	public void TheUngroupedHeader_CarriesItsOwnSwitch()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", [], "#00ff00"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1])]);
		var ungrouped = Header(legend, Resources.LegendUngroupedHeader);

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
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1])]);
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
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1])]);
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
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1])]);
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
	public void TheRow_FollowsAChangeOfTheChartsPenState()
	{
		var chart = CreateChart();
		var pen = new Pen(1, "Pen 1", ["Heaters"], "#ff0000");
		chart.AddPen(pen);
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1])]);
		var row = SingleRow(legend, 1);

		chart.SetPenVisibility(1, false);
		chart.ApplyCatalogue([pen with { Color = "#00ff00" }]);

		row.PenState.IsVisible.Should().BeFalse();
		row.PenState.Pen.Color.Should().Be("#00ff00");
	}

	[AvaloniaFact]
	public void SelectingRow_SetsTheActivePenOnChart()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Pressures"], "#00ff00"));
		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1]), new StoredGroup(2, "Pressures", [2])]);
		var secondRow = SingleRow(legend, 2);

		secondRow.Select();

		chart.ActivePenId.Should().Be(2);
		secondRow.IsActive.Should().BeTrue();
		SingleRow(legend, 1).IsActive.Should().BeFalse();
	}

	[AvaloniaFact]
	public void CurrentValueText_RendersTheReadingInThePensOwnMask()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000", "kPa", "0.000"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1])]);
		var row = SingleRow(legend, 1);

		ChartTestBuilder.LoadInitialHistory(chart, _scheduler, ChartTestBuilder.From, ChartTestBuilder.To);

		row.CurrentValueText.Should().Be(PenValueFormat.Format(2.0, "0.000"));
		row.CurrentValueText.Should().NotBe(PenValueFormat.Format(2.0, PenValueFormat.FallbackMask));
	}

	[AvaloniaFact]
	public void RowText_WithoutAValue_ReadsTheNoValuePlaceholder()
	{
		var chart = CreateChart();
		var penState = new TrendPenState(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"), new EnvelopeLine());

		using var row = new TrendLegendRowViewModel(chart, penState);

		row.CurrentValueText.Should().Be(Resources.NoValuePlaceholder);
	}

	[AvaloniaFact]
	public void Rebuild_ShowsARevisedNameColourUnitAndMask()
	{
		var chart = CreateChart();
		var pen = new Pen(1, "Pen 1", ["Heaters"], "#ff0000", "kPa", "0.0");
		chart.AddPen(pen);
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1])]);
		ChartTestBuilder.LoadInitialHistory(chart, _scheduler, ChartTestBuilder.From, ChartTestBuilder.To);

		chart.ApplyCatalogue([pen with { Name = "Heater 01", Color = "#00ff00", Unit = "Pa", Format = "0.000" }]);
		legend.Rebuild([new StoredGroup(1, "Heaters", [1])]);

		var row = legend.Groups.Single().Rows.Single();
		row.PenState.Pen.Name.Should().Be("Heater 01");
		row.PenState.Pen.Color.Should().Be("#00ff00");
		row.PenState.Pen.Unit.Should().Be("Pa");
		row.CurrentValueText.Should().Be(PenValueFormat.Format(2.0, "0.000"));
	}

	[AvaloniaFact]
	public void Rebuild_ListsAnAddedPenUnderItsGroupAndDropsARemovedOne()
	{
		var chart = CreateChart();
		var kept = new Pen(1, "Pen 1", ["Heaters"], "#ff0000");
		chart.AddPen(kept);
		chart.AddPen(new Pen(2, "Pen 2", ["Pressures"], "#00ff00"));
		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1]), new StoredGroup(2, "Pressures", [2])]);

		chart.ApplyCatalogue([kept, new Pen(3, "Pen 3", ["Dampers"], "#0000ff", EnabledOnStart: false)]);
		legend.Rebuild([new StoredGroup(1, "Heaters", [1]), new StoredGroup(3, "Dampers", [3])]);

		legend.Groups.Select(group => group.Name).Should().Equal("Dampers", "Heaters");
		SingleRow(legend, 3).PenState.IsVisible.Should().BeFalse("a pen joins with the visibility it starts with");
	}

	[AvaloniaFact]
	public void Rebuild_KeepsThePanelWidthTheCollapsedStateAndEveryRowsVisibility()
	{
		var chart = CreateChart();
		var first = new Pen(1, "Pen 1", ["Heaters"], "#ff0000");
		var second = new Pen(2, "Pen 2", ["Heaters"], "#00ff00");
		var third = new Pen(3, "Pen 3", ["Heaters"], "#ffff00", EnabledOnStart: false);
		chart.AddPen(first);
		chart.AddPen(second);
		chart.AddPen(third);
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1, 2, 3])]);
		legend.FitPanel(900);
		legend.ToggleExpandedCommand.Execute().Subscribe();
		legend.ResizePanel(-30);
		SingleRow(legend, 2).ToggleVisibilityCommand.Execute().Subscribe();

		chart.ApplyCatalogue(
		[
			first with { EnabledOnStart = false },
			second with { Color = "#0000ff" },
			third with { EnabledOnStart = true }
		]);
		legend.Rebuild([new StoredGroup(1, "Heaters", [1, 2, 3])]);

		legend.IsExpanded.Should().BeFalse();
		legend.PanelWidth.Should().Be(TrendLegendViewModel.CollapsedWidth + 30);
		SingleRow(legend, 1).PenState.IsVisible.Should().BeTrue();
		SingleRow(legend, 2).PenState.IsVisible.Should().BeFalse();
		SingleRow(legend, 3).PenState.IsVisible.Should().BeFalse("an EnabledOnStart revision switches no pen on");
		Header(legend, "Heaters").SwitchState.Should().BeNull();
	}

	[AvaloniaFact]
	public void Rebuild_AnnouncesTheNewGroupsBeforeItDisposesTheReplacedRows()
	{
		var chart = CreateChart();
		var pen = new Pen(1, "Pen 1", ["Heaters"], "#ff0000");
		var other = new Pen(2, "Pen 2", ["Heaters"], "#00ff00");
		chart.AddPen(pen);
		chart.AddPen(other);
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1, 2])]);
		var replacedRow = SingleRow(legend, 1);
		var replacedFollowedOnAnnouncement = false;
		legend.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(TrendLegendViewModel.Groups))
			{
				chart.SetActivePen(other.PenId);
				replacedFollowedOnAnnouncement = !replacedRow.IsActive;
			}
		};

		chart.ApplyCatalogue([pen with { Name = "Heater 01" }, other]);
		legend.Rebuild([new StoredGroup(1, "Heaters", [1, 2])]);

		replacedFollowedOnAnnouncement.Should().BeTrue();
		SingleRow(legend, 2).IsActive.Should().BeTrue();
	}

	private static TrendLegendRowViewModel SingleRow(TrendLegendViewModel legend, int penId)
	{
		return SingleRow(legend, $"Pen {penId}");
	}

	private static TrendLegendRowViewModel SingleRow(TrendLegendViewModel legend, string name)
	{
		return legend.Groups
			.SelectMany(group => group.Rows)
			.Distinct()
			.Single(row => row.PenState.Pen.Name == name);
	}

	private static TrendLegendGroupViewModel Header(TrendLegendViewModel legend, string name)
	{
		return legend.Groups.Single(group => group.Name == name);
	}

	private TrendChartViewModel CreateChart()
	{
		return ChartTestBuilder.CreateChart(_scheduler);
	}
}
