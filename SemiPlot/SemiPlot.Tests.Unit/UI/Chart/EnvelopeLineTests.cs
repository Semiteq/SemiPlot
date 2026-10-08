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
	private const double UnboundedRequestedEndX = double.PositiveInfinity;
	private const int RawBandColumns = HistoryPrefetch.MarginColumnFactor * HistoryColumnTarget.MaxColumns;

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
		state.LoadHistory(Envelope(LongColumnCount), _start.AddSeconds(LongColumnCount));

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
	public void AReplaceKeepsOnlyTheAppendedColumnsPastItsEnd()
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns([.. ColumnsFrom(0.0, 3), Column(10.0)], UnboundedRequestedEndX);
		line.AppendColumn(Column(11.0)).Should().BeTrue();
		line.AppendColumn(Column(12.0)).Should().BeTrue();

		line.ReplaceColumns(ColumnsFrom(0.0, 4), UnboundedRequestedEndX);

		line.Columns.Select(column => column.X).Should().Equal(
			[0.0, 1.0, 2.0, 3.0, 11.0, 12.0],
			"the replaced column at 10 is history, not a live append");
	}

	[Fact]
	public void AReplaceEndingAtAnAppendedColumnTakesItsPlace()
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns(ColumnsFrom(0.0, 2), UnboundedRequestedEndX);
		line.AppendColumn(new EnvelopeColumn(2.0, 7.0, 7.0, 7.0)).Should().BeTrue();
		line.AppendColumn(Column(3.0)).Should().BeTrue();

		line.ReplaceColumns(ColumnsFrom(0.0, 3), UnboundedRequestedEndX);

		line.Columns.Select(column => column.X).Should().Equal(0.0, 1.0, 2.0, 3.0);
		line.Columns[2].Center.Should().Be(Column(2.0).Center, "the read's column replaces the live one at its X");
	}

	[Fact]
	public void AReplaceDropsTheAppendedColumnsPastTheRequestedEnd()
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns(ColumnsFrom(0.0, 2), UnboundedRequestedEndX);
		line.AppendColumn(Column(5.0)).Should().BeTrue();
		line.AppendColumn(Column(6.0)).Should().BeTrue();
		line.AppendColumn(Column(7.0)).Should().BeTrue();

		line.ReplaceColumns(ColumnsFrom(0.0, 3), requestedEndX: 6.0);

		line.Columns.Select(column => column.X).Should().Equal(0.0, 1.0, 2.0, 5.0, 6.0);
	}

	[Fact]
	public void ASecondReplaceKeepsTheTailTheFirstOneKept()
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns(ColumnsFrom(0.0, 3), UnboundedRequestedEndX);
		line.AppendColumn(Column(10.0)).Should().BeTrue();
		line.AppendColumn(Column(11.0)).Should().BeTrue();
		line.ReplaceColumns(ColumnsFrom(0.0, 2), UnboundedRequestedEndX);

		line.ReplaceColumns(ColumnsFrom(0.0, 4), UnboundedRequestedEndX);

		line.Columns.Select(column => column.X).Should().Equal(0.0, 1.0, 2.0, 3.0, 10.0, 11.0);
	}

	[Fact]
	public void AfterAppendsPastTheCap_AReplaceKeepsTheNewestAppendedColumnsPastItsEnd()
	{
		var line = new EnvelopeLine();
		for (var x = 0; x <= ColumnCap; x++)
		{
			line.AppendColumn(Column(x));
		}

		line.ReplaceColumns([Column(-1.0), Column(ColumnCap / 2)], UnboundedRequestedEndX);

		var newestAppended = Enumerable.Range((ColumnCap / 2) + 1, ColumnCap / 2).Select(x => (double)x);
		double[] expected = [-1.0, ColumnCap / 2, .. newestAppended];
		line.Columns.Select(column => column.X).Should().Equal(expected);
	}

	[Fact]
	public void ATrimPastTheCap_KeepsEveryAppendedColumnInTheTail()
	{
		const int HistoryCount = 2 * TrimChunk;
		var line = new EnvelopeLine();
		line.ReplaceColumns(ColumnsFrom(-HistoryCount, HistoryCount), UnboundedRequestedEndX);
		var appended = ColumnCap - HistoryCount + 1;
		for (var x = 0; x < appended; x++)
		{
			line.AppendColumn(Column(x));
		}

		line.ReplaceColumns([Column(-1.0)], UnboundedRequestedEndX);

		line.Columns.Should().HaveCount(1 + appended, "the trim removed history alone");
		line.Columns[1].X.Should().Be(0.0);
		line.Columns[^1].X.Should().Be(appended - 1);
	}

	[Fact]
	public void AMergePastTheCap_TrimsTheOldestColumnsToTheCap()
	{
		const int Half = ColumnCap / 2;
		var line = new EnvelopeLine();
		line.ReplaceColumns(ColumnsFrom(-Half, Half), UnboundedRequestedEndX);
		for (var x = 0; x < Half; x++)
		{
			line.AppendColumn(Column(x));
		}

		line.ReplaceColumns(ColumnsFrom(-Half - 1, Half + 1), UnboundedRequestedEndX);

		line.Columns.Should().HaveCount(ColumnCap);
		line.Columns[0].X.Should().Be(-Half, "the one column past the cap is the oldest history column");
		line.Columns[^1].X.Should().Be(Half - 1);
	}

	[Fact]
	public void AfterAFollowPhasePastTheCap_APanIntoThePastKeepsTheBandItRead()
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns(ColumnsFrom(0.0, 2), UnboundedRequestedEndX);
		var liveX = 2.0;
		while (line.Columns[0].X < TrimChunk)
		{
			line.AppendColumn(Column(liveX++));
		}

		var band = ColumnsFrom(-RawBandColumns, RawBandColumns);
		line.ReplaceColumns(band, requestedEndX: 0.0);

		line.Columns.Should().Equal(band, "the follow phase's tail lies past the range the pan read");

		for (var appended = 0; appended < ColumnCap - RawBandColumns; appended++)
		{
			line.AppendColumn(Column(liveX++));
		}

		line.Columns[0].Should().Be(band[0], "the band keeps the budget of the whole buffer");
	}

	[Fact]
	public void AFoldAfterAGap_OpensAColumnPastItAndFoldsIntoThatOne()
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns([Column(0.0), new EnvelopeColumn(1.0, double.NaN, double.NaN, double.NaN)], 1.0);

		line.FoldIntoLastColumn(new EnvelopeColumn(1.0, 7.0, 7.0, 7.0)).Should().BeFalse("it would run backwards");
		line.FoldIntoLastColumn(new EnvelopeColumn(2.0, 7.0, 7.0, 7.0)).Should().BeTrue();
		line.FoldIntoLastColumn(new EnvelopeColumn(3.0, 9.0, 9.0, 9.0)).Should().BeTrue();

		line.Columns.Select(column => column.X).Should().Equal(0.0, 1.0, 2.0);
		double.IsNaN(line.Columns[1].Center).Should().BeTrue("the gap still breaks the line");
		line.Columns[^1].Should().Be(new EnvelopeColumn(2.0, 7.0, 9.0, 9.0));
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
			],
			UnboundedRequestedEndX);

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
			],
			UnboundedRequestedEndX);
		plot.Axes.SetLimitsX(0.0, 1.0);
		plot.Axes.SetLimitsY(LogAxisBottomDecade, LogAxisTopDecade);

		using var image = plot.GetImage(PlotWidth, PlotHeight);
		var dataRect = plot.RenderManager.LastRender.Layout.DataRect;

		return RedStroke.RowsIn(image.GetArrayRGB(), dataRect, (int)Math.Round(dataRect.HorizontalCenter));
	}

	private static EnvelopeColumn Column(double x)
	{
		return new EnvelopeColumn(x, 0.0, 1.0, 0.5);
	}

	private static EnvelopeColumn[] ColumnsFrom(double firstX, int count)
	{
		return [.. Enumerable.Range(0, count).Select(index => Column(firstX + index))];
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
			var requestedTo = realtime.AddHours(1.0);
			state.LoadHistory(longEnvelope, requestedTo);
			state.LoadHistory(shortEnvelope, requestedTo);

			realtime = realtime.AddSeconds(1);
			state.AppendRealtime(realtime, 1.0);
			state.FoldRealtime(realtime, 0.5);

			state.ClearHistory(requestedTo);

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
