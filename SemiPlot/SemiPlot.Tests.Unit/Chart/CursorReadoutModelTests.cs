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

		var readouts = model.ReadAt(_origin.AddHours(1), [envelope]);

		readouts[1].Should().Be(7.0);
	}

	[Fact]
	public void ReadAt_BetweenTwoFiniteColumns_LinearlyInterpolates()
	{
		var model = new CursorReadoutModel();
		var envelope = Envelope(1, (_origin, 10.0), (_origin.AddHours(2), 30.0));

		var readouts = model.ReadAt(_origin.AddHours(1), [envelope]);

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

		var readouts = model.ReadAt(_origin.AddMinutes(90), [envelope]);

		readouts[1].Should().BeNull();
	}

	[Fact]
	public void ReadAt_CursorAfterLastColumn_ReturnsNoValue()
	{
		var model = new CursorReadoutModel();
		var envelope = Envelope(1, (_origin, 10.0), (_origin.AddHours(1), 20.0));

		var readouts = model.ReadAt(_origin.AddHours(2), [envelope]);

		readouts[1].Should().BeNull();
	}

	[Fact]
	public void ReadAt_EmptyEnvelope_ReturnsNoValue()
	{
		var model = new CursorReadoutModel();
		var envelope = new PenHistoryEnvelope(1, [], [], [], []);

		var readouts = model.ReadAt(_origin, [envelope]);

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

		var readouts = model.ReadAt(_origin.AddHours(1), [inRange, outOfRange, gapped]);

		readouts.Should().HaveCount(3);
		readouts[1]!.Value.Should().BeApproximately(20.0, 1e-9);
		readouts[2].Should().BeNull();
		readouts[3].Should().BeNull();
	}
}
