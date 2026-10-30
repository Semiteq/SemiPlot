using System.Reactive.Linq;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using ScottPlot;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
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
public sealed class TrendLegendFoldTests
{
	private readonly TestScheduler _scheduler = new();

	[AvaloniaFact]
	public void AHeader_SeedsItsFoldFromTheStoredGroupOfItsName()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Pressures"], "#00ff00"));
		chart.AddPen(new Pen(3, "Pen 3", [], "#0000ff"));

		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1], FoldedOnStart: true), new StoredGroup(2, "Pressures", [2])]);

		Header(legend, "Heaters").IsFolded.Should().BeTrue();
		Header(legend, "Pressures").IsFolded.Should().BeFalse();
		Header(legend, Resources.LegendUngroupedHeader).IsFolded.Should().BeFalse("no stored group carries the name");
	}

	[AvaloniaFact]
	public void TheFoldCommand_FoldsAndUnfoldsTheHeader()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1])]);
		var heaters = Header(legend, "Heaters");

		heaters.FoldCommand.Execute().Subscribe();

		heaters.IsFolded.Should().BeTrue();
		heaters.Rows.Should().ContainSingle("the fold hides the rows from the list, not from the group");

		heaters.FoldCommand.Execute().Subscribe();

		heaters.IsFolded.Should().BeFalse();
	}

	[AvaloniaFact]
	public void AFoldTheOperatorSet_SurvivesEveryRebuildOverTheStoredFlag()
	{
		var chart = CreateChart();
		var heater = new Pen(1, "Pen 1", ["Heaters"], "#ff0000");
		var pressure = new Pen(2, "Pen 2", ["Pressures"], "#00ff00");
		chart.AddPen(heater);
		chart.AddPen(pressure);
		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1]), new StoredGroup(2, "Pressures", [2], FoldedOnStart: true)]);
		Header(legend, "Heaters").FoldCommand.Execute().Subscribe();
		Header(legend, "Pressures").FoldCommand.Execute().Subscribe();

		chart.ApplyCatalogue([heater with { Color = "#0000ff" }, pressure]);
		legend.Rebuild([new StoredGroup(1, "Heaters", [1]), new StoredGroup(2, "Pressures", [2], FoldedOnStart: true)]);
		legend.Rebuild([new StoredGroup(1, "Heaters", [1]), new StoredGroup(2, "Pressures", [2], FoldedOnStart: true)]);

		Header(legend, "Heaters").IsFolded.Should().BeTrue();
		Header(legend, "Pressures").IsFolded.Should().BeFalse("the session fold wins over the stored default");
	}

	[AvaloniaFact]
	public void AHeaderThatLeavesAndReturns_KeepsItsSessionFold()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Pressures"], "#00ff00"));
		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1]), new StoredGroup(2, "Pressures", [2])]);
		Header(legend, "Heaters").FoldCommand.Execute().Subscribe();

		legend.Rebuild([new StoredGroup(2, "Pressures", [1, 2])]);
		legend.Rebuild([new StoredGroup(1, "Heaters", [1]), new StoredGroup(2, "Pressures", [2])]);

		Header(legend, "Heaters").IsFolded.Should().BeTrue();
	}

	[AvaloniaFact]
	public void AWindowWhoseFirstPenSitsOnlyInAFoldedOnStartGroup_OpensFolded()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Pressures"], "#00ff00"));
		chart.ActivePenId.Should().Be(1);

		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1], FoldedOnStart: true), new StoredGroup(2, "Pressures", [2])]);

		Header(legend, "Heaters").IsFolded.Should().BeTrue();
		Header(legend, "Heaters").ShowsActiveAccent.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TheChartSettlingOnAPenInAFoldedGroup_UnfoldsNothing()
	{
		var chart = CreateChart();
		var pressure = new Pen(1, "Pen 1", ["Pressures"], "#ff0000");
		var heater = new Pen(2, "Pen 2", ["Heaters"], "#00ff00");
		var damper = new Pen(3, "Pen 3", ["Dampers"], "#0000ff");
		chart.AddPen(pressure);
		chart.AddPen(heater);
		chart.AddPen(damper);
		StoredGroup[] storedGroups =
		[
			new StoredGroup(1, "Pressures", [1]),
			new StoredGroup(2, "Heaters", [2], FoldedOnStart: true),
			new StoredGroup(3, "Dampers", [3], FoldedOnStart: true)
		];
		using var legend = new TrendLegendViewModel(chart, storedGroups);

		chart.SetPenVisibility(1, false);

		chart.ActivePenId.Should().Be(2, "switching the active pen off settles on the next visible one");
		Header(legend, "Heaters").IsFolded.Should().BeTrue();

		chart.ApplyCatalogue([pressure, damper]);
		legend.Rebuild(storedGroups);

		chart.ActivePenId.Should().Be(3, "removing the active pen settles on the next visible one");
		Header(legend, "Dampers").IsFolded.Should().BeTrue();
	}

	[AvaloniaFact]
	public void APickWhosePenHasNoUnfoldedRow_UnfoldsTheGroupHoldingIt()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Pressures"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Heaters"], "#00ff00"));
		chart.AddPen(new Pen(3, "Pen 3", ["Dampers"], "#0000ff"));
		using var legend = new TrendLegendViewModel(
			chart,
			[
				new StoredGroup(1, "Pressures", [1]),
				new StoredGroup(2, "Heaters", [2], FoldedOnStart: true),
				new StoredGroup(3, "Dampers", [3], FoldedOnStart: true)
			]);

		chart.SetActivePen(2).Should().BeTrue();

		Header(legend, "Heaters").IsFolded.Should().BeFalse();
		Header(legend, "Dampers").IsFolded.Should().BeTrue("a group not holding the pen keeps its fold");
	}

	[AvaloniaFact]
	public void APickWhosePenSitsOnlyInFoldedGroups_UnfoldsEveryOneOfThem()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Pressures"], "#ff0000"));
		chart.AddPen(new Pen(2, "Heater 01", ["Heaters", "Watchlist"], "#00ff00"));
		using var legend = new TrendLegendViewModel(
			chart,
			[
				new StoredGroup(1, "Pressures", [1]),
				new StoredGroup(2, "Heaters", [2], FoldedOnStart: true),
				new StoredGroup(3, "Watchlist", [2], FoldedOnStart: true)
			]);

		chart.SetActivePen(2).Should().BeTrue();

		Header(legend, "Heaters").IsFolded.Should().BeFalse();
		Header(legend, "Watchlist").IsFolded.Should().BeFalse();
	}

	[AvaloniaFact]
	public void APickWhosePenHasARowUnderAnUnfoldedHeader_LeavesFoldedGroupsAlone()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Pressures"], "#ff0000"));
		chart.AddPen(new Pen(2, "Heater 01", ["Heaters", "Watchlist"], "#00ff00"));
		using var legend = new TrendLegendViewModel(
			chart,
			[
				new StoredGroup(1, "Pressures", [1]),
				new StoredGroup(2, "Heaters", [2], FoldedOnStart: true),
				new StoredGroup(3, "Watchlist", [2])
			]);

		chart.SetActivePen(2).Should().BeTrue();

		Header(legend, "Heaters").IsFolded.Should().BeTrue();
		Header(legend, "Heaters").ShowsActiveAccent.Should().BeTrue();
	}

	[AvaloniaFact]
	public void ARefusedPick_UnfoldsNothing()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Pressures"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Heaters"], "#00ff00", EnabledOnStart: false));
		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Pressures", [1]), new StoredGroup(2, "Heaters", [2], FoldedOnStart: true)]);

		chart.SetActivePen(2).Should().BeFalse();

		Header(legend, "Heaters").IsFolded.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TheMergedUngroupedHeader_SeedsFromTheRealGroupAndFoldsAsOne()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", [Resources.LegendUngroupedHeader], "#00ff00"));
		chart.AddPen(new Pen(3, "Pen 3", [], "#0000ff"));
		StoredGroup[] storedGroups =
		[
			new StoredGroup(1, "Heaters", [1]),
			new StoredGroup(2, Resources.LegendUngroupedHeader, [2], FoldedOnStart: true)
		];
		using var legend = new TrendLegendViewModel(chart, storedGroups);
		var merged = Header(legend, Resources.LegendUngroupedHeader);

		merged.Rows.Should().HaveCount(2);
		merged.IsFolded.Should().BeTrue();

		merged.FoldCommand.Execute().Subscribe();
		legend.Rebuild(storedGroups);

		Header(legend, Resources.LegendUngroupedHeader).IsFolded.Should().BeFalse();
	}

	[AvaloniaFact]
	public void TheAccent_FollowsTheFoldAndTheActivePen()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Pressures"], "#00ff00"));
		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1]), new StoredGroup(2, "Pressures", [2])]);
		var heaters = Header(legend, "Heaters");
		var pressures = Header(legend, "Pressures");

		heaters.ShowsActiveAccent.Should().BeFalse("an unfolded header leaves the bar on the row");

		heaters.FoldCommand.Execute().Subscribe();
		pressures.FoldCommand.Execute().Subscribe();

		heaters.ShowsActiveAccent.Should().BeTrue();
		pressures.ShowsActiveAccent.Should().BeFalse("the group does not hold the active pen");

		pressures.FoldCommand.Execute().Subscribe();
		chart.SetActivePen(2);

		heaters.ShowsActiveAccent.Should().BeFalse();
	}

	[AvaloniaFact]
	public void AMemberlessStoredGroup_DrawsNoHeaderToFold()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));

		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Dampers", [], FoldedOnStart: true), new StoredGroup(2, "Heaters", [1])]);

		legend.Groups.Select(group => group.Name).Should().Equal("Heaters");
	}

	[AvaloniaFact]
	public void AnAllUngroupedCatalogue_DrawsNoHeaderToFold()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", [], "#ff0000"));

		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Dampers", [], FoldedOnStart: true)]);
		var list = legend.Groups.Single();

		list.HasHeader.Should().BeFalse();
		list.IsFolded.Should().BeFalse();
	}

	[AvaloniaFact]
	public void AStoredFlagSetOnAHeaderAlreadyDrawn_LeavesItUnfolded()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1])]);

		legend.Rebuild([new StoredGroup(1, "Heaters", [1], FoldedOnStart: true)]);

		Header(legend, "Heaters").IsFolded.Should().BeFalse("the stored flag seeds only a header drawn first");
	}

	[AvaloniaFact]
	public void AStoredFlagClearedOnAHeaderAlreadyDrawn_LeavesItFolded()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart, [new StoredGroup(1, "Heaters", [1], FoldedOnStart: true)]);

		legend.Rebuild([new StoredGroup(1, "Heaters", [1])]);

		Header(legend, "Heaters").IsFolded.Should().BeTrue("the stored flag seeds only a header drawn first");
	}

	[AvaloniaFact]
	public void ARenamedGroup_TakesTheStoredFlagAgain()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Pressures"], "#00ff00"));
		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1]), new StoredGroup(2, "Pressures", [2], FoldedOnStart: true)]);
		Header(legend, "Heaters").FoldCommand.Execute().Subscribe();
		Header(legend, "Pressures").FoldCommand.Execute().Subscribe();

		legend.Rebuild([new StoredGroup(1, "Chamber", [1]), new StoredGroup(2, "Gauges", [2], FoldedOnStart: true)]);

		Header(legend, "Chamber").IsFolded.Should().BeFalse("a rename is a new name and takes the stored flag");
		Header(legend, "Gauges").IsFolded.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TwoStoredGroupsSharingAName_OpenFoldedWhenEitherCarriesTheFlag()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", ["Heaters"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Heaters"], "#00ff00"));

		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Heaters", [1]), new StoredGroup(2, "Heaters", [2], FoldedOnStart: true)]);

		var heaters = Header(legend, "Heaters");
		heaters.Rows.Should().HaveCount(2);
		heaters.IsFolded.Should().BeTrue();
	}

	[AvaloniaFact]
	public void APickOnTheChartOfAPenFoldedAway_UnfoldsItsGroup()
	{
		const double PressureValue = 20.0;
		const double HeaterValue = 80.0;
		const int WindowSeconds = 60;
		var (chart, _, dataRect) = ChartTestBuilder.RenderTwoFlatPens(
			(FakeDataProvider.FirstPen, PressureValue),
			(FakeDataProvider.SecondPen, HeaterValue),
			WindowSeconds,
			activePenId: 1);
		using var legend = new TrendLegendViewModel(
			chart,
			[new StoredGroup(1, "Pressures", [1]), new StoredGroup(2, "Heaters", [2], FoldedOnStart: true)]);
		var heaterAxes = chart.FindPen(2)!.Line.Axes;
		var middle = LocalTimeAxis.ToAxis(ChartTestBuilder.From.AddSeconds(WindowSeconds / 2));
		var onHeaterLine = new Pixel(
			heaterAxes.XAxis.GetPixel(middle, dataRect),
			heaterAxes.YAxis.GetPixel(HeaterValue, dataRect));

		chart.PickPenAt(onHeaterLine);

		chart.ActivePenId.Should().Be(2);
		Header(legend, "Heaters").IsFolded.Should().BeFalse();
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
