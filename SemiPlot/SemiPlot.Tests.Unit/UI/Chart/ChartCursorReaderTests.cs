using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
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
	private static readonly DateTime _start = ChartTestBuilder.From;
	private static readonly DateTime _envelopeEnd = _start.AddMinutes(1.0);
	private static readonly DateTime _holdEnd = _start.AddMinutes(10.0);

	private readonly TrendPenState _pen = new(FakeDataProvider.FirstPen, new EnvelopeLine());
	private readonly Dictionary<int, TrendPenState> _pensById = [];
	private readonly Dictionary<int, PenHistoryEnvelope> _envelopesById = [];

	public ChartCursorReaderTests()
	{
		_pensById[PenId] = _pen;
	}

	private int PenId => _pen.Pen.PenId;

	[Fact]
	public void WhereTheEnvelopeCoversTheCursor_BothReadsReturnTheEnvelopeValue()
	{
		var reader = ReaderOverAHeldEnvelope();
		var moment = _start.AddSeconds(30.0);

		reader.ReadPenAt(PenId, moment).Should().Be(4.0);
		reader.ReadAt(moment).Should().Equal(new Dictionary<int, double?> { [PenId] = 4.0 });
	}

	[Fact]
	public void InsideTheHeldSpanPastTheEnvelopesEnd_BothReadsReturnTheHeldValue()
	{
		var reader = ReaderOverAHeldEnvelope();
		var moment = _start.AddMinutes(5.0);

		reader.ReadPenAt(PenId, moment).Should().Be(6.0);
		reader.ReadAt(moment).Should().Equal(new Dictionary<int, double?> { [PenId] = 6.0 });
	}

	[Fact]
	public void PastTheHoldEnd_BothReadsReturnNull()
	{
		var reader = ReaderOverAHeldEnvelope();
		var moment = _holdEnd.AddMinutes(1.0);

		reader.ReadPenAt(PenId, moment).Should().BeNull();
		reader.ReadAt(moment).Should().Equal(new Dictionary<int, double?> { [PenId] = null });
	}

	[Fact]
	public void APenWithNoEnvelope_ReadsItsHeldValueInsideTheHeldSpan()
	{
		_pen.AppendRealtime(_start, 5.0);
		_pen.ApplyHold(_holdEnd);
		var reader = new ChartCursorReader(_pensById, _envelopesById);
		var moment = _start.AddMinutes(5.0);

		reader.ReadPenAt(PenId, moment).Should().Be(5.0);
		reader.ReadAt(moment).Should().Equal(new Dictionary<int, double?> { [PenId] = 5.0 });
	}

	[Fact]
	public void APenWithNoEnvelopeAndNoHoldAtTheCursor_IsLeftOutOfTheReadout()
	{
		_pen.AppendRealtime(_start, 5.0);
		_pen.ApplyHold(_holdEnd);
		var reader = new ChartCursorReader(_pensById, _envelopesById);

		reader.ReadAt(_holdEnd.AddMinutes(1.0)).Should().BeEmpty();
	}

	[Fact]
	public void AHiddenPen_ReadsNothingInsideTheEnvelopeOrTheHeldSpan()
	{
		var reader = ReaderOverAHeldEnvelope();
		_pen.SetVisibility(false);

		reader.ReadPenAt(PenId, _start.AddSeconds(30.0)).Should().BeNull();
		reader.ReadPenAt(PenId, _start.AddMinutes(5.0)).Should().BeNull();
		reader.ReadAt(_start.AddMinutes(5.0)).Should().BeEmpty();
	}

	private ChartCursorReader ReaderOverAHeldEnvelope()
	{
		var envelope = new PenHistoryEnvelope(PenId, [_start, _envelopeEnd], [1.0, 3.0], [5.0, 9.0], [2.0, 6.0]);
		_pen.LoadHistory(envelope, _envelopeEnd, null);
		_pen.ApplyHold(_holdEnd);
		_envelopesById[PenId] = envelope;

		return new ChartCursorReader(_pensById, _envelopesById);
	}

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
