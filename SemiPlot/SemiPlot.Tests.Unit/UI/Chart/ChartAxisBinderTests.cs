using AwesomeAssertions;

using ScottPlot;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartAxisBinderTests
{
	[Fact]
	public void EveryAxisTheBinderCreates_IsALeftAxis()
	{
		using var plot = new Plot();
		var binder = new ChartAxisBinder(plot);

		binder.Apply([Scale(1, isActive: true), Scale(2), Scale(3)], Pens(1, 2, 3));

		binder.AxesByPenId.Should().HaveCount(3);
		binder.AxesByPenId.Values.Should().OnlyContain(axis => axis.Edge == Edge.Left);
	}

	[Fact]
	public void ChangingTheActivePen_LeavesTheOneVisibleAxisOnTheLeft()
	{
		using var plot = new Plot();
		var binder = new ChartAxisBinder(plot);
		var pens = Pens(1, 2);

		binder.Apply([Scale(1, isActive: true), Scale(2)], pens);
		VisibleAxes(binder).Should().ContainSingle().Which.Edge.Should().Be(Edge.Left);

		binder.Apply([Scale(1), Scale(2, isActive: true)], pens);
		VisibleAxes(binder).Should().ContainSingle().Which.Edge.Should().Be(Edge.Left);
	}

	// Only a visible pen's axis may be drawn, so the switched-off active pen leaves the plot with none.
	[Fact]
	public void TheActivePenSwitchedOff_DrawsNoAxisAtAll()
	{
		using var plot = new Plot();
		var binder = new ChartAxisBinder(plot);
		var pens = Pens(1, 2);
		pens[1].IsVisible = false;

		binder.Apply([Scale(1, isActive: true), Scale(2)], pens);

		VisibleAxes(binder).Should().BeEmpty();
	}

	[Fact]
	public void TheDrawnAxis_OwnsTheHorizontalGridlines()
	{
		using var plot = new Plot();
		var binder = new ChartAxisBinder(plot);
		var pens = Pens(1, 2, 3);

		binder.Apply([Scale(1, isActive: true), Scale(2), Scale(3)], pens);
		plot.Grid.YAxis.Should().BeSameAs(binder.AxesByPenId[1]);

		binder.Apply([Scale(1), Scale(2, isActive: true), Scale(3)], pens);
		plot.Grid.YAxis.Should().BeSameAs(binder.AxesByPenId[2]);
	}

	private static IEnumerable<IYAxis> VisibleAxes(ChartAxisBinder binder)
	{
		return binder.AxesByPenId.Values.Where(axis => axis.IsVisible);
	}

	private static Dictionary<int, TrendPenState> Pens(params int[] penIds)
	{
		return penIds.ToDictionary(
			penId => penId,
			penId => new TrendPenState(new Pen(penId, $"Pen {penId}", [], "#ff0000"), new EnvelopeLine()));
	}

	private static PenScale Scale(int penId, bool isActive = false)
	{
		return new PenScale(penId, Min: 0.0, Max: 1.0, ScaleMode.Auto, isActive, IsLogarithmic: false);
	}
}
