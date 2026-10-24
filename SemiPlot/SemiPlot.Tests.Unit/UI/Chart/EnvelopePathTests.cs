using AwesomeAssertions;

using ScottPlot;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class EnvelopePathTests
{
	private const double HoldEndX = 5.0;

	private static readonly PixelRect _bounds = new(0, 10, 10, 0);

	// The last two rows have no overlap: the boundary column stays alone, which draws nothing the viewport can show.
	[Theory]
	[InlineData(0, 0.0, 10.0, 0, 0)]
	[InlineData(5, 1.5, 2.5, 1, 4)]
	[InlineData(5, 1.0, 3.0, 0, 5)]
	[InlineData(5, -10.0, 10.0, 0, 5)]
	[InlineData(5, -3.0, -1.0, 0, 1)]
	[InlineData(5, 9.0, 11.0, 4, 5)]
	public void VisibleRange_KeepsOneColumnBeyondEachEdgeOfTheWindow(
		int columnCount,
		double from,
		double to,
		int expectedStart,
		int expectedEnd)
	{
		EnvelopePath.VisibleRange(Lattice(columnCount), from, to).Should().Be((expectedStart, expectedEnd));
	}

	[Fact]
	public void Build_DegenerateColumn_IsOnePoint()
	{
		var points = Build([new EnvelopeColumn(0.0, 4.0, 4.0, 4.0)], PenLineStyle.Interpolated);

		points.Should().Equal(new EnvelopePoint(0.0, 4.0, true));
	}

	[Fact]
	public void Build_FirstColumn_EntersAtItsMin()
	{
		var points = Build([new EnvelopeColumn(0.0, 1.0, 2.0, 1.5)], PenLineStyle.Interpolated);

		points.Should().Equal(
			new EnvelopePoint(0.0, 1.0, true),
			new EnvelopePoint(0.0, 2.0, false));
	}

	[Fact]
	public void Build_ColumnAboveThePreviousPoint_EntersAtItsMin()
	{
		var points = Build(
			[
				new EnvelopeColumn(0.0, 0.0, 0.0, 0.0),
				new EnvelopeColumn(1.0, 5.0, 10.0, 7.0)
			],
			PenLineStyle.Interpolated);

		points.Should().Equal(
			new EnvelopePoint(0.0, 0.0, true),
			new EnvelopePoint(1.0, 5.0, false),
			new EnvelopePoint(1.0, 10.0, false));
	}

	[Fact]
	public void Build_ColumnBelowThePreviousPoint_EntersAtItsMax()
	{
		var points = Build(
			[
				new EnvelopeColumn(0.0, 10.0, 10.0, 10.0),
				new EnvelopeColumn(1.0, 2.0, 3.0, 2.5)
			],
			PenLineStyle.Interpolated);

		points.Should().Equal(
			new EnvelopePoint(0.0, 10.0, true),
			new EnvelopePoint(1.0, 3.0, false),
			new EnvelopePoint(1.0, 2.0, false));
	}

	[Fact]
	public void Build_GapColumn_StartsANewSegmentAfterIt()
	{
		var points = Build(
			[
				new EnvelopeColumn(0.0, 1.0, 2.0, 1.5),
				new EnvelopeColumn(1.0, double.NaN, double.NaN, double.NaN),
				new EnvelopeColumn(2.0, 3.0, 4.0, 3.5)
			],
			PenLineStyle.Interpolated);

		points.Should().Equal(
			new EnvelopePoint(0.0, 1.0, true),
			new EnvelopePoint(0.0, 2.0, false),
			new EnvelopePoint(2.0, 3.0, true),
			new EnvelopePoint(2.0, 4.0, false));
	}

	[Fact]
	public void Build_SteppedPen_HoldsThePreviousLevelToTheNewColumnFirst()
	{
		var points = Build(
			[
				new EnvelopeColumn(0.0, 1.0, 1.0, 1.0),
				new EnvelopeColumn(1.0, 5.0, 5.0, 5.0)
			],
			PenLineStyle.Stepped);

		points.Should().Equal(
			new EnvelopePoint(0.0, 1.0, true),
			new EnvelopePoint(1.0, 1.0, false),
			new EnvelopePoint(1.0, 5.0, false));
	}

	[Fact]
	public void Build_SteppedPen_HoldsNoLevelIntoTheFirstColumnOfASegment()
	{
		var points = Build(
			[
				new EnvelopeColumn(0.0, 1.0, 1.0, 1.0),
				new EnvelopeColumn(1.0, double.NaN, double.NaN, double.NaN),
				new EnvelopeColumn(2.0, 5.0, 5.0, 5.0)
			],
			PenLineStyle.Stepped);

		points.Should().Equal(
			new EnvelopePoint(0.0, 1.0, true),
			new EnvelopePoint(2.0, 5.0, true));
	}

	[Fact]
	public void Build_WalksOnlyTheGivenRange()
	{
		var columns = Lattice(5);
		var points = new List<EnvelopePoint>();

		EnvelopePath.Build(columns, 1, 3, PenLineStyle.Interpolated, double.NaN, double.NaN, points);

		points.Should().Equal(
			new EnvelopePoint(1.0, 1.0, true),
			new EnvelopePoint(1.0, 2.0, false),
			new EnvelopePoint(2.0, 2.0, false),
			new EnvelopePoint(2.0, 3.0, false));
	}

	[Theory]
	[InlineData(5.0, 0.0, 0.0)]
	[InlineData(5.0, 3.0, 3.0)]
	[InlineData(13.0, 0.0, 3.0)]
	[InlineData(-3.0, 4.0, 5.0)]
	public void SegmentDistance_HorizontalSegment_MeasuresToTheNearestPointOfIt(double x, double y, double expected)
	{
		var distance = EnvelopePath.SegmentDistance(new Pixel(0, 0), new Pixel(10, 0), new Pixel((float)x, (float)y));

		distance.Should().BeApproximately(expected, 1e-9);
	}

	[Fact]
	public void SegmentDistance_PointOnAVerticalSegment_IsZero()
	{
		var distance = EnvelopePath.SegmentDistance(new Pixel(2, 0), new Pixel(2, 10), new Pixel(2, 7));

		distance.Should().BeApproximately(0.0, 1e-9);
	}

	[Fact]
	public void SegmentDistance_DegenerateSegment_MeasuresToItsPoint()
	{
		var distance = EnvelopePath.SegmentDistance(new Pixel(1, 1), new Pixel(1, 1), new Pixel(4, 5));

		distance.Should().BeApproximately(5.0, 1e-9);
	}

	[Fact]
	public void ClippedSegmentDistance_ASegmentRunningPastTheBounds_MeasuresToTheInsidePartOnly()
	{
		var distance = EnvelopePath.ClippedSegmentDistance(
			new Pixel(5, -20), new Pixel(5, 30), new Pixel(5, 12), _bounds);

		distance.Should().BeApproximately(2.0, 1e-9);
	}

	[Fact]
	public void ClippedSegmentDistance_ASegmentOutsideTheBounds_IsPositiveInfinity()
	{
		var distance = EnvelopePath.ClippedSegmentDistance(
			new Pixel(0, 12), new Pixel(10, 12), new Pixel(5, 10), _bounds);

		distance.Should().Be(double.PositiveInfinity);
	}

	[Fact]
	public void ClippedSegmentDistance_ASegmentInsideTheBounds_IsItsPlainDistance()
	{
		var distance = EnvelopePath.ClippedSegmentDistance(
			new Pixel(2, 2), new Pixel(8, 2), new Pixel(5, 6), _bounds);

		distance.Should().BeApproximately(4.0, 1e-9);
	}

	[Fact]
	public void Build_AHeldDegenerateLastColumn_RunsAtItsValueToTheHoldEnd()
	{
		var points = Build([Flat(0.0, 1.0), Flat(1.0, 4.0)], PenLineStyle.Interpolated, 1.0, HoldEndX);

		points.Should().Equal(
			new EnvelopePoint(0.0, 1.0, true),
			new EnvelopePoint(1.0, 4.0, false),
			new EnvelopePoint(HoldEndX, 4.0, false));
	}

	[Fact]
	public void Build_AHeldTallLastColumn_StepsFromItsExitToItsCenterFirst()
	{
		var points = Build(
			[Flat(0.0, 0.0), new EnvelopeColumn(1.0, 5.0, 10.0, 7.0)],
			PenLineStyle.Interpolated,
			1.0,
			HoldEndX);

		points.Should().Equal(
			new EnvelopePoint(0.0, 0.0, true),
			new EnvelopePoint(1.0, 5.0, false),
			new EnvelopePoint(1.0, 10.0, false),
			new EnvelopePoint(1.0, 7.0, false),
			new EnvelopePoint(HoldEndX, 7.0, false));
	}

	[Fact]
	public void Build_AHeldSteppedPen_StepsToItsLastValueAndHoldsIt()
	{
		var points = Build([Flat(0.0, 1.0), Flat(1.0, 5.0)], PenLineStyle.Stepped, 1.0, HoldEndX);

		points.Should().Equal(
			new EnvelopePoint(0.0, 1.0, true),
			new EnvelopePoint(1.0, 1.0, false),
			new EnvelopePoint(1.0, 5.0, false),
			new EnvelopePoint(HoldEndX, 5.0, false));
	}

	[Theory]
	[InlineData(2.0, HoldEndX)]
	[InlineData(1.0, 1.0)]
	[InlineData(1.0, 0.5)]
	[InlineData(1.0, double.NaN)]
	[InlineData(double.NaN, HoldEndX)]
	public void Build_WithNoConfirmedLastColumnShortOfTheHoldEnd_HoldsNothing(double confirmedX, double holdEndX)
	{
		EnvelopeColumn[] columns = [Flat(0.0, 1.0), Flat(1.0, 4.0)];

		Build(columns, PenLineStyle.Interpolated, confirmedX, holdEndX)
			.Should().Equal(Build(columns, PenLineStyle.Interpolated));
	}

	[Fact]
	public void Build_ALineEndingOnAGap_HoldsNothing()
	{
		EnvelopeColumn[] columns = [Flat(0.0, 1.0), Gap(1.0)];

		Build(columns, PenLineStyle.Interpolated, 0.0, HoldEndX).Should().Equal(new EnvelopePoint(0.0, 1.0, true));
	}

	[Fact]
	public void Build_ARangeStoppingShortOfTheLastColumn_HoldsNothing()
	{
		EnvelopeColumn[] columns = [Flat(0.0, 1.0), Flat(1.0, 4.0)];
		var points = new List<EnvelopePoint>();

		EnvelopePath.Build(columns, 0, 1, PenLineStyle.Interpolated, 1.0, HoldEndX, points);

		points.Should().Equal(new EnvelopePoint(0.0, 1.0, true));
	}

	[Fact]
	public void Build_ALastColumnLeftOfTheWindow_IsStillHeldAcrossIt()
	{
		EnvelopeColumn[] columns = [Flat(0.0, 1.0), Flat(1.0, 4.0)];
		var (first, lastExclusive) = EnvelopePath.VisibleRange(columns, 5.0, 10.0);
		var points = new List<EnvelopePoint>();

		EnvelopePath.Build(columns, first, lastExclusive, PenLineStyle.Interpolated, 1.0, 12.0, points);

		points.Should().Equal(
			new EnvelopePoint(1.0, 4.0, true),
			new EnvelopePoint(12.0, 4.0, false));
	}

	[Fact]
	public void Hold_AConfirmedLastValue_GivesItsXTheHoldEndAndItsCenter()
	{
		EnvelopeColumn[] columns = [Flat(0.0, 1.0), new EnvelopeColumn(1.0, 5.0, 10.0, 7.0)];

		EnvelopePath.Hold(columns, 0.5, HoldEndX).Should().Be(new HeldSpan(1.0, HoldEndX, 7.0));
	}

	[Fact]
	public void AheadBreakSpans_RunFromTheLastValueBeforeToTheFirstValueAfter()
	{
		var spans = Spans([Flat(0.0, 1.0), Flat(1.0, 2.0), AheadGap(4.999), Flat(5.0, 3.0), Flat(6.0, 4.0)]);

		spans.Should().Equal(new AheadBreakSpan(1.0, 5.0));
	}

	[Fact]
	public void AheadBreakSpans_AnAnchorGapBeforeAKeptAheadBreak_StaysInOneSpan()
	{
		var spans = Spans([Flat(0.0, 1.0), Gap(1.0), AheadGap(4.999), Flat(5.0, 3.0)]);

		spans.Should().Equal(new AheadBreakSpan(0.0, 5.0));
	}

	[Fact]
	public void AheadBreakSpans_OnAnEmptyLine_OpenAtNegativeInfinity()
	{
		var spans = Spans([AheadGap(4.999), Flat(5.0, 3.0)]);

		spans.Should().Equal(new AheadBreakSpan(double.NegativeInfinity, 5.0));
	}

	[Fact]
	public void AheadBreakSpans_WithNoValueAfterTheAheadBreak_EndAtItsOwnX()
	{
		var spans = Spans([Flat(0.0, 1.0), AheadGap(4.999)]);

		spans.Should().Equal(new AheadBreakSpan(0.0, 4.999));
	}

	[Fact]
	public void AheadBreakSpans_AnOrdinaryGap_GivesNone()
	{
		Spans([Flat(0.0, 1.0), Gap(1.0), Flat(2.0, 1.0)]).Should().BeEmpty();
	}

	[Fact]
	public void AheadBreakSpans_TwoAheadBreaks_GiveOneSpanEachAndReplaceWhatTheDestinationHeld()
	{
		EnvelopeColumn[] columns =
		[
			Flat(0.0, 1.0), AheadGap(1.999), Flat(2.0, 2.0), Gap(3.0), Flat(4.0, 2.0), AheadGap(5.999), Flat(6.0, 3.0)
		];
		List<AheadBreakSpan> spans = [new AheadBreakSpan(-1.0, -1.0)];

		EnvelopePath.AheadBreakSpans(columns, 0, columns.Length, spans);

		spans.Should().Equal(new AheadBreakSpan(0.0, 2.0), new AheadBreakSpan(4.0, 6.0));
	}

	[Fact]
	public void AheadBreakSpans_AWindowLyingInsideASpan_StillYieldsIt()
	{
		EnvelopeColumn[] columns = [Flat(0.0, 1.0), AheadGap(1.0), Gap(2.0), Gap(9.0), Flat(10.0, 3.0)];
		var (first, lastExclusive) = EnvelopePath.VisibleRange(columns, 3.0, 5.0);
		var spans = new List<AheadBreakSpan>();

		EnvelopePath.AheadBreakSpans(columns, first, lastExclusive, spans);

		columns[first..lastExclusive].Should().NotContain(
			column => column.Break == BreakKind.Ahead,
			"the window meets only the ordinary gaps of the run");
		spans.Should().Equal(new AheadBreakSpan(0.0, 10.0));
	}

	private static List<AheadBreakSpan> Spans(IReadOnlyList<EnvelopeColumn> columns)
	{
		var spans = new List<AheadBreakSpan>();
		EnvelopePath.AheadBreakSpans(columns, 0, columns.Count, spans);

		return spans;
	}

	private static List<EnvelopePoint> Build(
		IReadOnlyList<EnvelopeColumn> columns,
		PenLineStyle lineStyle,
		double confirmedX = double.NaN,
		double holdEndX = double.NaN)
	{
		var points = new List<EnvelopePoint>();
		EnvelopePath.Build(columns, 0, columns.Count, lineStyle, confirmedX, holdEndX, points);

		return points;
	}

	private static EnvelopeColumn Flat(double x, double y)
	{
		return new EnvelopeColumn(x, y, y, y);
	}

	private static EnvelopeColumn Gap(double x)
	{
		return new EnvelopeColumn(x, double.NaN, double.NaN, double.NaN);
	}

	private static EnvelopeColumn AheadGap(double x)
	{
		return new EnvelopeColumn(x, double.NaN, double.NaN, double.NaN, BreakKind.Ahead);
	}

	// One column per integer X, each spanning [index, index + 1] so no column is degenerate.
	private static List<EnvelopeColumn> Lattice(int count)
	{
		var columns = new List<EnvelopeColumn>(count);
		for (var index = 0; index < count; index++)
		{
			columns.Add(new EnvelopeColumn(index, index, index + 1.0, index + 0.5));
		}

		return columns;
	}
}
