using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartRealtimeApplierTests
{
	private const double SampledValue = 7.0;
	private static readonly DateTime _start = ChartTestBuilder.From;

	[Fact]
	public void AtRaw_AValueAndThenABreak_AppendARealColumnAndThenANaNColumn()
	{
		var state = NewState();

		Apply(state, foldIntoColumn: false, (_start, SampledValue), (_start.AddMilliseconds(1), null));

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(2);
		columns[0].Center.Should().Be(SampledValue);
		double.IsNaN(columns[1].Center).Should().BeTrue();
		columns[1].X.Should().Be(LocalTimeAxis.ToAxis(_start.AddMilliseconds(1)));
		state.CurrentValue.Should().Be(SampledValue);
	}

	[Fact]
	public void AtACoarseLayer_TheValueFoldsAndTheBreakAppendsPastTheFoldedColumn()
	{
		var state = NewState();
		state.LoadHistory(new PenHistoryEnvelope(1, [_start], [1.0], [5.0], [2.0]), _start, null);

		Apply(
			state,
			foldIntoColumn: true,
			(_start.AddSeconds(1), SampledValue),
			(_start.AddSeconds(1).AddMilliseconds(1), null));

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(2);
		columns[0].Min.Should().Be(1.0);
		columns[0].Max.Should().Be(SampledValue);
		columns[0].Center.Should().Be(SampledValue);
		double.IsNaN(columns[1].Center).Should().BeTrue();
		columns[1].X.Should().Be(LocalTimeAxis.ToAxis(_start.AddSeconds(1).AddMilliseconds(1)));
		state.CurrentValue.Should().Be(SampledValue);
	}

	// docs/architecture/charting.md#per-pen-plottable-envelopeline
	[Fact]
	public void AtACoarseLayer_OnAPenWithNoColumn_TheBreakAppendsAndTheNextValueOpensAColumnPastIt()
	{
		const double NextValue = 9.0;
		var state = NewState();

		Apply(
			state,
			foldIntoColumn: true,
			(_start, SampledValue),
			(_start.AddMilliseconds(1), null),
			(_start.AddSeconds(1), NextValue));

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(2);
		double.IsNaN(columns[0].Center).Should().BeTrue();
		columns[1].Center.Should().Be(NextValue);
		columns[1].X.Should().Be(LocalTimeAxis.ToAxis(_start.AddSeconds(1)));
		state.CurrentValue.Should().Be(NextValue);
	}

	[Fact]
	public void AtACoarseLayer_AValueOneColumnWidthPastTheLastColumn_OpensAColumnTheHoldStartsFrom()
	{
		var state = NewState(FakeDataProvider.FirstPen);
		state.LoadHistory(new PenHistoryEnvelope(1, [_start], [1.0], [5.0], [2.0]), _start, null);
		var navigation = new ChartNavigationController();
		navigation.SetWidth(TimeSpan.FromMinutes(navigation.TargetColumnCount));
		navigation.ColumnWidth.Should().Be(TimeSpan.FromMinutes(1.0));
		var withinTheColumn = _start.AddSeconds(30.0);
		var pastTheColumn = _start.AddMinutes(90.0);
		var answeredAt = _start.AddMinutes(91.0);

		var batch = Batch(
			answeredAt,
			resumesAfterFailure: false,
			(state.Pen.PenId, withinTheColumn, SampledValue),
			(state.Pen.PenId, pastTheColumn, 9.0));
		new ChartRealtimeApplier(new Dictionary<int, TrendPenState> { [state.Pen.PenId] = state }, navigation)
			.Apply(batch, foldIntoColumn: true);

		state.Line.SnapshotColumns().Should().Equal(
			new EnvelopeColumn(LocalTimeAxis.ToAxis(_start), 1.0, SampledValue, SampledValue),
			new EnvelopeColumn(LocalTimeAxis.ToAxis(pastTheColumn), 9.0, 9.0, 9.0));
		state.Line.ReadHeldSpan().Should().Be(new HeldSpan(
			LocalTimeAxis.ToAxis(pastTheColumn), LocalTimeAxis.ToAxis(answeredAt), 9.0));
	}

	[Fact]
	public void ABatchWithAnAnswerAndNoSample_HoldsEveryPenToTheAnswer()
	{
		var first = NewState(FakeDataProvider.FirstPen);
		var second = NewState(FakeDataProvider.SecondPen);
		first.AppendRealtime(_start, SampledValue);
		second.AppendRealtime(_start, SampledValue);
		second.SetVisibility(false);
		var answeredAt = _start.AddSeconds(5.0);

		Apply([first, second], Batch(answeredAt, resumesAfterFailure: false));

		first.Line.ReadHeldSpan().Should().Be(new HeldSpan(
			LocalTimeAxis.ToAxis(_start), LocalTimeAxis.ToAxis(answeredAt), SampledValue));
		second.Line.ReadHeldSpan().Should().Be(new HeldSpan(
			LocalTimeAxis.ToAxis(_start), LocalTimeAxis.ToAxis(answeredAt), SampledValue));
	}

	[Fact]
	public void ABatchWithSamples_AppendsThemFirstAndHoldsFromTheNewLastColumn()
	{
		const double NextValue = 9.0;
		var state = NewState(FakeDataProvider.FirstPen);
		state.AppendRealtime(_start, SampledValue);
		var sampledAt = _start.AddSeconds(1.0);
		var answeredAt = _start.AddSeconds(2.0);

		Apply(
			[state],
			Batch(answeredAt, resumesAfterFailure: false, (state.Pen.PenId, sampledAt, NextValue)));

		state.Line.SnapshotColumns().Should().HaveCount(2);
		state.Line.ReadHeldSpan().Should().Be(new HeldSpan(
			LocalTimeAxis.ToAxis(sampledAt), LocalTimeAxis.ToAxis(answeredAt), NextValue));
	}

	[Fact]
	public void AResumedBatch_BreaksEveryPenAtItsOldHoldEndBeforeItsSamples()
	{
		const double NextValue = 9.0;
		var first = NewState(FakeDataProvider.FirstPen);
		var second = NewState(FakeDataProvider.SecondPen);
		first.AppendRealtime(_start, SampledValue);
		second.AppendRealtime(_start, SampledValue);
		var lastAnswer = _start.AddSeconds(1.0);
		var sampledAt = _start.AddSeconds(10.0);
		var resumedAnswer = _start.AddSeconds(11.0);
		var applier = NewApplier(first, second);
		applier.Apply(Batch(lastAnswer, resumesAfterFailure: false), foldIntoColumn: false);

		var endsAVisibleOutage = applier.Apply(
			Batch(resumedAnswer, resumesAfterFailure: true, (first.Pen.PenId, sampledAt, NextValue)),
			foldIntoColumn: false);

		endsAVisibleOutage.Should().BeTrue();
		var firstColumns = first.Line.SnapshotColumns();
		firstColumns.Select(column => column.X).Should().Equal(
			LocalTimeAxis.ToAxis(_start), LocalTimeAxis.ToAxis(lastAnswer), LocalTimeAxis.ToAxis(sampledAt));
		double.IsNaN(firstColumns[1].Center).Should().BeTrue();
		first.Line.ReadHeldSpan().Should().Be(new HeldSpan(
			LocalTimeAxis.ToAxis(sampledAt), LocalTimeAxis.ToAxis(resumedAnswer), NextValue));
		var secondColumns = second.Line.SnapshotColumns();
		secondColumns.Should().HaveCount(2);
		secondColumns[1].X.Should().Be(LocalTimeAxis.ToAxis(lastAnswer));
		double.IsNaN(secondColumns[1].Center).Should().BeTrue();
		second.Line.ReadHeldSpan().Should().BeNull("a pen holds again from its next change");
	}

	[Fact]
	public void AResumedBatch_KeepsASampleStampedBeforeTheOldHoldEnd()
	{
		const double CommittedLate = 8.0;
		var state = NewState(FakeDataProvider.FirstPen);
		state.AppendRealtime(_start, SampledValue);
		var committedAt = _start.AddSeconds(1.0);
		var lastAnswer = _start.AddSeconds(2.0);
		var resumedAnswer = _start.AddSeconds(11.0);
		var applier = NewApplier(state);
		applier.Apply(Batch(lastAnswer, resumesAfterFailure: false), foldIntoColumn: false);

		applier.Apply(
			Batch(resumedAnswer, resumesAfterFailure: true, (state.Pen.PenId, committedAt, CommittedLate)),
			foldIntoColumn: false);

		var columns = state.Line.SnapshotColumns();
		columns.Select(column => column.X).Should().Equal(
			LocalTimeAxis.ToAxis(_start), LocalTimeAxis.ToAxis(committedAt), LocalTimeAxis.ToAxis(lastAnswer));
		double.IsNaN(columns[^1].Center).Should().BeTrue();
		state.CurrentValue.Should().Be(CommittedLate);
	}

	[Fact]
	public void AResumedBatch_NoLaterThanTheShortestVisibleOutage_BreaksNothingAndHoldsOn()
	{
		var state = NewState(FakeDataProvider.FirstPen);
		state.AppendRealtime(_start, SampledValue);
		var lastAnswer = _start.AddSeconds(1.0);
		var resumedAnswer = lastAnswer + ChartRealtimeApplier.ShortestVisibleOutage;
		var applier = NewApplier(state);
		applier.Apply(Batch(lastAnswer, resumesAfterFailure: false), foldIntoColumn: false);

		var endsAVisibleOutage = applier.Apply(Batch(resumedAnswer, resumesAfterFailure: true), foldIntoColumn: false);

		endsAVisibleOutage.Should().BeFalse();
		state.Line.SnapshotColumns().Should().ContainSingle();
		state.Line.ReadHeldSpan().Should().Be(new HeldSpan(
			LocalTimeAxis.ToAxis(_start), LocalTimeAxis.ToAxis(resumedAnswer), SampledValue));
	}

	[Fact]
	public void AResumedBatch_PastTheShortestVisibleOutage_BreaksAtTheOldHoldEnd()
	{
		var state = NewState(FakeDataProvider.FirstPen);
		state.AppendRealtime(_start, SampledValue);
		var lastAnswer = _start.AddSeconds(1.0);
		var resumedAnswer = lastAnswer + ChartRealtimeApplier.ShortestVisibleOutage + TimeSpan.FromMilliseconds(1.0);
		var applier = NewApplier(state);
		applier.Apply(Batch(lastAnswer, resumesAfterFailure: false), foldIntoColumn: false);

		var endsAVisibleOutage = applier.Apply(Batch(resumedAnswer, resumesAfterFailure: true), foldIntoColumn: false);

		endsAVisibleOutage.Should().BeTrue();
		var columns = state.Line.SnapshotColumns();
		columns.Select(column => column.X).Should().Equal(
			LocalTimeAxis.ToAxis(_start), LocalTimeAxis.ToAxis(lastAnswer));
		double.IsNaN(columns[^1].Center).Should().BeTrue();
		state.Line.ReadHeldSpan().Should().BeNull();
	}

	// With no answer before it there is no hold to break and no outage to measure.
	[Fact]
	public void AResumedFirstBatch_BreaksNothing()
	{
		var state = NewState(FakeDataProvider.FirstPen);
		state.AppendRealtime(_start, SampledValue);
		var answeredAt = _start.AddSeconds(10.0);
		var applier = NewApplier(state);

		var endsAVisibleOutage = applier.Apply(Batch(answeredAt, resumesAfterFailure: true), foldIntoColumn: false);

		endsAVisibleOutage.Should().BeFalse();
		state.Line.SnapshotColumns().Should().ContainSingle();
	}

	private static void Apply(TrendPenState state, bool foldIntoColumn, params (DateTime Utc, double? Value)[] samples)
	{
		var timestamps = samples.Select(sample => sample.Utc).ToArray();
		var batch = new RealtimeBatch(
			timestamps,
			[new PenRealtimeValues(state.Pen.PenId, timestamps, [.. samples.Select(sample => sample.Value)])],
			timestamps.Max(),
			ResumesAfterFailure: false);

		NewApplier(state).Apply(batch, foldIntoColumn);
	}

	private static void Apply(IReadOnlyList<TrendPenState> states, RealtimeBatch batch)
	{
		NewApplier([.. states]).Apply(batch, foldIntoColumn: false);
	}

	private static RealtimeBatch Batch(
		DateTime answeredAtUtc,
		bool resumesAfterFailure,
		params (int PenId, DateTime Utc, double? Value)[] samples)
	{
		var pens = samples
			.GroupBy(sample => sample.PenId)
			.Select(pen => new PenRealtimeValues(
				pen.Key, [.. pen.Select(sample => sample.Utc)], [.. pen.Select(sample => sample.Value)]))
			.ToArray();

		return new RealtimeBatch(
			[.. samples.Select(sample => sample.Utc).Distinct().Order()],
			pens,
			answeredAtUtc,
			resumesAfterFailure);
	}

	private static ChartRealtimeApplier NewApplier(params TrendPenState[] states)
	{
		return new ChartRealtimeApplier(
			states.ToDictionary(state => state.Pen.PenId),
			new ChartNavigationController());
	}

	private static TrendPenState NewState(Pen pen)
	{
		return new TrendPenState(pen, new EnvelopeLine());
	}

	private static TrendPenState NewState()
	{
		return NewState(FakeDataProvider.FirstPen);
	}
}
