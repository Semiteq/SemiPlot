using AwesomeAssertions;

using ScottPlot;
using ScottPlot.TickGenerators;

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
	private const float AxisPixels = 400f;
	private const double DecadeTolerance = 1e-12;
	private const string ExponentMask = "0.0E+0";
	private const string FinerExponentMask = "0.00E+0";
	private const string FixedPointMask = "0.000";

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
		pens[1].SetVisibility(false);

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

	[Fact]
	public void ALogScale_SetsTheAxisLimitsInDecades()
	{
		using var plot = new Plot();
		var binder = new ChartAxisBinder(plot);

		binder.Apply([LogScale(1, 1e-6, 1e-2, isActive: true)], Pens(1));

		var axis = binder.AxesByPenId[1];
		axis.Min.Should().BeApproximately(-6.0, DecadeTolerance);
		axis.Max.Should().BeApproximately(-2.0, DecadeTolerance);
		axis.TickGenerator.Should().BeOfType<LogTickGenerator>();
	}

	[Fact]
	public void ALinearScale_GivesALogAxisItsStockGeneratorBack()
	{
		using var plot = new Plot();
		var binder = new ChartAxisBinder(plot);
		var pens = Pens(1);

		binder.Apply([LogScale(1, 1e-6, 1e-2, isActive: true)], pens);
		binder.Apply([Scale(1, isActive: true)], pens);

		var axis = binder.AxesByPenId[1];
		axis.TickGenerator.Should().BeOfType<NumericAutomatic>();
		axis.Min.Should().Be(0.0);
		axis.Max.Should().Be(1.0);
	}

	[Fact]
	public void AMaskRevision_ChangesTheMajorLabels()
	{
		using var plot = new Plot();
		var binder = new ChartAxisBinder(plot);
		var pens = Pens(1);
		var scales = new[] { LogScale(1, 1e-6, 1e-1, isActive: true) };
		pens[1].Revise(pens[1].Pen with { Format = ExponentMask });
		binder.Apply(scales, pens);
		var before = MajorTicks(binder.AxesByPenId[1]);

		pens[1].Revise(pens[1].Pen with { Format = FinerExponentMask });
		binder.Apply(scales, pens);
		var after = MajorTicks(binder.AxesByPenId[1]);

		before.Should().NotBeEmpty().And.AllSatisfy(tick => tick.Label.Should().Be(LabelOf(tick, ExponentMask)));
		after.Should().NotBeEmpty().And.AllSatisfy(tick => tick.Label.Should().Be(LabelOf(tick, FinerExponentMask)));
	}

	[Fact]
	public void AFixedPointMask_LabelsNoMajorWithTheZeroItCannotTellFromTheDecade()
	{
		using var plot = new Plot();
		var binder = new ChartAxisBinder(plot);
		var pens = Pens(1);
		pens[1].Revise(pens[1].Pen with { Format = FixedPointMask });

		binder.Apply([LogScale(1, 1e-6, 1e-1, isActive: true)], pens);

		MajorTicks(binder.AxesByPenId[1]).Select(tick => tick.Label).Should().Equal(
			PenValueFormat.Format(1e-3, FixedPointMask),
			PenValueFormat.Format(1e-2, FixedPointMask),
			PenValueFormat.Format(1e-1, FixedPointMask));
	}

	[Fact]
	public void TheMinorGridWidth_FollowsTheDrawnAxis()
	{
		using var plot = new Plot();
		var binder = new ChartAxisBinder(plot);
		var pens = Pens(1, 2);

		binder.Apply([LogScale(1, 1e-6, 1e-2, isActive: true), Scale(2)], pens);
		plot.Grid.YAxisStyle.MinorLineStyle.Width.Should().Be(1f);

		binder.Apply([LogScale(1, 1e-6, 1e-2), Scale(2, isActive: true)], pens);
		plot.Grid.YAxisStyle.MinorLineStyle.Width.Should().Be(0f);

		binder.Apply([Scale(1, isActive: true), Scale(2)], pens);
		plot.Grid.YAxisStyle.MinorLineStyle.Width.Should().Be(0f, "the drawn axis turned linear");
	}

	private static List<Tick> MajorTicks(IYAxis axis)
	{
		using var paint = Paint.NewDisposablePaint();
		axis.RegenerateTicks(new PixelLength(AxisPixels), paint);

		return [.. axis.TickGenerator.Ticks.Where(tick => tick.IsMajor)];
	}

	private static string LabelOf(Tick tick, string mask)
	{
		return PenValueFormat.Format(Math.Pow(10.0, tick.Position), mask);
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

	private static PenScale LogScale(int penId, double min, double max, bool isActive = false)
	{
		return new PenScale(penId, min, max, ScaleMode.Manual, isActive, IsLogarithmic: true);
	}
}
