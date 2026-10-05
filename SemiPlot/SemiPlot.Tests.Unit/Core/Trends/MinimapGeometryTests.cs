using AwesomeAssertions;

using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Trends;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class MinimapGeometryTests
{
	private const double StripWidth = 900.0;
	private const double MinimumMarkerWidth = 6.0;
	private const double BandWidth = 400.0;
	private const double BandHeight = 30.0;
	private const double LabelWidth = 60.0;
	private const double CoverDistance = 6.0;
	private static readonly DateTime _first = new(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _last = new(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void WindowFraction_MidWindow_MapsToCenteredRectangle()
	{
		var from = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc);
		var to = new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc);

		var (start, width) = MinimapGeometry.WindowFraction(_first, _last, from, to);

		start.Should().BeApproximately(0.2, 1e-9);
		width.Should().BeApproximately(0.4, 1e-9);
	}

	[Fact]
	public void WindowFraction_WindowEqualsExtent_FillsStrip()
	{
		var (start, width) = MinimapGeometry.WindowFraction(_first, _last, _first, _last);

		start.Should().Be(0.0);
		width.Should().BeApproximately(1.0, 1e-9);
	}

	[Fact]
	public void WindowFraction_WindowReachesBeyondExtent_ClampsToStrip()
	{
		var from = _first - TimeSpan.FromDays(5.0);
		var to = _last + TimeSpan.FromDays(5.0);

		var (start, width) = MinimapGeometry.WindowFraction(_first, _last, from, to);

		start.Should().Be(0.0);
		width.Should().Be(1.0);
	}

	[Fact]
	public void WindowFraction_ZeroSpanExtent_ReturnsFullStrip()
	{
		var (start, width) = MinimapGeometry.WindowFraction(_first, _first, _first, _first);

		start.Should().Be(0.0);
		width.Should().Be(1.0);
	}

	[Theory]
	[InlineData(1.0)]
	[InlineData(0.999)]
	public void MarkerSpan_AtTheRightEnd_StaysInsideTheStrip(double startFraction)
	{
		var (left, width) = MinimapGeometry.MarkerSpan(startFraction, 0.0, StripWidth, MinimumMarkerWidth);

		width.Should().Be(MinimumMarkerWidth);
		(left + width).Should().BeLessThanOrEqualTo(StripWidth);
	}

	[Fact]
	public void MarkerSpan_WindowWiderThanTheStrip_FillsItExactly()
	{
		var (left, width) = MinimapGeometry.MarkerSpan(-0.5, 2.0, StripWidth, MinimumMarkerWidth);

		left.Should().Be(0.0);
		width.Should().Be(StripWidth);
	}

	[Fact]
	public void MarkerSpan_ZeroStripWidth_IsEmpty()
	{
		var span = MinimapGeometry.MarkerSpan(0.5, 0.1, 0.0, MinimumMarkerWidth);

		span.Should().Be((0.0, 0.0));
	}

	[Fact]
	public void MarkerSpan_WindowAtTheLeftEdge_KeepsLeftAtZero()
	{
		var (left, width) = MinimapGeometry.MarkerSpan(0.0, 0.001, StripWidth, MinimumMarkerWidth);

		left.Should().Be(0.0);
		width.Should().Be(MinimumMarkerWidth);
	}

	[Theory]
	[InlineData(100.0, 100.0)]
	[InlineData(-5.0, 0.0)]
	[InlineData(899.5, StripWidth - 1.0)]
	public void SpanLeftWithin_KeepsAOnePixelSpanInsideTheLimit(double left, double expected)
	{
		MinimapGeometry.SpanLeftWithin(left, 1.0, StripWidth).Should().Be(expected);
	}

	[Fact]
	public void SpanLeftWithin_ASpanWiderThanTheLimit_StartsAtItsLeftEdge()
	{
		MinimapGeometry.SpanLeftWithin(10.0, StripWidth + 50.0, StripWidth).Should().Be(0.0);
	}

	[Fact]
	public void PlaceHoverTime_InTheMiddle_CentresOnThePointerAndCoversNeitherEndLabel()
	{
		var placement = MinimapGeometry.PlaceHoverTime(
			450.0, LabelWidth, StripWidth, LabelWidth, LabelWidth, CoverDistance);

		placement.Should().Be(
			new HoverTimePlacement(450.0 - (LabelWidth / 2.0), CoversFirst: false, CoversLast: false));
	}

	[Theory]
	[InlineData(0.0, 0.0)]
	[InlineData(StripWidth, StripWidth - LabelWidth)]
	public void PlaceHoverTime_AtAnEnd_StaysInTheRowAndCoversThatEndLabelOnly(double pointerX, double expectedLeft)
	{
		var placement = MinimapGeometry.PlaceHoverTime(
			pointerX, LabelWidth, StripWidth, LabelWidth, LabelWidth, CoverDistance);

		var atLeftEnd = pointerX == 0.0;
		placement.Should().Be(new HoverTimePlacement(expectedLeft, CoversFirst: atLeftEnd, CoversLast: !atLeftEnd));
	}

	[Theory]
	[InlineData(CoverDistance - 3.0, true)]
	[InlineData(CoverDistance + 1.0, false)]
	public void PlaceHoverTime_ATimeWithinTheCoverDistanceOfAnEndLabel_CoversIt(double distance, bool covers)
	{
		var nearFirst = LabelWidth + distance + (LabelWidth / 2.0);
		var nearLast = StripWidth - LabelWidth - distance - (LabelWidth / 2.0);

		MinimapGeometry.PlaceHoverTime(nearFirst, LabelWidth, StripWidth, LabelWidth, LabelWidth, CoverDistance)
			.CoversFirst.Should().Be(covers);
		MinimapGeometry.PlaceHoverTime(nearLast, LabelWidth, StripWidth, LabelWidth, LabelWidth, CoverDistance)
			.CoversLast.Should().Be(covers);
	}

	[Fact]
	public void RightBound_IsTheLaterOfTheExtentAndTheNewestSample()
	{
		var newer = _last + TimeSpan.FromMinutes(5.0);
		var older = _last - TimeSpan.FromMinutes(5.0);

		MinimapGeometry.RightBound(_last, newer).Should().Be(newer);
		MinimapGeometry.RightBound(_last, older).Should().Be(_last);
		MinimapGeometry.RightBound(_last, null).Should().Be(_last);
	}

	[Fact]
	public void ABandWithABreak_DrawsTwoFigures()
	{
		var band = Envelope(
			(0.0, 1.0, 3.0, 2.0),
			(1.0, 2.0, 4.0, 3.0),
			(2.0, double.NaN, double.NaN, double.NaN),
			(3.0, 0.0, 2.0, 1.0),
			(4.0, 1.0, 5.0, 3.0));

		var figures = MinimapGeometry.BandFigures(band, _first, _first.AddDays(4.0), BandWidth, BandHeight);

		// The drawn range is [0, 5] over 30 px, so a value v sits at 6 * (5 - v).
		figures.Should().HaveCount(2);
		figures[0].Outline.Should().Equal(
			new BandPoint(0.0, 12.0),
			new BandPoint(100.0, 6.0),
			new BandPoint(100.0, 18.0),
			new BandPoint(0.0, 24.0));
		figures[0].CenterLine.Should().Equal(new BandPoint(0.0, 18.0), new BandPoint(100.0, 12.0));
		figures[1].Outline.Should().Equal(
			new BandPoint(300.0, 18.0),
			new BandPoint(400.0, 0.0),
			new BandPoint(400.0, 24.0),
			new BandPoint(300.0, BandHeight));
		figures[1].CenterLine.Should().Equal(new BandPoint(300.0, 24.0), new BandPoint(400.0, 12.0));
	}

	[Fact]
	public void ALoneColumnBetweenBreaks_DrawsAFigureTwoPixelsWide()
	{
		var band = Envelope(
			(0.0, 1.0, 3.0, 2.0),
			(1.0, 2.0, 4.0, 3.0),
			(2.0, double.NaN, double.NaN, double.NaN),
			(3.0, 2.0, 4.0, 3.0),
			(4.0, double.NaN, double.NaN, double.NaN));

		var figures = MinimapGeometry.BandFigures(band, _first, _first.AddDays(4.0), BandWidth, BandHeight);

		figures.Should().HaveCount(2);
		figures[1].Outline.Select(point => point.X).Should().Equal(299.0, 301.0, 301.0, 299.0);
		figures[1].CenterLine.Select(point => point.X).Should().Equal(299.0, 301.0);
	}

	[Fact]
	public void AnInfiniteColumn_IsABreakAndEveryPointStaysFinite()
	{
		var band = Envelope(
			(0.0, 0.0, 2.0, 1.0),
			(1.0, 1.0, 5.0, 3.0),
			(2.0, double.NegativeInfinity, double.PositiveInfinity, 0.0),
			(3.0, 1.0, 3.0, 2.0),
			(4.0, 0.0, 5.0, 2.0));

		var figures = MinimapGeometry.BandFigures(band, _first, _first.AddDays(4.0), BandWidth, BandHeight);

		figures.Should().HaveCount(2);
		figures.SelectMany(figure => figure.Outline.Concat(figure.CenterLine))
			.Should().OnlyContain(point => double.IsFinite(point.Y));
	}

	[Fact]
	public void AColumnWithNoCentre_IsLeftOutOfTheRange()
	{
		var band = Envelope(
			(0.0, 0.0, 5.0, 2.0),
			(1.0, 1.0, 4.0, 2.0),
			(2.0, -100.0, 100.0, double.NaN),
			(3.0, 1.0, 4.0, 2.0),
			(4.0, 0.0, 5.0, 3.0));

		var figures = MinimapGeometry.BandFigures(band, _first, _first.AddDays(4.0), BandWidth, BandHeight);

		var outlineYs = figures.SelectMany(figure => figure.Outline).Select(point => point.Y).ToArray();
		outlineYs.Min().Should().Be(0.0, "the drawn maximum 5 maps to the top");
		outlineYs.Max().Should().Be(BandHeight, "the drawn minimum 0 maps to the bottom");
	}

	[Fact]
	public void AFlatPen_DrawsAMidLineBand()
	{
		var band = Envelope((0.0, 5.0, 5.0, 5.0), (1.0, 5.0, 5.0, 5.0));

		var figures = MinimapGeometry.BandFigures(band, _first, _first.AddDays(1.0), BandWidth, BandHeight);

		figures.Should().ContainSingle();
		figures[0].Outline.Should().OnlyContain(point => point.Y == BandHeight / 2.0);
		figures[0].CenterLine.Should().OnlyContain(point => point.Y == BandHeight / 2.0);
	}

	[Fact]
	public void AnAllNaNBand_DrawsNothing()
	{
		var band = Envelope(
			(0.0, double.NaN, double.NaN, double.NaN),
			(1.0, double.NaN, double.NaN, double.NaN));

		var figures = MinimapGeometry.BandFigures(band, _first, _first.AddDays(1.0), BandWidth, BandHeight);

		figures.Should().BeEmpty();
	}

	[Fact]
	public void BandFigures_DropColumnsOutsideTheBoundsAndMapTheMaxToTheTop()
	{
		var band = Envelope(
			(-1.0, -100.0, 100.0, 0.0),
			(0.0, 0.0, 10.0, 5.0),
			(1.0, 2.0, 8.0, 5.0),
			(2.0, -100.0, 100.0, 0.0));

		var figures = MinimapGeometry.BandFigures(band, _first, _first.AddDays(1.0), BandWidth, BandHeight);

		figures.Should().ContainSingle();
		figures[0].Outline.Should().Equal(
			new BandPoint(0.0, 0.0),
			new BandPoint(BandWidth, 6.0),
			new BandPoint(BandWidth, 24.0),
			new BandPoint(0.0, BandHeight));
		figures[0].CenterLine.Should().Equal(
			new BandPoint(0.0, BandHeight / 2.0),
			new BandPoint(BandWidth, BandHeight / 2.0));
	}

	[Fact]
	public void TimeAtFraction_Half_ReturnsExtentMidpoint()
	{
		var time = MinimapGeometry.TimeAtFraction(_first, _last, 0.5);

		time.Should().Be(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc));
	}

	[Theory]
	[InlineData(-0.5)]
	[InlineData(1.5)]
	public void TimeAtFraction_OutOfRange_ClampsToExtentEdge(double fraction)
	{
		var time = MinimapGeometry.TimeAtFraction(_first, _last, fraction);

		time.Should().BeOnOrAfter(_first);
		time.Should().BeOnOrBefore(_last);
	}

	private static PenHistoryEnvelope Envelope(params (double Days, double Min, double Max, double Center)[] columns)
	{
		return new PenHistoryEnvelope(
			1,
			[.. columns.Select(column => _first.AddDays(column.Days))],
			[.. columns.Select(column => column.Min)],
			[.. columns.Select(column => column.Max)],
			[.. columns.Select(column => column.Center)]);
	}
}
