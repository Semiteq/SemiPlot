using AwesomeAssertions;

using SemiPlot.Core.Trends;

using Xunit;

using static SemiPlot.Tests.Unit.Chart.PenEnvelopes;

namespace SemiPlot.Tests.Unit.Chart;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class CursorReadoutModelTests
{
	private static readonly DateTime _origin = new(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void ReadAt_ExactColumnHit_ReturnsThatColumnsCenter()
	{
		var model = new CursorReadoutModel();
		var envelope = Envelope(1, (_origin, 5.0), (_origin.AddHours(1), 7.0), (_origin.AddHours(2), 9.0));

		var readouts = model.ReadAt(_origin.AddHours(1), [(envelope, PenLineStyle.Interpolated)]);

		readouts[1].Should().Be(7.0);
	}

	[Fact]
	public void ReadAt_BetweenTwoFiniteColumns_LinearlyInterpolates()
	{
		var model = new CursorReadoutModel();
		var envelope = Envelope(1, (_origin, 10.0), (_origin.AddHours(2), 30.0));

		var readouts = model.ReadAt(_origin.AddHours(1), [(envelope, PenLineStyle.Interpolated)]);

		readouts[1].Should().NotBeNull();
		readouts[1]!.Value.Should().BeApproximately(20.0, 1e-9);
	}

	[Fact]
	public void ReadAt_CursorInsideNaNGap_ReturnsNoValue()
	{
		var model = new CursorReadoutModel();
		var envelope = Envelope(
			1,
			(_origin, 10.0),
			(_origin.AddHours(1), double.NaN),
			(_origin.AddHours(2), 30.0));

		var readouts = model.ReadAt(_origin.AddMinutes(90), [(envelope, PenLineStyle.Interpolated)]);

		readouts[1].Should().BeNull();
	}

	[Fact]
	public void ReadAt_CursorAfterLastColumn_ReturnsNoValue()
	{
		var model = new CursorReadoutModel();
		var envelope = Envelope(1, (_origin, 10.0), (_origin.AddHours(1), 20.0));

		var readouts = model.ReadAt(_origin.AddHours(2), [(envelope, PenLineStyle.Interpolated)]);

		readouts[1].Should().BeNull();
	}

	[Fact]
	public void ReadAt_EmptyEnvelope_ReturnsNoValue()
	{
		var model = new CursorReadoutModel();
		var envelope = new PenHistoryEnvelope(1, [], [], [], []);

		var readouts = model.ReadAt(_origin, [(envelope, PenLineStyle.Interpolated)]);

		readouts[1].Should().BeNull();
	}

	[Fact]
	public void ReadAt_MultiplePens_MapsEachPenIndependently()
	{
		var model = new CursorReadoutModel();
		var inRange = Envelope(1, (_origin, 0.0), (_origin.AddHours(2), 40.0));
		var outOfRange = Envelope(2, (_origin.AddHours(5), 100.0), (_origin.AddHours(6), 200.0));
		var gapped = Envelope(
			3,
			(_origin, 1.0),
			(_origin.AddHours(1), double.NaN),
			(_origin.AddHours(2), 3.0));

		var readouts = model.ReadAt(
			_origin.AddHours(1),
			[
				(inRange, PenLineStyle.Interpolated),
				(outOfRange, PenLineStyle.Interpolated),
				(gapped, PenLineStyle.Stepped)
			]);

		readouts.Should().HaveCount(3);
		readouts[1]!.Value.Should().BeApproximately(20.0, 1e-9);
		readouts[2].Should().BeNull();
		readouts[3].Should().BeNull();
	}

	[Theory]
	[InlineData(PenLineStyle.Stepped, 4.0, 226.0)]
	[InlineData(PenLineStyle.Interpolated, 4.0, 209.2)]
	[InlineData(PenLineStyle.Stepped, 5.0, 205.0)]
	[InlineData(PenLineStyle.Interpolated, 5.0, 205.0)]
	[InlineData(PenLineStyle.Stepped, 0.0, 226.0)]
	[InlineData(PenLineStyle.Interpolated, 0.0, 226.0)]
	public void ReadPenAt_ReadsAsThePensLineStyleDraws(PenLineStyle lineStyle, double minutes, double expected)
	{
		var envelope = Envelope(1, (_origin, 226.0), (_origin.AddMinutes(5), 205.0));

		var reading = CursorReadoutModel.ReadPenAt(_origin.AddMinutes(minutes), envelope, lineStyle);

		reading.Should().NotBeNull();
		reading!.Value.Should().BeApproximately(expected, 1e-9);
	}

	[Theory]
	[InlineData(PenLineStyle.Stepped, -1.0)]
	[InlineData(PenLineStyle.Interpolated, -1.0)]
	[InlineData(PenLineStyle.Stepped, 6.0)]
	[InlineData(PenLineStyle.Interpolated, 6.0)]
	public void ReadPenAt_OutsideTheColumns_ReturnsNoValue(PenLineStyle lineStyle, double minutes)
	{
		var envelope = Envelope(1, (_origin, 226.0), (_origin.AddMinutes(5), 205.0));

		CursorReadoutModel.ReadPenAt(_origin.AddMinutes(minutes), envelope, lineStyle).Should().BeNull();
	}

	[Theory]
	[InlineData(PenLineStyle.Stepped)]
	[InlineData(PenLineStyle.Interpolated)]
	public void ReadPenAt_BesideAGapColumn_ReturnsNoValue(PenLineStyle lineStyle)
	{
		var envelope = Envelope(1, (_origin, 10.0), (_origin.AddHours(1), double.NaN), (_origin.AddHours(2), 30.0));

		CursorReadoutModel.ReadPenAt(_origin.AddMinutes(30), envelope, lineStyle).Should().BeNull();
		CursorReadoutModel.ReadPenAt(_origin.AddMinutes(90), envelope, lineStyle).Should().BeNull();
	}
}
