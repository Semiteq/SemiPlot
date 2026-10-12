using AwesomeAssertions;

using SemiPlot.Core.Trends;

using Xunit;

using static SemiPlot.Tests.Unit.Chart.PenEnvelopes;

namespace SemiPlot.Tests.Unit.Chart;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class DeltaCursorModelTests
{
	private static readonly DateTime _origin = new(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void Compute_BeforeBothCursorsPlaced_ReturnsNull()
	{
		var model = new DeltaCursorModel();
		model.Place(_origin);

		var readout = model.Compute(Envelope(1, (_origin, 5.0), (_origin.AddHours(2), 9.0)));

		readout.Should().BeNull();
	}

	[Fact]
	public void Compute_TwoExactColumns_ReturnsDeltaTimeAndDeltaY()
	{
		var model = new DeltaCursorModel();
		var envelope = Envelope(1, (_origin, 5.0), (_origin.AddHours(1), 7.0), (_origin.AddHours(2), 12.0));

		model.Place(_origin);
		model.Place(_origin.AddHours(2));
		var readout = model.Compute(envelope);

		readout.Should().NotBeNull();
		readout.DeltaTime.Should().Be(TimeSpan.FromHours(2));
		readout.DeltaY.Should().NotBeNull();
		readout.DeltaY!.Value.Should().BeApproximately(7.0, 1e-9);
	}

	[Fact]
	public void Compute_DeltaTimeIsAbsolute_RegardlessOfCursorOrder()
	{
		var model = new DeltaCursorModel();
		var envelope = Envelope(1, (_origin, 5.0), (_origin.AddHours(2), 9.0));

		model.Place(_origin.AddHours(2));
		model.Place(_origin);
		var readout = model.Compute(envelope);

		readout!.DeltaTime.Should().Be(TimeSpan.FromHours(2));
		readout.DeltaY!.Value.Should().BeApproximately(-4.0, 1e-9);
	}

	[Fact]
	public void Compute_EndpointInGap_ReturnsNullDeltaYButKeepsDeltaTime()
	{
		var model = new DeltaCursorModel();
		var envelope = Envelope(
			1,
			(_origin, 10.0),
			(_origin.AddHours(1), double.NaN),
			(_origin.AddHours(2), 30.0));

		model.Place(_origin);
		model.Place(_origin.AddHours(1));
		var readout = model.Compute(envelope);

		readout!.DeltaTime.Should().Be(TimeSpan.FromHours(1));
		readout.DeltaY.Should().BeNull();
	}

	[Fact]
	public void Clear_ResetsBothCursorsAndMeasurement()
	{
		var model = new DeltaCursorModel();
		model.Place(_origin);
		model.Place(_origin.AddHours(2));

		model.Clear();

		model.FirstCursor.Should().BeNull();
		model.SecondCursor.Should().BeNull();
		model.Compute(Envelope(1, (_origin, 5.0))).Should().BeNull();
	}

	[Fact]
	public void Place_ThirdTime_StartsFreshMeasurement()
	{
		var model = new DeltaCursorModel();
		model.Place(_origin);
		model.Place(_origin.AddHours(2));

		model.Place(_origin.AddHours(3));

		model.FirstCursor.Should().Be(_origin.AddHours(3));
		model.SecondCursor.Should().BeNull();
	}
}
