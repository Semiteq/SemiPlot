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
	private const float AxisPixels = 400f;
	private const double DecadeTolerance = 1e-12;
	private const string ExponentMask = "0.0E+0";
	private const string FinerExponentMask = "0.00E+0";
	private const string FixedPointMask = "0.000";

	private static readonly TimeSpan _lockTimeout = TimeSpan.FromSeconds(10.0);
	private static readonly TimeSpan _heldRenderWindow = TimeSpan.FromMilliseconds(200.0);

	[Fact]
	public void EveryAxisTheBinderCreates_IsALeftAxis()
	{
		using var plot = new Plot();
		var binder = new ChartAxisBinder(plot);

		binder.Apply([Scale(1, isActive: true), Scale(2), Scale(3)], Pens(1, 2, 3));

		binder.AxesByPenId.Should().HaveCount(3);
		binder.AxesByPenId.Values.Should().OnlyContain(axis => axis.Edge == Edge.Left);
		binder.AxesByPenId.Values.Should().OnlyContain(axis => axis.TickGenerator is LinearTickGenerator);
	}

	[Fact]
	public void OnlyTheActiveVisiblePensGenerator_IsDrawn()
	{
		using var plot = new Plot();
		var binder = new ChartAxisBinder(plot);
		var pens = Pens(1, 2, 3);

		binder.Apply([Scale(1, isActive: true), Scale(2), Scale(3)], pens);
		DrawnPenIds(binder).Should().Equal(1);

		binder.Apply([Scale(1), Scale(2, isActive: true), Scale(3)], pens);
		DrawnPenIds(binder).Should().Equal(2);

		pens[2].SetVisibility(false);
		binder.Apply([Scale(1), Scale(2, isActive: true), Scale(3)], pens);
		DrawnPenIds(binder).Should().BeEmpty();
	}

	[Fact]
	public void AHiddenLogPensAxis_StaysLogarithmic()
	{
		using var plot = new Plot();
		var binder = new ChartAxisBinder(plot);
		var pens = Pens(1, 2);

		binder.Apply([LogScale(1, 1e-6, 1e-2, isActive: true), Scale(2)], pens);
		binder.Apply([LogScale(1, 1e-6, 1e-2), Scale(2, isActive: true)], pens);

		var axis = binder.AxesByPenId[1];
		axis.IsVisible.Should().BeFalse();
		axis.TickGenerator.Should().BeOfType<LogTickGenerator>().Which.IsDrawn.Should().BeFalse();
		axis.Min.Should().BeApproximately(-6.0, DecadeTolerance);
	}

	[Fact]
	public void HideAxis_StopsTheAxisAndItsGenerator()
	{
		using var plot = new Plot();
		var binder = new ChartAxisBinder(plot);
		binder.Apply([Scale(1, isActive: true)], Pens(1));

		binder.HideAxis(1);

		var axis = binder.AxesByPenId[1];
		axis.IsVisible.Should().BeFalse();
		axis.TickGenerator.Should().BeOfType<LinearTickGenerator>().Which.IsDrawn.Should().BeFalse();
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
	public void ALinearScale_GivesALogAxisItsLinearGeneratorBack()
	{
		using var plot = new Plot();
		var binder = new ChartAxisBinder(plot);
		var pens = Pens(1);

		binder.Apply([LogScale(1, 1e-6, 1e-2, isActive: true)], pens);
		binder.Apply([Scale(1, isActive: true)], pens);

		var axis = binder.AxesByPenId[1];
		axis.TickGenerator.Should().BeOfType<LinearTickGenerator>().Which.IsDrawn.Should().BeTrue();
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

	[Fact]
	public async Task APanStep_DoesNotWaitForARenderInProgress()
	{
		using var plot = new Plot();
		var binder = new ChartAxisBinder(plot);
		var pens = Pens(1, 2);
		binder.Apply([Scale(1, isActive: true), Scale(2)], pens);
		PenScale[] panned =
		[
			new(1, Min: 0.5, Max: 1.5, ScaleMode.Auto, IsActive: true, IsLogarithmic: false),
			new(2, Min: 0.5, Max: 1.5, ScaleMode.Auto, IsActive: false, IsLogarithmic: false)
		];

		var cancellation = TestContext.Current.CancellationToken;

		using var render = new RenderInProgress(plot);
		var panStep = Task.Run(() => binder.Apply(panned, pens), cancellation);
		var finished = await Task.WhenAny(panStep, Task.Delay(_lockTimeout, cancellation));

		finished.Should().BeSameAs(panStep, "a pan step changes no drawn axis, so it takes no plot lock");
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task AVisibilityChange_WaitsForTheRenderInProgress(bool isShown)
	{
		using var plot = new Plot();
		var binder = new ChartAxisBinder(plot);
		var pens = Pens(1);
		binder.Apply([Scale(1, isActive: true)], pens);
		if (isShown)
		{
			pens[1].SetVisibility(false);
			binder.Apply([Scale(1, isActive: true)], pens);
		}

		pens[1].SetVisibility(isShown);
		var cancellation = TestContext.Current.CancellationToken;
		Task change;
		using (new RenderInProgress(plot))
		{
			change = Task.Run(() => binder.Apply([Scale(1, isActive: true)], pens), cancellation);
			var finished = await Task.WhenAny(change, Task.Delay(_heldRenderWindow, cancellation));

			finished.Should().NotBeSameAs(change, "the axis and its generator change together between two renders");
		}

		await change.WaitAsync(_lockTimeout, cancellation);
		binder.AxesByPenId[1].IsVisible.Should().Be(isShown);
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

	private static IEnumerable<int> DrawnPenIds(ChartAxisBinder binder)
	{
		return binder.AxesByPenId
			.Where(entry => entry.Value.TickGenerator is IDrawnTickGenerator { IsDrawn: true })
			.Select(entry => entry.Key);
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

	/// <summary>Holds <c>Plot.Sync</c> on a thread of its own, as ScottPlot's render does, until disposed.</summary>
	private sealed class RenderInProgress : IDisposable
	{
		private readonly ManualResetEventSlim _entered = new();
		private readonly ManualResetEventSlim _finished = new();
		private readonly Thread _thread;

		public RenderInProgress(Plot plot)
		{
			_thread = new Thread(() =>
			{
				lock (plot.Sync)
				{
					_entered.Set();
					_finished.Wait();
				}
			});
			_thread.Start();
			_entered.Wait();
		}

		public void Dispose()
		{
			_finished.Set();
			_thread.Join();
			_entered.Dispose();
			_finished.Dispose();
		}
	}
}
