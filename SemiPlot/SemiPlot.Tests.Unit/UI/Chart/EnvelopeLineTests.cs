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
	private const double TenByTenSpan = 10.0;
	private const double HitTolerance = 1e-3;
	private const double MissDistance = 10.0;
	private const double PinnedFloorPixels = 2.0;
	private const float ThickLineWidth = 5f;
	private const int RawBandColumns = 3 * HistoryColumnTarget.MaxColumns;

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

		var longEnvelope = Envelope(LongColumnCount);
		var shortEnvelope = Envelope(ShortColumnCount);
		var realtime = _start.AddSeconds(LongColumnCount);

		await RenderWhileMutating.RunAsync(
			() =>
			{
				RenderFrame(plot);
				_ = line.GetAxisLimits();
			},
			() =>
			{
				var requestedTo = realtime.AddHours(1.0);
				state.LoadHistory(longEnvelope, requestedTo);
				state.LoadHistory(shortEnvelope, requestedTo);

				realtime = realtime.AddSeconds(1);
				state.AppendRealtime(realtime, 1.0);
				state.FoldRealtime(realtime, 0.5);
				line.ConfirmLive(LocalTimeAxis.ToAxis(realtime));
				line.SetHoldEnd(LocalTimeAxis.ToAxis(realtime.AddSeconds(1)));

				state.ClearHistory(requestedTo);

				Thread.Sleep(_mutationPause);
			},
			"the render thread must survive concurrent rewrites");
	}

	[Fact]
	public void AppendingPastTheCapTrimsOneChunk()
	{
		var line = new EnvelopeLine();

		for (var index = 0; index < ColumnCap; index++)
		{
			line.AppendColumn(new EnvelopeColumn(index, 0.0, 1.0, 0.5)).Should().BeTrue();
		}

		line.SnapshotColumns().Should().HaveCount(ColumnCap, "the cap itself trims nothing");

		line.AppendColumn(new EnvelopeColumn(ColumnCap, 0.0, 1.0, 0.5)).Should().BeTrue();

		line.SnapshotColumns().Should().HaveCount(ColumnCap + 1 - TrimChunk);
		line.SnapshotColumns()[0].X.Should().Be(TrimChunk);

		for (var index = ColumnCap + 1; index < ColumnCap + TrimChunk; index++)
		{
			line.AppendColumn(new EnvelopeColumn(index, 0.0, 1.0, 0.5));
		}

		line.SnapshotColumns().Should().HaveCount(ColumnCap);
		line.SnapshotColumns()[0].X.Should().Be(TrimChunk);
	}

	[Fact]
	public void AReplaceKeepsOnlyTheAppendedColumnsPastItsEnd()
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns([.. ColumnsFrom(0.0, 3), Column(10.0)], UnboundedRequestedEndX);
		line.AppendColumn(Column(11.0)).Should().BeTrue();
		line.AppendColumn(Column(12.0)).Should().BeTrue();

		line.ReplaceColumns(ColumnsFrom(0.0, 4), UnboundedRequestedEndX)
			.Should().BeFalse("no column lies past the end");

		line.SnapshotColumns().Select(column => column.X).Should().Equal(
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

		line.SnapshotColumns().Select(column => column.X).Should().Equal(0.0, 1.0, 2.0, 3.0);
		line.SnapshotColumns()[2].Center.Should().Be(
			Column(2.0).Center, "the read's column replaces the live one at its X");
	}

	[Fact]
	public void AReplaceDropsTheAppendedColumnsPastTheRequestedEnd()
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns(ColumnsFrom(0.0, 2), UnboundedRequestedEndX);
		line.AppendColumn(Column(5.0)).Should().BeTrue();
		line.AppendColumn(Column(6.0)).Should().BeTrue();
		line.AppendColumn(Column(7.0)).Should().BeTrue();

		line.ReplaceColumns(ColumnsFrom(0.0, 3), requestedEndX: 6.0).Should().BeTrue();

		line.SnapshotColumns().Select(column => column.X).Should().Equal(0.0, 1.0, 2.0, 5.0, 6.0);
	}

	[Fact]
	public void AnAppendAfterBreakAtOrBeforeTheLastColumn_AppendsNothing()
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns(ColumnsFrom(0.0, 2), UnboundedRequestedEndX);

		line.AppendAfterBreak(Gap(0.5), Column(1.0)).Should().BeFalse();
		line.AppendAfterBreak(Gap(-1.0), Column(0.0)).Should().BeFalse();

		line.SnapshotColumns().Select(column => column.X).Should().Equal(0.0, 1.0);
	}

	[Fact]
	public void AnAppendAfterBreakOnAnEmptyLine_AppendsTheGapAndThenTheSample()
	{
		var line = new EnvelopeLine();

		line.AppendAfterBreak(Gap(4.0), Column(5.0)).Should().BeTrue();

		line.SnapshotColumns().Should().Equal(Gap(4.0), Column(5.0));
	}

	[Theory]
	[InlineData(false, 2.5, new[] { 0.0, 1.0, 2.5, 3.0 }, 1)]
	[InlineData(false, 1.0, new[] { 0.0, 1.0, 3.0 }, 0)]
	[InlineData(true, 2.5, new[] { 0.0, 1.0, 3.0 }, 1)]
	public void AnAppendAfterBreak_AppendsTheGapOnlyPastTheLastColumnOfALineNotEndingOnOne(
		bool endsOnGap,
		double gapX,
		double[] expectedXs,
		int expectedGaps)
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns(endsOnGap ? [Column(0.0), Gap(1.0)] : ColumnsFrom(0.0, 2), UnboundedRequestedEndX);

		line.AppendAfterBreak(Gap(gapX), Column(3.0)).Should().BeTrue();

		line.SnapshotColumns().Select(column => column.X).Should().Equal(expectedXs);
		line.SnapshotColumns().Count(column => double.IsNaN(column.Center)).Should().Be(expectedGaps);
	}

	[Fact]
	public void AnAheadBreakKeepsItsKindThroughAReplaceThatKeepsItAndAnAppendPastTheCap()
	{
		const double LastHistoryX = TrimChunk;
		const double AheadBreakX = LastHistoryX + 0.5;
		var line = new EnvelopeLine();
		var history = ColumnsFrom(0.0, TrimChunk + 1);
		line.ReplaceColumns(history, UnboundedRequestedEndX);
		line.AppendAfterBreak(AheadGap(AheadBreakX), Column(LastHistoryX + 1.0)).Should().BeTrue();

		line.ReplaceColumns(history, UnboundedRequestedEndX);

		line.SnapshotColumns()[TrimChunk + 1].Should().Be(AheadGap(AheadBreakX), "the kept tail carries the kind");

		var appendsToPassTheCap = ColumnCap + 1 - line.SnapshotColumns().Count;
		for (var appended = 0; appended < appendsToPassTheCap; appended++)
		{
			line.AppendColumn(Column(LastHistoryX + 2.0 + appended));
		}

		var columns = line.SnapshotColumns();
		columns.Should().HaveCount(ColumnCap + 1 - TrimChunk, "one append past the cap trims one chunk");
		columns[0].X.Should().Be(LastHistoryX);
		columns[1].Should().Be(AheadGap(AheadBreakX), "the trim moves the column, kind and all");
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

		line.SnapshotColumns().Select(column => column.X).Should().Equal(0.0, 1.0, 2.0, 3.0, 10.0, 11.0);
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
		line.SnapshotColumns().Select(column => column.X).Should().Equal(expected);
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

		line.SnapshotColumns().Should().HaveCount(1 + appended, "the trim removed history alone");
		line.SnapshotColumns()[1].X.Should().Be(0.0);
		line.SnapshotColumns()[^1].X.Should().Be(appended - 1);
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

		line.SnapshotColumns().Should().HaveCount(ColumnCap);
		line.SnapshotColumns()[0].X.Should().Be(-Half, "the one column past the cap is the oldest history column");
		line.SnapshotColumns()[^1].X.Should().Be(Half - 1);
	}

	[Fact]
	public void AfterAFollowPhasePastTheCap_APanIntoThePastKeepsTheBandItRead()
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns(ColumnsFrom(0.0, 2), UnboundedRequestedEndX);
		var liveX = 2.0;
		for (var appended = 0; appended < ColumnCap - 1; appended++)
		{
			line.AppendColumn(Column(liveX++));
		}

		line.SnapshotColumns()[0].X.Should().Be(TrimChunk, "the follow phase has trimmed one chunk");

		var band = ColumnsFrom(-RawBandColumns, RawBandColumns);
		line.ReplaceColumns(band, requestedEndX: 0.0);

		line.SnapshotColumns().Should().Equal(band, "the follow phase's tail lies past the range the pan read");

		for (var appended = 0; appended < ColumnCap - RawBandColumns; appended++)
		{
			line.AppendColumn(Column(liveX++));
		}

		line.SnapshotColumns()[0].Should().Be(band[0], "the band keeps the budget of the whole buffer");
	}

	[Fact]
	public void AFoldAfterAGap_OpensAColumnPastItAndFoldsIntoThatOne()
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns([Column(0.0), new EnvelopeColumn(1.0, double.NaN, double.NaN, double.NaN)], 1.0);

		line.FoldIntoLastColumn(new EnvelopeColumn(1.0, 7.0, 7.0, 7.0)).Should().BeFalse("it would run backwards");
		line.FoldIntoLastColumn(new EnvelopeColumn(2.0, 7.0, 7.0, 7.0)).Should().BeTrue();
		line.FoldIntoLastColumn(new EnvelopeColumn(3.0, 9.0, 9.0, 9.0)).Should().BeTrue();

		line.SnapshotColumns().Select(column => column.X).Should().Equal(0.0, 1.0, 2.0);
		double.IsNaN(line.SnapshotColumns()[1].Center).Should().BeTrue("the gap still breaks the line");
		line.SnapshotColumns()[^1].Should().Be(new EnvelopeColumn(2.0, 7.0, 9.0, 9.0));
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

	[Fact]
	public void DistanceTo_AClickOnAnInterpolatedLine_IsZero()
	{
		using var plot = new Plot();
		var (line, dataRect) = LineOverTenByTen(plot, [Flat(0.0, 2.0), Flat(10.0, 8.0)], PenLineStyle.Interpolated);

		line.DistanceTo(PixelAt(line, dataRect, 5.0, 5.0), dataRect).Should().BeApproximately(0.0, HitTolerance);
	}

	[Fact]
	public void DistanceTo_AClickOnASteppedHold_HitsItWhileAnInterpolatedPenOfTheSameColumnsDoesNot()
	{
		EnvelopeColumn[] columns = [Flat(0.0, 0.0), Flat(10.0, 10.0)];
		using var steppedPlot = new Plot();
		using var interpolatedPlot = new Plot();
		var (stepped, steppedRect) = LineOverTenByTen(steppedPlot, columns, PenLineStyle.Stepped);
		var (interpolated, interpolatedRect) = LineOverTenByTen(interpolatedPlot, columns, PenLineStyle.Interpolated);

		stepped.DistanceTo(PixelAt(stepped, steppedRect, 5.0, 0.0), steppedRect)
			.Should().BeApproximately(0.0, HitTolerance);
		interpolated.DistanceTo(PixelAt(interpolated, interpolatedRect, 5.0, 0.0), interpolatedRect)
			.Should().BeGreaterThan(MissDistance);
	}

	[Fact]
	public void DistanceTo_AClickInsideATallColumn_IsZero()
	{
		using var plot = new Plot();
		var (line, dataRect) = LineOverTenByTen(
			plot,
			[Flat(0.0, 5.0), new EnvelopeColumn(5.0, 0.0, 10.0, 5.0), Flat(10.0, 5.0)],
			PenLineStyle.Interpolated);

		line.DistanceTo(PixelAt(line, dataRect, 5.0, 8.0), dataRect).Should().BeApproximately(0.0, HitTolerance);
	}

	[Fact]
	public void DistanceTo_AClickInTheMiddleOfAGap_IsTheDistanceToTheNearerStrokedVertex()
	{
		using var plot = new Plot();
		var (line, dataRect) = LineOverTenByTen(
			plot,
			[
				Flat(0.0, 5.0),
				Flat(1.0, 5.0),
				new EnvelopeColumn(5.0, double.NaN, double.NaN, double.NaN),
				Flat(8.0, 5.0),
				Flat(9.0, 5.0)
			],
			PenLineStyle.Interpolated);
		var click = PixelAt(line, dataRect, 3.0, 5.0);
		var nearerVertex = PixelAt(line, dataRect, 1.0, 5.0);

		line.DistanceTo(click, dataRect).Should().BeApproximately(click.X - nearerVertex.X, HitTolerance);
	}

	[Fact]
	public void DistanceTo_OnALog10Axis_AValueAtOrBelowZeroIsHitAtThePinnedFloor()
	{
		using var plot = new Plot();
		var (line, dataRect) = LineOverTenByTen(
			plot,
			[Flat(0.0, 0.0), Flat(10.0, -5.0)],
			PenLineStyle.Interpolated,
			new LogTickGenerator(),
			LogAxisBottomDecade,
			LogAxisTopDecade);
		var onTheFloor = new Pixel(line.Axes.XAxis.GetPixel(5.0, dataRect), dataRect.Bottom - PinnedFloorPixels);

		line.DistanceTo(onTheFloor, dataRect).Should().BeApproximately(0.0, HitTolerance);
	}

	[Fact]
	public void DistanceTo_ALineWithNoColumns_IsPositiveInfinity()
	{
		using var plot = new Plot();
		var (line, dataRect) = LineOverTenByTen(plot, [], PenLineStyle.Interpolated);

		line.DistanceTo(new Pixel(dataRect.HorizontalCenter, dataRect.VerticalCenter), dataRect)
			.Should().Be(double.PositiveInfinity);
	}

	[Fact]
	public void DistanceTo_AClickInTheMarginOutsideTheDataArea_IsPositiveInfinity()
	{
		using var plot = new Plot();
		var (line, dataRect) = LineOverTenByTen(plot, [Flat(0.0, 5.0), Flat(10.0, 5.0)], PenLineStyle.Interpolated);
		var onTheLine = PixelAt(line, dataRect, 5.0, 5.0);

		line.DistanceTo(new Pixel(dataRect.Left - 1f, onTheLine.Y), dataRect).Should().Be(double.PositiveInfinity);
	}

	[Fact]
	public void DistanceTo_ALineJustPastTheTopEdge_IsNotHitAtTheEdge()
	{
		using var plot = new Plot();
		var (line, dataRect) = LineOverTenByTen(plot, [Flat(0.0, 10.1), Flat(10.0, 10.1)], PenLineStyle.Interpolated);
		var atTheEdge = new Pixel(dataRect.HorizontalCenter, dataRect.Top);

		line.DistanceTo(atTheEdge, dataRect).Should().Be(double.PositiveInfinity, "ScottPlot draws nothing there");
	}

	[Fact]
	public void DistanceTo_AClickOnTheHeldSegment_IsZero()
	{
		using var plot = new Plot();
		var (line, dataRect) = LineOverTenByTen(plot, [Flat(0.0, 2.0), Flat(2.0, 6.0)], PenLineStyle.Interpolated);
		var onTheHold = PixelAt(line, dataRect, 5.0, 6.0);

		line.DistanceTo(onTheHold, dataRect).Should().BeGreaterThan(MissDistance, "nothing is held yet");

		line.ConfirmLive(2.0);
		line.SetHoldEnd(8.0);

		line.DistanceTo(onTheHold, dataRect).Should().BeApproximately(0.0, HitTolerance);
	}

	[Fact]
	public void DistanceTo_AClickPastTheHoldEnd_MeasuresToTheHoldsEndPoint()
	{
		using var plot = new Plot();
		var (line, dataRect) = LineOverTenByTen(plot, [Flat(0.0, 2.0), Flat(2.0, 6.0)], PenLineStyle.Interpolated);
		line.ConfirmLive(2.0);
		line.SetHoldEnd(8.0);
		var click = PixelAt(line, dataRect, 9.0, 6.0);
		var holdEnd = PixelAt(line, dataRect, 8.0, 6.0);

		line.DistanceTo(click, dataRect).Should().BeApproximately(click.X - holdEnd.X, HitTolerance);
	}

	[Fact]
	public void HeldSpan_RunsFromTheLastColumnToTheHoldEndAtItsCenter()
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns(ColumnsFrom(0.0, 2), UnboundedRequestedEndX);

		line.ReadHeldSpan().Should().BeNull("no live value confirmed the line");

		line.ConfirmLive(1.0);

		line.ReadHeldSpan().Should().BeNull("no hold end is set");

		line.SetHoldEnd(4.0);

		line.ReadHeldSpan().Should().Be(new HeldSpan(1.0, 4.0, Column(1.0).Center));

		line.SetHoldEnd(double.NaN);

		line.ReadHeldSpan().Should().BeNull();
	}

	[Fact]
	public void ConfirmLive_KeepsTheSmallerOfTheValuesXAndTheLastColumnsX()
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns(ColumnsFrom(0.0, 2), UnboundedRequestedEndX);
		line.SetHoldEnd(4.0);

		line.ConfirmLive(3.0);

		line.ReadHeldSpan().Should().NotBeNull("a value past the last column confirms that column");

		line.ConfirmLive(0.5);
		line.ReplaceColumns(ColumnsFrom(0.0, 1), UnboundedRequestedEndX);

		line.ReadHeldSpan().Should().BeNull("the read cut the line back before the confirmed value");

		line.ReplaceColumns(ColumnsFrom(0.0, 2), UnboundedRequestedEndX);

		line.ReadHeldSpan().Should().Be(new HeldSpan(1.0, 4.0, Column(1.0).Center), "the rows came back");
	}

	[Fact]
	public void ConfirmLive_AfterAFold_ConfirmsTheBucketsOpeningX()
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns([Column(0.0)], UnboundedRequestedEndX);
		line.FoldIntoLastColumn(Flat(3.0, 9.0)).Should().BeTrue();
		line.SetHoldEnd(5.0);

		line.ConfirmLive(3.0);

		line.ReadHeldSpan().Should().Be(new HeldSpan(0.0, 5.0, 9.0));
	}

	[Fact]
	public void SetLineWidth_WidensTheDrawnStroke()
	{
		var thinRows = StrokeRowsAtWidth(1f);
		var thickRows = StrokeRowsAtWidth(ThickLineWidth);

		thickRows.Count.Should().BeGreaterThan(thinRows.Count);
	}

	private static IReadOnlyList<int> StrokeRowsAtWidth(float width)
	{
		using var plot = new Plot();
		var (line, dataRect) = LineOverTenByTen(
			plot,
			[Flat(0.0, 5.0), Flat(10.0, 5.0)],
			PenLineStyle.Interpolated);
		line.SetLineWidth(width);

		using var image = plot.GetImage(PlotWidth, PlotHeight);

		return StrokeProbe.RowsIn(image.GetArrayRGB(), dataRect, (int)Math.Round(dataRect.HorizontalCenter));
	}

	private static (EnvelopeLine Line, PixelRect DataRect) LineOverTenByTen(
		Plot plot,
		EnvelopeColumn[] columns,
		PenLineStyle lineStyle,
		ITickGenerator? yTickGenerator = null,
		double yMin = 0.0,
		double yMax = TenByTenSpan)
	{
		var line = new EnvelopeLine();
		line.Axes.XAxis = plot.Axes.Bottom;
		line.Axes.YAxis = plot.Axes.Left;
		if (yTickGenerator is not null)
		{
			plot.Axes.Left.TickGenerator = yTickGenerator;
		}

		plot.Add.Plottable(line);
		line.Restyle(new Color(PenColorHex), lineStyle);
		line.ReplaceColumns(columns, UnboundedRequestedEndX);
		plot.Axes.SetLimitsX(0.0, TenByTenSpan);
		plot.Axes.SetLimitsY(yMin, yMax);

		using var image = plot.GetImage(PlotWidth, PlotHeight);

		return (line, plot.RenderManager.LastRender.Layout.DataRect);
	}

	private static EnvelopeColumn Flat(double x, double y)
	{
		return new EnvelopeColumn(x, y, y, y);
	}

	private static Pixel PixelAt(EnvelopeLine line, PixelRect dataRect, double x, double y)
	{
		return new Pixel(line.Axes.XAxis.GetPixel(x, dataRect), line.Axes.YAxis.GetPixel(y, dataRect));
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

		return StrokeProbe.RowsIn(image.GetArrayRGB(), dataRect, (int)Math.Round(dataRect.HorizontalCenter));
	}

	private static EnvelopeColumn Column(double x)
	{
		return new EnvelopeColumn(x, 0.0, 1.0, 0.5);
	}

	private static EnvelopeColumn Gap(double x)
	{
		return new EnvelopeColumn(x, double.NaN, double.NaN, double.NaN);
	}

	private static EnvelopeColumn AheadGap(double x)
	{
		return Gap(x) with { Break = BreakKind.Ahead };
	}

	private static EnvelopeColumn[] ColumnsFrom(double firstX, int count)
	{
		return [.. Enumerable.Range(0, count).Select(index => Column(firstX + index))];
	}

	private static void RenderFrame(Plot plot)
	{
		using var image = plot.GetImage(PlotWidth, PlotHeight);
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
