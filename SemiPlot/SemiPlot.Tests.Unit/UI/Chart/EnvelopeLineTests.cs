using AwesomeAssertions;

using ScottPlot;

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

	private static readonly TimeSpan _runBudget = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan _joinBudget = TimeSpan.FromSeconds(30);
	// Yields between rewrites so the render task reaches its frame budget inside the run budget.
	private static readonly TimeSpan _mutationPause = TimeSpan.FromMilliseconds(1);
	private static readonly DateTime _start = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Render_WhileTheUiThreadRewritesTheColumns_DoesNotThrow()
	{
		var line = new EnvelopeLine { Color = new Color(PenColorHex) };

		using var plot = new Plot();
		line.Axes.XAxis = plot.Axes.Bottom;
		plot.Add.Plottable(line);

		var state = new TrendPenState(new Pen(PenId, "Pen 1", "Group A", PenColorHex), line);
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
