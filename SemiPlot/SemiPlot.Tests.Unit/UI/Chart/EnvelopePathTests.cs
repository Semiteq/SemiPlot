using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class EnvelopePathTests
{
	[Fact]
	public void VisibleRange_EmptyColumns_IsEmpty()
	{
		EnvelopePath.VisibleRange([], 0.0, 10.0).Should().Be((0, 0));
	}

	[Fact]
	public void VisibleRange_WindowInsideTheData_KeepsOneColumnBeyondEachEdge()
	{
		EnvelopePath.VisibleRange(Lattice(5), 1.5, 2.5).Should().Be((1, 4));
	}

	[Fact]
	public void VisibleRange_WindowLandingOnColumns_StillKeepsOneColumnBeyondEachEdge()
	{
		EnvelopePath.VisibleRange(Lattice(5), 1.0, 3.0).Should().Be((0, 5));
	}

	[Fact]
	public void VisibleRange_WindowWiderThanTheData_IsEveryColumn()
	{
		EnvelopePath.VisibleRange(Lattice(5), -10.0, 10.0).Should().Be((0, 5));
	}

	// No overlap leaves the boundary column alone, which draws nothing the viewport can show.
	[Fact]
	public void VisibleRange_WindowEntirelyBeforeTheData_IsTheFirstColumnOnly()
	{
		EnvelopePath.VisibleRange(Lattice(5), -3.0, -1.0).Should().Be((0, 1));
	}

	[Fact]
	public void VisibleRange_WindowEntirelyAfterTheData_IsTheLastColumnOnly()
	{
		EnvelopePath.VisibleRange(Lattice(5), 9.0, 11.0).Should().Be((4, 5));
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

		EnvelopePath.Build(columns, 1, 3, PenLineStyle.Interpolated, points);

		points.Should().Equal(
			new EnvelopePoint(1.0, 1.0, true),
			new EnvelopePoint(1.0, 2.0, false),
			new EnvelopePoint(2.0, 2.0, false),
			new EnvelopePoint(2.0, 3.0, false));
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
