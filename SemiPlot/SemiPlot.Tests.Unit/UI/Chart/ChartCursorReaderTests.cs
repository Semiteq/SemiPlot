using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

using static SemiPlot.Tests.Unit.Chart.PenEnvelopes;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartCursorReaderTests
{
	private const int SteppedPenId = 1;
	private const int InterpolatedPenId = 2;

	private static readonly DateTime _origin = new(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _cursor = _origin.AddMinutes(4);

	[Fact]
	public void ReadAt_ReadsEachVisiblePenAsItsLineStyleDraws()
	{
		var reader = ReaderOverTwoPens();

		var readings = reader.ReadAt(_cursor);

		readings[SteppedPenId].Should().Be(226.0);
		readings[InterpolatedPenId]!.Value.Should().BeApproximately(209.2, 1e-9);
	}

	[Fact]
	public void ReadPenAt_ASteppedPen_ReadsTheHeldValue()
	{
		var reader = ReaderOverTwoPens();

		reader.ReadPenAt(SteppedPenId, _cursor).Should().Be(226.0);
		reader.ReadPenAt(InterpolatedPenId, _cursor)!.Value.Should().BeApproximately(209.2, 1e-9);
	}

	private static ChartCursorReader ReaderOverTwoPens()
	{
		var pensById = new Dictionary<int, TrendPenState>
		{
			[SteppedPenId] = PenState(SteppedPenId, PenLineStyle.Stepped),
			[InterpolatedPenId] = PenState(InterpolatedPenId, PenLineStyle.Interpolated)
		};
		var envelopesById = new Dictionary<int, PenHistoryEnvelope>
		{
			[SteppedPenId] = StepFrom226To205(SteppedPenId),
			[InterpolatedPenId] = StepFrom226To205(InterpolatedPenId)
		};

		return new ChartCursorReader(pensById, envelopesById);
	}

	private static TrendPenState PenState(int penId, PenLineStyle lineStyle)
	{
		return new TrendPenState(
			new Pen(penId, $"Pen {penId}", ["Group A"], "#ff0000", LineStyle: lineStyle),
			new EnvelopeLine());
	}

	private static PenHistoryEnvelope StepFrom226To205(int penId)
	{
		return Envelope(penId, (_origin, 226.0), (_origin.AddMinutes(5), 205.0));
	}
}
