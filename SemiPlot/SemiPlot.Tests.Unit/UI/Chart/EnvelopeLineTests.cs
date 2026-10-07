using AwesomeAssertions;

using ScottPlot;
using ScottPlot.TickGenerators;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

// docs/architecture/charting.md#per-pen-plottable-envelopeline
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class EnvelopeLineTests
{
	private const int PenId = 1;
	private const string PenColorHex = "#ff0000";
	private const int PlotWidth = 400;
	private const int PlotHeight = 300;
	private const double YAxisMin = -2.0;
	private const double YAxisMax = 2.0;
	private const int FrameBudget = 300;
	private const int LongColumnCount = 6000;
	private const int ShortColumnCount = 2000;
	private const int GapEvery = 500;
	private const int TestTimeoutMilliseconds = 120_000;
	private const int ColumnCap = 100_000;
	private const int TrimChunk = ColumnCap / 10;
	private const double FlatValue = 1e-4;
	private const double LogAxisBottomDecade = -6.0;
	private const double LogAxisTopDecade = -2.0;

	private static readonly TimeSpan _runBudget = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan _joinBudget = TimeSpan.FromSeconds(30);
	// Yields between rewrites so the render task reaches its frame budget inside the run budget.
	private static readonly TimeSpan _mutationPause = TimeSpan.FromMilliseconds(1);
	private static readonly DateTime _start = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Render_WhileTheUiThreadRewritesTheColumns_DoesNotThrow()
	{
		var line = new EnvelopeLine();

		using var plot = new Plot();
		line.Axes.XAxis = plot.Axes.Bottom;
		plot.Add.Plottable(line);

		var state = new TrendPenState(new Pen(PenId, "Pen 1", ["Group A"], PenColorHex), line);
		state.LoadHistory(Envelope(LongColumnCount));

		plot.Axes.SetLimitsX(
			LocalTimeAxis.ToAxis(_start),
			LocalTimeAxis.ToAxis(_start.AddSeconds(LongColumnCount)));
		plot.Axes.SetLimitsY(YAxisMin, YAxisMax);

		plot.GetPlottables().Should().Contain(line);
		RenderFrame(plot);

		using var cancellation = new CancellationTokenSource(_runBudget);

		// Written by the render task, read by the test thread only after joining it.
		var frames = 0;

		var renderTask = Task.Run(
			() =>
			{
				while (frames < FrameBudget && !cancellation.Token.IsCancellationRequested)
				{
					RenderFrame(plot);
					_ = line.GetAxisLimits();
					frames++;
				}
			},
			TestContext.Current.CancellationToken);

		Exception? failure;

		try
		{
			RewriteColumns(state, renderTask, cancellation.Token);
		}
		finally
		{
			await cancellation.CancelAsync();
			failure = await Record.ExceptionAsync(
				() => renderTask.WaitAsync(_joinBudget, TestContext.Current.CancellationToken));
		}

		failure.Should().BeNull(
			"the render thread must survive concurrent rewrites; it stopped at frame {0}",
			frames);
		frames.Should().Be(FrameBudget);
	}

	[Fact]
	public void AppendingPastTheCapTrimsOneChunk()
	{
		var line = new EnvelopeLine();
		var peakCount = 0;

		for (var index = 0; index <= ColumnCap; index++)
		{
			line.AppendColumn(new EnvelopeColumn(index, 0.0, 1.0, 0.5)).Should().BeTrue();
			peakCount = Math.Max(peakCount, line.Columns.Count);
		}

		line.Columns.Should().HaveCount(ColumnCap + 1 - TrimChunk);
		line.Columns[0].X.Should().Be(TrimChunk);
		peakCount.Should().Be(ColumnCap);

		for (var index = ColumnCap + 1; index < ColumnCap + TrimChunk; index++)
		{
			line.AppendColumn(new EnvelopeColumn(index, 0.0, 1.0, 0.5));
		}

		line.Columns.Should().HaveCount(ColumnCap);
		line.Columns[0].X.Should().Be(TrimChunk);
	}

	[Fact]
	public void Render_ProjectsThroughLog10OnlyUnderALogTickGenerator()
	{
		var underLogTicks = RowsOfAFlatLine(new LogTickGenerator());
		var underStockTicks = RowsOfAFlatLine(new NumericAutomatic());

		underLogTicks.Should().NotBeEmpty("log10(1e-4) is -4, inside the -6..-2 axis");
		underStockTicks.Should().BeEmpty("1e-4 read as a linear value lies above an axis whose top is -2");
	}

	[Fact]
	public void GetAxisLimits_UnderALogTickGenerator_ReportsNoVerticalLimits()
	{
		using var plot = new Plot();
		var line = new EnvelopeLine();
		line.Axes.YAxis = plot.Axes.Left;
		line.ReplaceColumns(
		[
			new EnvelopeColumn(0.0, FlatValue, FlatValue, FlatValue),
			new EnvelopeColumn(1.0, FlatValue, FlatValue, FlatValue)
		]);

		var linearLimits = line.GetAxisLimits();
		line.Axes.YAxis.TickGenerator = new LogTickGenerator();
		var logLimits = line.GetAxisLimits();

		linearLimits.Should().Be(new AxisLimits(0.0, 1.0, FlatValue, FlatValue));
		logLimits.Should().Be(
			AxisLimits.HorizontalOnly(0.0, 1.0), "the columns are in data units, the axis in decades");
	}

	private static IReadOnlyList<int> RowsOfAFlatLine(ITickGenerator tickGenerator)
	{
		using var plot = new Plot();
		var line = new EnvelopeLine();
		line.Axes.XAxis = plot.Axes.Bottom;
		line.Axes.YAxis = plot.Axes.Left;
		plot.Axes.Left.TickGenerator = tickGenerator;
		plot.Add.Plottable(line);
		line.Restyle(new Color(PenColorHex), PenLineStyle.Interpolated);
		line.ReplaceColumns(
		[
			new EnvelopeColumn(0.0, FlatValue, FlatValue, FlatValue),
			new EnvelopeColumn(1.0, FlatValue, FlatValue, FlatValue)
		]);
		plot.Axes.SetLimitsX(0.0, 1.0);
		plot.Axes.SetLimitsY(LogAxisBottomDecade, LogAxisTopDecade);

		using var image = plot.GetImage(PlotWidth, PlotHeight);
		var dataRect = plot.RenderManager.LastRender.Layout.DataRect;

		return RedStroke.RowsIn(image.GetArrayRGB(), dataRect, (int)Math.Round(dataRect.HorizontalCenter));
	}

	private static void RenderFrame(Plot plot)
	{
		using var image = plot.GetImage(PlotWidth, PlotHeight);
	}

	private static void RewriteColumns(TrendPenState state, Task renderTask, CancellationToken cancellationToken)
	{
		var longEnvelope = Envelope(LongColumnCount);
		var shortEnvelope = Envelope(ShortColumnCount);
		var realtime = _start.AddSeconds(LongColumnCount);

		while (!cancellationToken.IsCancellationRequested && !renderTask.IsCompleted)
		{
			state.LoadHistory(longEnvelope);
			state.LoadHistory(shortEnvelope);

			realtime = realtime.AddSeconds(1);
			state.AppendRealtime(realtime, 1.0);
			state.FoldRealtime(0.5);

			state.ClearHistory();

			Thread.Sleep(_mutationPause);
		}
	}

	private static PenHistoryEnvelope Envelope(int columnCount)
	{
		var timestamps = new List<DateTime>(columnCount);
		var min = new List<double>(columnCount);
		var max = new List<double>(columnCount);
		var center = new List<double>(columnCount);

		for (var index = 0; index < columnCount; index++)
		{
			var isGap = index % GapEvery == GapEvery - 1;

			timestamps.Add(_start.AddSeconds(index));
			min.Add(isGap ? double.NaN : -1.0);
			max.Add(isGap ? double.NaN : 1.0);
			center.Add(isGap ? double.NaN : 0.0);
		}

		return new PenHistoryEnvelope(PenId, timestamps, min, max, center);
	}
}
