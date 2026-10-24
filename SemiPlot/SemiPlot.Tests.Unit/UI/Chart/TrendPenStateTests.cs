using AwesomeAssertions;

using ScottPlot;

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
	private static readonly DateTime _holdEdge = _start.AddMinutes(10.0);

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
		columns[0].Break.Should().Be(BreakKind.Ordinary, "the source wrote it");
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
		columns[2].Break.Should().Be(BreakKind.Ordinary);
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
		columns[2].Break.Should().Be(BreakKind.Ahead);
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
		columns[2].Break.Should().Be(BreakKind.Ahead, "the kept tail carries the kind");
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
		columns[1].Break.Should().Be(
			BreakKind.Ordinary,
			"the history's own gap stands, so the span after it is not shaded as an ahead-break");
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
		columns[2].Break.Should().Be(BreakKind.Ordinary, "the gap stands in for the rejected source break");
	}

	[Fact]
	public void Realtime_ARejectedReplayAfterARejectedBreak_StillOpensTheNextValueBehindAnOrdinaryBreak()
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));
		state.AppendRealtime(_start.AddMinutes(1.0), 6.0);
		state.AppendRealtime(_start.AddSeconds(30.0), value: null);

		state.AppendRealtime(_start.AddMinutes(1.0), 6.0);
		state.Line.SnapshotColumns().Should().HaveCount(2, "the replay lies at the last column");

		state.AppendRealtime(_start.AddMinutes(2.0), 42.0);

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(4, "no line crosses the outage");
		columns[2].Should().Be(new EnvelopeColumn(
			XOf(_start.AddMinutes(2.0).AddMilliseconds(-1.0)),
			double.NaN,
			double.NaN,
			double.NaN,
			BreakKind.Ordinary));
		columns[3].Center.Should().Be(42.0);
		state.CurrentValue.Should().Be(42.0);
	}

	[Fact]
	public void Realtime_ASourceBreakThenADroppingHistoryApply_OpensTheNextValueBehindAnAheadBreak()
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));
		state.AppendRealtime(_start.AddMinutes(2.0), 42.0);
		state.AppendRealtime(_start.AddMinutes(3.0), value: null);

		state.LoadHistory(TwoColumnEnvelope(), _start.AddMinutes(2.5));
		state.AppendRealtime(_start.AddMinutes(4.0), 44.0);

		var columns = state.Line.SnapshotColumns();
		columns.Select(column => column.X).Should().Equal(
			XOf(_start),
			XOf(_start.AddMinutes(1.0)),
			XOf(_start.AddMinutes(2.0).AddMilliseconds(-1.0)),
			XOf(_start.AddMinutes(2.0)),
			XOf(_start.AddMinutes(4.0).AddMilliseconds(-1.0)),
			XOf(_start.AddMinutes(4.0)));
		columns[4].Break.Should().Be(BreakKind.Ahead, "the latest unjoin, the dropping apply, wins");
		columns[^1].Center.Should().Be(44.0);
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
		columns.Where(column => double.IsNaN(column.Center)).Should().OnlyContain(
			column => column.Break == BreakKind.Ahead);
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
		columns[1].Break.Should().Be(BreakKind.Ahead);
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

	[Fact]
	public void FoldRealtime_AReplayAfterARejectedBreak_IsRejectedAndTheNextValueOpensBehindAnOrdinaryBreak()
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));
		state.FoldRealtime(_start.AddSeconds(30.0), 6.0);
		state.AppendRealtime(_start.AddSeconds(30.0).AddMilliseconds(1.0), value: null);

		state.FoldRealtime(_start.AddSeconds(30.0), 6.0);
		state.HoldTo(XOf(_holdEdge));

		state.Line.SnapshotColumns().Should().HaveCount(2, "the replay lies before the last column");
		AssertHeldFrom(state, _start.AddMinutes(1.0));

		state.FoldRealtime(_start.AddMinutes(2.0), 42.0);

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(4, "no line crosses the outage");
		columns[2].Should().Be(new EnvelopeColumn(
			XOf(_start.AddMinutes(2.0).AddMilliseconds(-1.0)),
			double.NaN,
			double.NaN,
			double.NaN,
			BreakKind.Ordinary));
		columns[3].Center.Should().Be(42.0);
		state.CurrentValue.Should().Be(42.0);
	}

	[Fact]
	public void FoldRealtime_TheAheadBreakSpanEndsAtTheFirstLiveBucketsX()
	{
		var state = NewState();
		using var plot = new Plot();
		state.Line.Axes.XAxis = plot.Axes.Bottom;
		plot.Axes.SetLimitsX(XOf(_start), XOf(_start.AddMinutes(5.0)));
		state.LoadHistory(new PenHistoryEnvelope(1, [_start], [1.0], [5.0], [2.0]), _start);

		state.FoldRealtime(_start.AddMinutes(1.0), 9.0);
		state.FoldRealtime(_start.AddMinutes(2.0), 4.0);
		state.FoldRealtime(_start.AddMinutes(3.0), 7.0);

		var spans = new List<AheadBreakSpan>();
		state.Line.CollectAheadBreakSpans(spans);
		spans.Should().Equal(
			[new AheadBreakSpan(XOf(_start), XOf(_start.AddMinutes(1.0)))],
			"the bucket keeps its opening X while values fold into it");
		state.Line.SnapshotColumns().Should().HaveCount(3);
	}

	[Fact]
	public void Hold_BeforeALiveValue_HoldsNothing()
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));

		state.HoldTo(XOf(_holdEdge));

		state.Line.ReadHeldSpan().Should().BeNull("no live value has confirmed the pen");
	}

	[Fact]
	public void Hold_ARejectedFirstValue_ConfirmsAConstantPenAndHoldsItFromItsArchiveRow()
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));

		state.AppendRealtime(_start.AddMinutes(1.0), 6.0);
		state.HoldTo(XOf(_holdEdge));

		AssertHeldFrom(state, _start.AddMinutes(1.0));
		state.CurrentValue.Should().Be(6.0);
	}

	[Fact]
	public void Hold_AHistoryLoadCuttingBackBeforeTheConfirmedStamp_EndsItAndALoadBringingTheRowsBackRestoresIt()
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));
		state.AppendRealtime(_start.AddMinutes(1.0), 6.0);
		state.HoldTo(XOf(_holdEdge));

		state.LoadHistory(new PenHistoryEnvelope(1, [_start], [1.0], [5.0], [2.0]), _start.AddMinutes(0.5));

		state.Line.ReadHeldSpan().Should().BeNull("no hold draws an older value across the change it confirmed");

		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));

		AssertHeldFrom(state, _start.AddMinutes(1.0));
	}

	[Fact]
	public void Hold_ARejectedValueStampedBeforeEveryArchivedRow_SurvivesAPanReadReturningFewerRows()
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));
		state.AppendRealtime(_start.AddMinutes(-1.0), 2.0);
		state.HoldTo(XOf(_holdEdge));

		state.LoadHistory(new PenHistoryEnvelope(1, [_start], [1.0], [5.0], [2.0]), _start.AddMinutes(0.5));

		AssertHeldFrom(state, _start);
		state.CurrentValue.Should().Be(2.0);
	}

	[Fact]
	public void Hold_AtACoarseLayer_StaysWhileValuesFoldIntoTheLastBucket()
	{
		var state = NewState();
		state.LoadHistory(new PenHistoryEnvelope(1, [_start], [1.0], [5.0], [2.0]), _start);
		state.HoldTo(XOf(_holdEdge));

		state.FoldRealtime(_start.AddMinutes(1.0), 9.0);
		AssertHeldFrom(state, _start.AddMinutes(1.0));

		state.FoldRealtime(_start.AddMinutes(2.0), 4.0);

		AssertHeldFrom(state, _start.AddMinutes(1.0));
		state.CurrentValue.Should().Be(4.0);
	}

	[Fact]
	public void Hold_AtACoarseLayer_AReadCoveringTheLiveBucketButEndingBeforeItsNewestValue_EndsItUntilTheNextValue()
	{
		var state = NewState();
		state.LoadHistory(new PenHistoryEnvelope(1, [_start], [1.0], [5.0], [2.0]), _start);
		state.HoldTo(XOf(_holdEdge));
		state.FoldRealtime(_start.AddMinutes(1.0), 9.0);
		state.FoldRealtime(_start.AddMinutes(2.0), 4.0);
		AssertHeldFrom(state, _start.AddMinutes(1.0));

		state.LoadHistory(
			new PenHistoryEnvelope(1, [_start, _start.AddSeconds(90.0)], [1.0, 7.0], [5.0, 7.0], [2.0, 7.0]),
			_start.AddSeconds(100.0));

		state.Line.ReadHeldSpan().Should().BeNull(
			"the read ends before the value OPC UA delivered last, so the archive's bucket does not hold it");

		state.FoldRealtime(_start.AddMinutes(3.0), 5.0);

		AssertHeldFrom(state, _start.AddMinutes(3.0));
		state.CurrentValue.Should().Be(5.0);
	}

	[Fact]
	public void Hold_AtACoarseLayer_AReadEndingBeforeAValueFoldedIntoAnArchiveBucket_EndsItUntilTheNextValue()
	{
		var state = NewState();
		state.LoadHistory(new PenHistoryEnvelope(1, [_start], [1.0], [5.0], [2.0]), _start);
		state.HoldTo(XOf(_holdEdge));
		state.FoldRealtime(_start.AddMinutes(1.0), 9.0);
		state.FoldRealtime(_start.AddMinutes(2.0), 4.0);
		state.LoadHistory(ArchiveBucketAtOneMinute(), _start.AddSeconds(150.0));
		state.FoldRealtime(_start.AddMinutes(3.0), 9.0);
		AssertHeldFrom(state, _start.AddMinutes(1.0));
		state.CurrentValue.Should().Be(9.0, "the value folded into the archive bucket");

		state.LoadHistory(ArchiveBucketAtOneMinute(), _start.AddMinutes(2.0));

		state.Line.ReadHeldSpan().Should().BeNull(
			"the read ends before the value OPC UA delivered last, so the archive's bucket does not hold it");

		state.FoldRealtime(_start.AddMinutes(4.0), 5.0);

		AssertHeldFrom(state, _start.AddMinutes(4.0));
		state.CurrentValue.Should().Be(5.0);
	}

	[Fact]
	public void Hold_ASourceBreakAppendedAtTheEndOfTheLine_EndsIt()
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));
		state.AppendRealtime(_start.AddMinutes(2.0), 42.0);
		state.HoldTo(XOf(_holdEdge));
		AssertHeldFrom(state, _start.AddMinutes(2.0));

		state.AppendRealtime(_start.AddMinutes(3.0), value: null);

		state.Line.ReadHeldSpan().Should().BeNull();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Hold_AfterARejectedSourceBreak_ReturnsOnlyWithAReplayAtTheOriginalStamp(bool isReplayed)
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));
		state.AppendRealtime(_start, 2.0);
		state.AppendRealtime(_start.AddMilliseconds(1.0), value: null);
		state.Line.SnapshotColumns().Should().HaveCount(2, "the break lies inside the history");

		if (isReplayed)
		{
			state.AppendRealtime(_start, 2.0);
		}

		state.HoldTo(XOf(_holdEdge));

		if (isReplayed)
		{
			AssertHeldFrom(state, _start.AddMinutes(1.0));
		}
		else
		{
			state.Line.ReadHeldSpan().Should().BeNull("the server has not stated the pen's value since the break");
		}
	}

	[Fact]
	public void Hold_AReplayAfterASourceBreakAppendedPastTheLastRow_HoldsNothing()
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));
		state.AppendRealtime(_start.AddMinutes(1.0), 6.0);
		state.AppendRealtime(_start.AddMinutes(1.0).AddMilliseconds(1.0), value: null);

		state.AppendRealtime(_start.AddMinutes(1.0), 6.0);
		state.HoldTo(XOf(_holdEdge));

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(3, "the break lies past the last row and the replay before it");
		double.IsNaN(columns[^1].Center).Should().BeTrue();
		state.Line.ReadHeldSpan().Should().BeNull("the line ends on the appended break");
	}

	[Fact]
	public void Hold_ADroppingHistoryApply_EndsItUntilTheNextValue()
	{
		var state = NewState();
		state.LoadHistory(TwoColumnEnvelope(), _start.AddHours(1.0));
		state.AppendRealtime(_start.AddMinutes(2.0), 42.0);
		state.HoldTo(XOf(_holdEdge));

		state.LoadHistory(TwoColumnEnvelope(), _start.AddMinutes(1.5));

		state.Line.ReadHeldSpan().Should().BeNull("the chart holds no live confirmation past the archive's newest row");

		state.AppendRealtime(_start.AddMinutes(3.0), 43.0);

		AssertHeldFrom(state, _start.AddMinutes(3.0));
		state.CurrentValue.Should().Be(43.0);
	}

	private static void AssertHeldFrom(TrendPenState state, DateTime lastColumnUtc)
	{
		var span = state.Line.ReadHeldSpan();
		span.Should().NotBeNull();
		span.Value.FromX.Should().Be(XOf(lastColumnUtc));
		span.Value.ToX.Should().Be(XOf(_holdEdge));
		span.Value.Value.Should().Be(state.CurrentValue, "the held line and the sidebar read one value");
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

	private static PenHistoryEnvelope ArchiveBucketAtOneMinute()
	{
		return new PenHistoryEnvelope(1, [_start, _start.AddMinutes(1.0)], [1.0, 7.0], [5.0, 7.0], [2.0, 7.0]);
	}

	private static PenHistoryEnvelope TwoColumnEnvelope()
	{
		return new PenHistoryEnvelope(1, [_start, _start.AddMinutes(1.0)], [1.0, 3.0], [5.0, 9.0], [2.0, 6.0]);
	}
}
