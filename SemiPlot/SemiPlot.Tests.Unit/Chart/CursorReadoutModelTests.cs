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
	public void ReadPenAt_ExactColumnHit_ReturnsThatColumnsCenter()
	{
		var envelope = Envelope(1, (_origin, 5.0), (_origin.AddHours(1), 7.0), (_origin.AddHours(2), 9.0));

		CursorReadoutModel.ReadPenAt(_origin.AddHours(1), envelope).Should().Be(7.0);
	}

	[Fact]
	public void ReadPenAt_BetweenTwoFiniteColumns_LinearlyInterpolates()
	{
		var envelope = Envelope(1, (_origin, 10.0), (_origin.AddHours(2), 30.0));

		CursorReadoutModel.ReadPenAt(_origin.AddHours(1), envelope).Should().BeApproximately(20.0, 1e-9);
	}

	[Fact]
	public void ReadPenAt_CursorInsideNaNGap_ReturnsNoValue()
	{
		var envelope = Envelope(
			1,
			(_origin, 10.0),
			(_origin.AddHours(1), double.NaN),
			(_origin.AddHours(2), 30.0));

		CursorReadoutModel.ReadPenAt(_origin.AddMinutes(90), envelope).Should().BeNull();
	}

	[Fact]
	public void ReadPenAt_CursorAfterLastColumn_ReturnsNoValue()
	{
		var envelope = Envelope(1, (_origin, 10.0), (_origin.AddHours(1), 20.0));

		CursorReadoutModel.ReadPenAt(_origin.AddHours(2), envelope).Should().BeNull();
	}

	[Fact]
	public void ReadPenAt_CursorBeforeFirstColumn_ReturnsNoValue()
	{
		var envelope = Envelope(1, (_origin.AddHours(5), 100.0), (_origin.AddHours(6), 200.0));

		CursorReadoutModel.ReadPenAt(_origin.AddHours(1), envelope).Should().BeNull();
	}

	[Fact]
	public void ReadPenAt_EmptyEnvelope_ReturnsNoValue()
	{
		var envelope = new PenHistoryEnvelope(1, [], [], [], []);

		CursorReadoutModel.ReadPenAt(_origin, envelope).Should().BeNull();
	}
}
