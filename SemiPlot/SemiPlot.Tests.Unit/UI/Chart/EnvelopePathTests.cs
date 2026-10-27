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
	public void Build_FirstColumn_EntersAtItsMinAndEndsOnItsCenter()
	{
		var points = Build([new EnvelopeColumn(0.0, 1.0, 2.0, 1.5)], PenLineStyle.Interpolated);

		points.Should().Equal(
			new EnvelopePoint(0.0, 1.0, true),
			new EnvelopePoint(0.0, 2.0, false),
			new EnvelopePoint(0.0, 1.5, false));
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
			new EnvelopePoint(1.0, 10.0, false),
			new EnvelopePoint(1.0, 7.0, false));
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
			new EnvelopePoint(1.0, 2.0, false),
			new EnvelopePoint(1.0, 2.5, false));
	}

	[Fact]
	public void Build_ColumnEndingOnItsFarExtreme_AddsNoVertexForItsCenter()
	{
		var points = Build(
			[
				new EnvelopeColumn(0.0, 0.0, 0.0, 0.0),
				new EnvelopeColumn(1.0, 5.0, 10.0, 10.0)
			],
			PenLineStyle.Interpolated);

		points.Should().Equal(
			new EnvelopePoint(0.0, 0.0, true),
			new EnvelopePoint(1.0, 5.0, false),
			new EnvelopePoint(1.0, 10.0, false));
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
			new EnvelopePoint(0.0, 1.5, false),
			new EnvelopePoint(2.0, 3.0, true),
			new EnvelopePoint(2.0, 4.0, false),
			new EnvelopePoint(2.0, 3.5, false));
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
	public void Build_SteppedPen_AfterAMinMaxColumn_HoldsItsCenter()
	{
		var points = Build(
			[
				new EnvelopeColumn(0.0, 9.0, 9.0, 9.0),
				new EnvelopeColumn(1.0, 6.0, 9.0, 6.0),
				new EnvelopeColumn(2.0, 4.0, 4.0, 4.0)
			],
			PenLineStyle.Stepped);

		points.Should().Equal(
			new EnvelopePoint(0.0, 9.0, true),
			new EnvelopePoint(1.0, 9.0, false),
			new EnvelopePoint(1.0, 9.0, false),
			new EnvelopePoint(1.0, 6.0, false),
			new EnvelopePoint(2.0, 6.0, false),
			new EnvelopePoint(2.0, 4.0, false));
	}

	[Fact]
	public void Build_WalksOnlyTheGivenRange()
	{
		var columns = Lattice(5);
		var points = new List<EnvelopePoint>();

		EnvelopePath.Build(columns, 1, 3, PenLineStyle.Interpolated, points);

		points.Should().Equal(
			new EnvelopePoint(1.0, 1.0, true),
			new EnvelopePoint(1.0, 2.0, false),
			new EnvelopePoint(1.0, 1.5, false),
			new EnvelopePoint(2.0, 2.0, false),
			new EnvelopePoint(2.0, 3.0, false),
			new EnvelopePoint(2.0, 2.5, false));
	}

	[Theory]
	[InlineData(PenLineStyle.Interpolated, 1)]
	[InlineData(PenLineStyle.Interpolated, 2)]
	[InlineData(PenLineStyle.Interpolated, 4)]
	[InlineData(PenLineStyle.Stepped, 1)]
	[InlineData(PenLineStyle.Stepped, 2)]
	[InlineData(PenLineStyle.Stepped, 4)]
	public void Build_FromALaterFirstColumn_DrawsTheSharedColumnsAsTheWholeRangeDoes(
		PenLineStyle lineStyle,
		int first)
	{
		EnvelopeColumn[] columns =
		[
			new(0.0, 10.0, 10.0, 10.0),
			new(1.0, 2.0, 10.0, 2.0),
			new(2.0, 2.0, 2.0, 2.0),
			new(3.0, 1.0, 2.0, 1.0),
			new(4.0, 1.0, 8.0, 3.0),
			new(5.0, 3.0, 3.0, 3.0)
		];
		var fromLater = new List<EnvelopePoint>();
		EnvelopePath.Build(columns, first, columns.Length, lineStyle, fromLater);

		var sharedFromWhole = Build(columns, lineStyle)
			.Where(point => point.X >= columns[first].X)
			.Select(point => (point.X, point.Y));

		fromLater.Select(point => (point.X, point.Y)).Should().Equal(sharedFromWhole);
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

	private static List<EnvelopePoint> Build(IReadOnlyList<EnvelopeColumn> columns, PenLineStyle lineStyle)
	{
		var points = new List<EnvelopePoint>();
		EnvelopePath.Build(columns, 0, columns.Count, lineStyle, points);

		return points;
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
