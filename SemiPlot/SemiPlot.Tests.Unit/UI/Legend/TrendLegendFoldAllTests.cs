using System.Reactive.Linq;
using System.Windows.Input;

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
public sealed class TrendLegendFoldAllTests
{
	private readonly TestScheduler _scheduler = new();

	[AvaloniaFact]
	public void FoldAll_FoldsEveryHeaderWhileAnyIsUnfolded()
	{
		using var legend = ThreeGroupLegend(CreateChart());

		legend.AreAllFolded.Should().BeFalse("one header opened folded, two did not");
		legend.FoldAllText.Should().Be(Resources.LegendFoldAll);
		legend.FoldAllGroupsText.Should().Be(Resources.LegendFoldAllGroups);

		legend.ToggleAllFoldsCommand.Execute().Subscribe();

		legend.Groups.Should().AllSatisfy(group => group.IsFolded.Should().BeTrue());
		legend.AreAllFolded.Should().BeTrue();
		legend.FoldAllText.Should().Be(Resources.LegendUnfoldAll);
		legend.FoldAllGroupsText.Should().Be(Resources.LegendUnfoldAllGroups);
	}

	[AvaloniaFact]
	public void FoldAll_UnfoldsEveryHeaderWhenAllAreFolded()
	{
		using var legend = ThreeGroupLegend(CreateChart());
		legend.ToggleAllFoldsCommand.Execute().Subscribe();

		legend.ToggleAllFoldsCommand.Execute().Subscribe();

		legend.Groups.Should().AllSatisfy(group => group.IsFolded.Should().BeFalse());
		legend.AreAllFolded.Should().BeFalse();
		legend.FoldAllText.Should().Be(Resources.LegendFoldAll);
	}

	[AvaloniaFact]
	public void TheLabel_FollowsTheHeadersOwnFolds()
	{
		using var legend = ThreeGroupLegend(CreateChart());

		foreach (var group in legend.Groups.Where(group => !group.IsFolded))
		{
			group.FoldCommand.Execute().Subscribe();
		}

		legend.AreAllFolded.Should().BeTrue();
		legend.FoldAllText.Should().Be(Resources.LegendUnfoldAll);

		legend.Groups[0].FoldCommand.Execute().Subscribe();

		legend.AreAllFolded.Should().BeFalse();
		legend.FoldAllText.Should().Be(Resources.LegendFoldAll);
	}

	[AvaloniaFact]
	public void TheRule_FollowsTheHeadersARebuildDraws()
	{
		var chart = CreateChart();
		using var legend = ThreeGroupLegend(chart);
		legend.ToggleAllFoldsCommand.Execute().Subscribe();

		chart.AddPen(new Pen(4, "Pen 4", ["Valves"], "#ffff00"));
		legend.Rebuild([.. ThreeStoredGroups(), new StoredGroup(4, "Valves", [4])]);

		legend.AreAllFolded.Should().BeFalse("the new header opens unfolded");

		Header(legend, "Valves").FoldCommand.Execute().Subscribe();

		legend.AreAllFolded.Should().BeTrue("the rule reads the headers the rebuild drew");
	}

	[AvaloniaFact]
	public void FoldAll_CannotRunOnAHeaderlessList()
	{
		var chart = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", [], "#ff0000"));
		using var legend = new TrendLegendViewModel(chart, []);

		((ICommand)legend.ToggleAllFoldsCommand).CanExecute(null).Should().BeFalse();
		legend.AreAllFolded.Should().BeFalse("a list with no header is never all folded");
		legend.FoldAllText.Should().Be(Resources.LegendFoldAll);

		legend.Rebuild([new StoredGroup(1, "Heaters", [1])]);

		((ICommand)legend.ToggleAllFoldsCommand).CanExecute(null).Should().BeTrue();

		legend.ToggleAllFoldsCommand.Execute().Subscribe();
		legend.AreAllFolded.Should().BeTrue();
		legend.Rebuild([]);

		((ICommand)legend.ToggleAllFoldsCommand).CanExecute(null).Should().BeFalse();
		legend.AreAllFolded.Should().BeFalse("the rebuild drew no header");
	}

	[AvaloniaFact]
	public void FoldAll_CannotRunOnAnEmptyChart()
	{
		using var legend = new TrendLegendViewModel(CreateChart(), []);

		((ICommand)legend.ToggleAllFoldsCommand).CanExecute(null).Should().BeFalse();
		legend.AreAllFolded.Should().BeFalse();
	}

	[AvaloniaFact]
	public void APickAfterFoldAll_StillUnfoldsTheGroupHoldingThePen()
	{
		var chart = CreateChart();
		using var legend = ThreeGroupLegend(chart);
		legend.ToggleAllFoldsCommand.Execute().Subscribe();

		chart.SetActivePen(2).Should().BeTrue();

		Header(legend, "Heaters").IsFolded.Should().BeFalse();
		Header(legend, "Pressures").IsFolded.Should().BeTrue();
		Header(legend, "Dampers").IsFolded.Should().BeTrue();
		legend.AreAllFolded.Should().BeFalse();
		legend.FoldAllText.Should().Be(Resources.LegendFoldAll);
	}

	private static TrendLegendViewModel ThreeGroupLegend(TrendChartViewModel chart)
	{
		chart.AddPen(new Pen(1, "Pen 1", ["Pressures"], "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", ["Heaters"], "#00ff00"));
		chart.AddPen(new Pen(3, "Pen 3", ["Dampers"], "#0000ff"));

		return new TrendLegendViewModel(chart, ThreeStoredGroups());
	}

	private static StoredGroup[] ThreeStoredGroups()
	{
		return
		[
			new StoredGroup(1, "Pressures", [1]),
			new StoredGroup(2, "Heaters", [2], FoldedOnStart: true),
			new StoredGroup(3, "Dampers", [3])
		];
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
