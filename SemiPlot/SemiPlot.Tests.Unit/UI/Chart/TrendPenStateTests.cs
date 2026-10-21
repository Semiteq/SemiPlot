using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendPenStateTests
{
	private static readonly DateTime _start = ChartTestBuilder.From;

	[Fact]
	public void Line_ReadsTheSameBufferLoadAndAppendMutate()
	{
		var state = NewState();

		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));

		// Reading through the plottable rather than the backing field proves it renders the buffer the
		// state mutates: the limits are taken from the very list LoadHistory filled.
		state.Line.GetAxisLimits().Top.Should().Be(9.0);

		// Past the end of the loaded range, so the point is appended rather than folded into it.
		state.AppendRealtime(_start.AddHours(3.0), 12.0);

		state.Line.GetAxisLimits().Top.Should().Be(12.0);
	}

	[Fact]
	public void History_WithInteriorGap_PlacesNaNBetweenTwoSegments()
	{
		var state = NewState();
		var envelope = new PenHistoryEnvelope(
			1,
			[_start, _start.AddMinutes(1.0), _start.AddMinutes(2.0)],
			[1.0, double.NaN, 3.0],
			[1.0, double.NaN, 3.0],
			[1.0, double.NaN, 3.0]);

		state.LoadHistory(envelope, _start.AddMinutes(2.0));

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(3);
		double.IsNaN(columns[0].Center).Should().BeFalse();
		double.IsNaN(columns[1].Center).Should().BeTrue();
		double.IsNaN(columns[2].Center).Should().BeFalse();
	}

	[Fact]
	public void Realtime_NullSample_AppendsNaNGap()
	{
		var state = NewState();

		state.AppendRealtime(_start, value: null);

		var columns = state.Line.SnapshotColumns();
		columns.Should().ContainSingle();
		double.IsNaN(columns[0].Center).Should().BeTrue();
	}

	[Fact]
	public void Realtime_BreakOneTickAfterTheValue_IsRejectedAndOneMillisecondAfterIt_AddsANaNColumn()
	{
		var state = NewState();
		state.AppendRealtime(_start, 5.0);

		state.AppendRealtime(_start.AddTicks(1), value: null);

		state.Line.SnapshotColumns().Should().HaveCount(2, "the axis keeps milliseconds only");

		state.AppendRealtime(_start.AddMilliseconds(1.0), value: null);

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(3);
		columns[1].Center.Should().Be(5.0);
		double.IsNaN(columns[2].Center).Should().BeTrue();
		state.CurrentValue.Should().Be(5.0);
	}

	[Fact]
	public void History_LoadsColumnsCarryingMinAndMax()
	{
		var state = NewState();

		state.LoadHistory(TwoColumnEnvelope(), _start.AddMinutes(1.0));

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(2);
		columns[0].Min.Should().Be(1.0);
		columns[0].Max.Should().Be(5.0);
		columns[1].Min.Should().Be(3.0);
		columns[1].Max.Should().Be(9.0);
	}

	[Fact]
	public void Realtime_LiveEdgeColumnDegeneratesToMinEqualsMaxEqualsValue()
	{
		var state = NewState();

		state.AppendRealtime(_start, 42.0);

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(2, "the first value opens behind its break");
		columns[^1].Min.Should().Be(42.0);
		columns[^1].Max.Should().Be(42.0);
		columns[^1].Center.Should().Be(42.0);
	}

	[Theory]
	[InlineData(1.0, 2, 6.0)]
	[InlineData(0.0, 2, 6.0)]
	[InlineData(2.0, 4, 42.0)]
	public void Realtime_SampleAfterTheLastPoint_IsAppendedAndOneAtOrBeforeItIsIgnored(
		double minutesFromStart,
		int expectedColumns,
		double expectedCurrentValue)
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));

		state.AppendRealtime(_start.AddMinutes(minutesFromStart), 42.0);

		state.Line.SnapshotColumns().Should().HaveCount(expectedColumns);
		state.Line.SnapshotColumns()[^1].Center.Should().Be(expectedCurrentValue);
		state.CurrentValue.Should().Be(expectedCurrentValue);
	}

	[Fact]
	public void Realtime_AfterAHistoryLoadEndingBeforeALiveColumn_KeepsItAndRejectsAppendsAtOrBeforeIt()
	{
		var state = NewState();
		state.AppendRealtime(_start.AddMinutes(10.0), 7.0);

		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));
		AppendAtOrBeforeTheLiveColumn(state);

		state.Line.SnapshotColumns().Should().HaveCount(4);
		state.Line.SnapshotColumns()[^1].Center.Should().Be(7.0);
		state.CurrentValue.Should().Be(7.0);
	}

	[Fact]
	public void Realtime_AfterAClearedHistoryEndingBeforeALiveColumn_KeepsItAndRejectsAppendsAtOrBeforeIt()
	{
		var state = NewState();
		state.AppendRealtime(_start.AddMinutes(10.0), 7.0);

		state.ClearHistory(_start.AddHours(1.0));
		AppendAtOrBeforeTheLiveColumn(state);

		state.Line.SnapshotColumns().Should().HaveCount(2);
		state.Line.SnapshotColumns()[^1].Center.Should().Be(7.0);
		state.CurrentValue.Should().Be(7.0);
	}

	[Fact]
	public void Realtime_AFirstValuePastTheLastColumn_OpensOneMillisecondBehindANaNColumn()
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));

		state.AppendRealtime(_start.AddMinutes(2.0), 42.0);
		state.AppendRealtime(_start.AddMinutes(3.0), 43.0);

		var columns = state.Line.SnapshotColumns();
		columns.Select(column => column.X).Should().Equal(
			XOf(_start),
			XOf(_start.AddMinutes(1.0)),
			XOf(_start.AddMinutes(2.0).AddMilliseconds(-1.0)),
			XOf(_start.AddMinutes(2.0)),
			XOf(_start.AddMinutes(3.0)));
		double.IsNaN(columns[2].Center).Should().BeTrue();
		columns.Count(column => double.IsNaN(column.Center)).Should().Be(1, "only the first value opens a break");
		state.CurrentValue.Should().Be(43.0);
	}

	[Fact]
	public void Realtime_AFirstValueAtTheLastColumn_IsRejectedAndTheNextValuePastItAppendsWithNoBreak()
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));

		state.AppendRealtime(_start.AddMinutes(1.0), 42.0);
		state.AppendRealtime(_start.AddMinutes(2.0), 43.0);

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(3);
		columns.Should().NotContain(column => double.IsNaN(column.Center));
		state.CurrentValue.Should().Be(43.0);
	}

	[Fact]
	public void Realtime_OnAnEmptyLine_TheFirstValueOpensBehindANaNColumnAndALaterHistoryLoadKeepsBoth()
	{
		var state = NewState();

		state.AppendRealtime(_start.AddMinutes(10.0), 7.0);
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));

		var columns = state.Line.SnapshotColumns();
		columns.Select(column => column.X).Should().Equal(
			XOf(_start),
			XOf(_start.AddMinutes(1.0)),
			XOf(_start.AddMinutes(10.0).AddMilliseconds(-1.0)),
			XOf(_start.AddMinutes(10.0)));
		double.IsNaN(columns[2].Center).Should().BeTrue();
		columns[3].Center.Should().Be(7.0);
	}

	[Fact]
	public void Realtime_AFirstValueOnALineEndingOnAGap_TakesNoSecondNaNColumn()
	{
		var state = NewState();
		state.LoadHistory(
			new PenHistoryEnvelope(1, [_start, _start.AddMinutes(1.0)], [1.0, double.NaN], [1.0, double.NaN],
				[1.0, double.NaN]),
			_start.AddHours(1.0));

		state.AppendRealtime(_start.AddMinutes(2.0), 42.0);

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(3);
		columns.Count(column => double.IsNaN(column.Center)).Should().Be(1);
		columns[^1].Center.Should().Be(42.0);
	}

	[Fact]
	public void Realtime_AFirstValueOneMillisecondPastTheLastColumn_TakesNoBreak()
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));

		state.AppendRealtime(_start.AddMinutes(1.0).AddMilliseconds(1.0), 42.0);

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(3);
		columns.Should().NotContain(column => double.IsNaN(column.Center));
	}

	[Fact]
	public void Realtime_ARejectedBreak_StillOpensTheNextValuePastTheLastColumnBehindABreak()
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));
		state.AppendRealtime(_start.AddMinutes(1.0), 6.0);

		state.AppendRealtime(_start.AddSeconds(30.0), value: null);
		state.Line.SnapshotColumns().Should().HaveCount(2, "the break lies inside the history");

		state.AppendRealtime(_start.AddMinutes(2.0), 42.0);

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(4);
		double.IsNaN(columns[2].Center).Should().BeTrue();
		columns[2].X.Should().Be(XOf(_start.AddMinutes(2.0).AddMilliseconds(-1.0)));
	}

	[Theory]
	[InlineData(2.5, 2)]
	[InlineData(60.0, 1)]
	public void Realtime_AfterAHistoryApply_TheNextValueOpensBehindABreakOnlyWhenTheApplyDroppedALiveColumn(
		double requestedEndMinutes,
		int expectedGaps)
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));
		state.AppendRealtime(_start.AddMinutes(2.0), 42.0);
		state.AppendRealtime(_start.AddMinutes(3.0), 43.0);

		state.LoadHistory(TwoColumnEnvelope(), _start.AddMinutes(requestedEndMinutes));
		state.AppendRealtime(_start.AddMinutes(4.0), 44.0);

		var columns = state.Line.SnapshotColumns();
		columns.Count(column => double.IsNaN(column.Center)).Should().Be(expectedGaps);
		columns[^1].Center.Should().Be(44.0);
		state.CurrentValue.Should().Be(44.0);
	}

	[Fact]
	public void FoldRealtime_AFirstValueOpensAColumnBehindTheBreakAndTheNextOneWidensIt()
	{
		var state = NewState();
		state.LoadHistory(new PenHistoryEnvelope(1, [_start], [1.0], [5.0], [2.0]), _start);

		state.FoldRealtime(_start.AddMinutes(1.0), 9.0);
		state.FoldRealtime(_start.AddMinutes(2.0), 4.0);

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(3);
		double.IsNaN(columns[1].Center).Should().BeTrue();
		columns[1].X.Should().Be(XOf(_start.AddMinutes(1.0).AddMilliseconds(-1.0)));
		columns[2].Should().Be(new EnvelopeColumn(XOf(_start.AddMinutes(1.0)), 4.0, 9.0, 4.0));
		state.CurrentValue.Should().Be(4.0);
	}

	[Fact]
	public void FoldRealtime_AFirstValueAtTheLastColumn_WidensItAndTheNextOneFoldsWithNoBreak()
	{
		var state = NewState();
		state.LoadHistory(new PenHistoryEnvelope(1, [_start], [1.0], [5.0], [2.0]), _start);

		state.FoldRealtime(_start, 9.0);
		state.FoldRealtime(_start.AddMinutes(1.0), 3.0);

		var columns = state.Line.SnapshotColumns();
		columns.Should().ContainSingle();
		columns[0].Min.Should().Be(1.0);
		columns[0].Max.Should().Be(9.0);
		columns[0].Center.Should().Be(3.0);
		state.CurrentValue.Should().Be(3.0);
	}

	private static void AppendAtOrBeforeTheLiveColumn(TrendPenState state)
	{
		state.AppendRealtime(_start.AddMinutes(1.5), 42.0);
		state.AppendRealtime(_start, 42.0);
		state.AppendRealtime(_start.AddMinutes(10.0), 43.0);
	}

	private static double XOf(DateTime utc)
	{
		return LocalTimeAxis.ToAxis(utc);
	}

	private static TrendPenState NewState()
	{
		return new TrendPenState(FakeDataProvider.FirstPen, new EnvelopeLine());
	}

	private static PenHistoryEnvelope TwoColumnEnvelope()
	{
		return new PenHistoryEnvelope(1, [_start, _start.AddMinutes(1.0)], [1.0, 3.0], [5.0, 9.0], [2.0, 6.0]);
	}
}
