using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendChartLiveHoldTests
{
	private const double SlowValue = 6.0;
	private const double FastValue = 42.0;

	private static readonly Pen _slowPen = FakeDataProvider.FirstPen;
	private static readonly Pen _fastPen = FakeDataProvider.SecondPen;
	private static readonly DateTime _start = ChartTestBuilder.From;
	private static readonly DateTime _slowLastRow = _start.AddMinutes(1.0);
	private static readonly DateTime _fastStamp = _start.AddMinutes(5.0);
	private static readonly DateTime _insideTheHeldSpan = _start.AddMinutes(3.0);
	private static readonly LiveSourceState _lost =
		LiveSourceState.FaultedBy(new LiveSourceError(LiveSourceFault.Unreachable, "opc.tcp://127.0.0.1:4840"));

	[AvaloniaFact]
	public void AConfirmedPen_IsHeldToTheNewestSampleWhileTheSessionIsConnected()
	{
		var (viewModel, scheduler, _, provider) = ChartTestBuilder.CreateViewModel();
		using var chart = viewModel;
		var slow = AddSlowPen(chart);
		chart.AddPen(_fastPen);

		ConfirmTheSlowPenAndMoveTheEdge(provider, scheduler);

		chart.Navigation.NewestSample.Should().Be(_fastStamp);
		AssertHeldTo(slow, _fastStamp);
	}

	[AvaloniaFact]
	public void AnInitialConnectingSession_HoldsNoPen()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromHours(1));
		provider.PushLiveState(LiveSourceState.Connecting);
		var (viewModel, _, _, _) = ChartTestBuilder.CreateViewModel(scheduler: scheduler, provider: provider);
		using var chart = viewModel;
		var slow = AddSlowPen(chart);
		chart.AddPen(_fastPen);

		ConfirmTheSlowPenAndMoveTheEdge(provider, scheduler);

		chart.Navigation.NewestSample.Should().Be(_fastStamp);
		slow.Line.ReadHeldSpan().Should().BeNull("the session has never been up");
	}

	[AvaloniaFact]
	public void ALossAsTheSourceWritesIt_EndsTheHoldAndRequestsARedraw()
	{
		var (viewModel, scheduler, _, provider) = ChartTestBuilder.CreateViewModel();
		using var chart = viewModel;
		var slow = AddSlowPen(chart);
		chart.AddPen(_fastPen);
		ConfirmTheSlowPenAndMoveTheEdge(provider, scheduler);
		AssertHeldTo(slow, _fastStamp);
		var redraws = 0;
		using var counter = chart.RedrawRequested.Subscribe(_ => redraws++);

		provider.PushLiveState(_lost);

		redraws.Should().Be(1, "the session state alone redraws");
		slow.Line.ReadHeldSpan().Should().BeNull();

		PushTheLossBreaks(provider, scheduler);

		slow.Line.ReadHeldSpan().Should().BeNull();
	}

	// The source raises a non-numeric break to the pen's newest stamp, so one batch carries the value, then the break.
	[AvaloniaFact]
	public void ABreakAtTheStampOfTheValueAheadOfItInOneBatch_EndsTheHold()
	{
		var (viewModel, scheduler, _, provider) = ChartTestBuilder.CreateViewModel();
		using var chart = viewModel;
		var slow = AddSlowPen(chart);
		chart.AddPen(_fastPen);
		ConfirmTheSlowPenAndMoveTheEdge(provider, scheduler);
		AssertHeldTo(slow, _fastStamp);
		var changedUtc = _fastStamp.AddMinutes(1.0);

		provider.PushLiveSamples([new Sample(_slowPen.PenId, changedUtc, FastValue)]);
		provider.PushLiveSamples([new Sample(_slowPen.PenId, changedUtc, null)]);
		provider.PushLiveSamples([new Sample(_fastPen.PenId, changedUtc.AddMinutes(1.0), FastValue)]);
		AdvancePastTheBatchWindow(scheduler);

		slow.Line.SnapshotColumns()[^1].Center.Should().Be(FastValue, "the break at the value's own stamp is rejected");
		slow.CurrentValue.Should().Be(FastValue, "the value was taken before the break");
		slow.Line.ReadHeldSpan().Should().BeNull("the server no longer states the value the line ends on");
	}

	[AvaloniaTheory]
	[InlineData(true)]
	[InlineData(false)]
	public void AReconnectedSession_HoldsThePenAgainOnlyAfterAReplayAtItsOriginalStamp(bool isReplayed)
	{
		var (viewModel, scheduler, _, provider) = ChartTestBuilder.CreateViewModel();
		using var chart = viewModel;
		var slow = AddSlowPen(chart);
		chart.AddPen(_fastPen);
		ConfirmTheSlowPenAndMoveTheEdge(provider, scheduler);
		AssertHeldTo(slow, _fastStamp);
		provider.PushLiveState(_lost);
		PushTheLossBreaks(provider, scheduler);

		provider.PushLiveState(LiveSourceState.Connected);

		slow.Line.ReadHeldSpan().Should().BeNull("the server has not stated the pen's value since the loss");

		if (isReplayed)
		{
			provider.PushLiveSamples([new Sample(_slowPen.PenId, _start, SlowValue)]);
			AdvancePastTheBatchWindow(scheduler);

			AssertHeldTo(slow, _fastStamp.AddMilliseconds(1.0));
		}
		else
		{
			AdvancePastTheBatchWindow(scheduler);

			slow.Line.ReadHeldSpan().Should().BeNull();
		}
	}

	[AvaloniaFact]
	public void APenALaterCatalogueReadAdds_TakesTheCurrentEdgeOnceConfirmed()
	{
		var (viewModel, scheduler, _, provider) = ChartTestBuilder.CreateViewModel();
		using var chart = viewModel;
		chart.AddPen(_fastPen);
		provider.PushLiveSamples([new Sample(_fastPen.PenId, _fastStamp, FastValue)]);
		AdvancePastTheBatchWindow(scheduler);

		var slow = AddSlowPen(chart);

		slow.Line.ReadHeldSpan().Should().BeNull("no live value has confirmed the added pen");

		provider.PushLiveSamples([new Sample(_slowPen.PenId, _slowLastRow, SlowValue)]);
		AdvancePastTheBatchWindow(scheduler);

		chart.Navigation.NewestSample.Should().Be(_fastStamp, "the confirming value moved no edge");
		AssertHeldTo(slow, _fastStamp);
	}

	[AvaloniaFact]
	public void TheCursorInsideTheHeldSpan_ReadsTheCurrentValue_AndGivesAPenWithNoEnvelopeItsKey()
	{
		var (viewModel, scheduler, _, provider) = ChartTestBuilder.CreateViewModel();
		using var chart = viewModel;
		var slow = AddSlowPen(chart);
		chart.AddPen(_fastPen);
		ConfirmTheSlowPenAndMoveTheEdge(provider, scheduler);

		chart.MoveCursor(_insideTheHeldSpan);

		chart.CursorValues.Should().ContainKey(_slowPen.PenId, "a held value is a reading with no envelope behind it");
		chart.CursorValues[_slowPen.PenId].Should().Be(slow.CurrentValue);
		slow.CurrentValue.Should().Be(SlowValue);
	}

	[AvaloniaFact]
	public void APenWithAnEnvelope_ReadsItsCurrentValueInsideTheHeldSpanPastTheEnvelopesEnd()
	{
		var (viewModel, scheduler, _, provider) = ChartTestBuilder.CreateViewModel();
		using var chart = viewModel;
		var held = chart.AddPen(_slowPen);
		chart.AddPen(_fastPen);
		ChartTestBuilder.LoadInitialHistory(chart, scheduler, ChartTestBuilder.From, ChartTestBuilder.To);
		var envelopeEnd = provider.ChartHistoryQueries.Max(query => query.ToUtc);
		var edge = envelopeEnd.AddMinutes(4.0);
		var pastTheEnvelope = envelopeEnd.AddMinutes(2.0);
		chart.MoveCursor(pastTheEnvelope);
		chart.CursorValues[_slowPen.PenId].Should().BeNull(
			"the envelope ends before the cursor and nothing is held yet");

		provider.PushLiveSamples(
		[
			new Sample(_slowPen.PenId, ChartTestBuilder.From, FakeDataProvider.DefaultCenter),
			new Sample(_fastPen.PenId, edge, FastValue)
		]);
		AdvancePastTheBatchWindow(scheduler);
		chart.MoveCursor(pastTheEnvelope);

		held.Line.ReadHeldSpan().Should().NotBeNull();
		chart.CursorValues[_slowPen.PenId].Should().Be(held.CurrentValue);
		held.CurrentValue.Should().Be(FakeDataProvider.DefaultCenter);
	}

	[AvaloniaTheory]
	[InlineData(NoReading.PastTheHoldEnd)]
	[InlineData(NoReading.SessionDown)]
	[InlineData(NoReading.HiddenPen)]
	public void TheCursor_ReadsNoHeldValue(NoReading reason)
	{
		var (viewModel, scheduler, _, provider) = ChartTestBuilder.CreateViewModel();
		using var chart = viewModel;
		var slow = AddSlowPen(chart);
		chart.AddPen(_fastPen);
		ConfirmTheSlowPenAndMoveTheEdge(provider, scheduler);
		AssertHeldTo(slow, _fastStamp);
		var cursor = _insideTheHeldSpan;

		switch (reason)
		{
			case NoReading.PastTheHoldEnd:
				cursor = _fastStamp.AddMinutes(1.0);
				break;
			case NoReading.SessionDown:
				provider.PushLiveState(_lost);
				break;
			case NoReading.HiddenPen:
				chart.SetPenVisibility(_slowPen.PenId, false);
				slow.Line.ReadHeldSpan().Should().NotBeNull("the hidden pen keeps its hold; the reader skips the pen");
				break;
		}

		chart.MoveCursor(cursor);

		chart.CursorValues.GetValueOrDefault(_slowPen.PenId).Should().BeNull();
		chart.ActivePenReadingAt(cursor).Should().BeNull();
	}

	[AvaloniaTheory]
	[InlineData(HeldSpanEdge.Start, true)]
	[InlineData(HeldSpanEdge.End, true)]
	[InlineData(HeldSpanEdge.OneMillisecondPastTheEnd, false)]
	public void TheCursorAtTheHeldSpansEdges_ReadsTheCurrentValueOnlyInsideIt(HeldSpanEdge edge, bool readsAValue)
	{
		var (viewModel, scheduler, _, provider) = ChartTestBuilder.CreateViewModel();
		using var chart = viewModel;
		var slow = AddSlowPen(chart);
		chart.AddPen(_fastPen);
		ConfirmTheSlowPenAndMoveTheEdge(provider, scheduler);
		AssertHeldTo(slow, _fastStamp);
		var cursor = edge switch
		{
			HeldSpanEdge.Start => _slowLastRow,
			HeldSpanEdge.End => _fastStamp,
			_ => _fastStamp.AddMilliseconds(1.0)
		};

		chart.MoveCursor(cursor);

		chart.CursorValues.GetValueOrDefault(_slowPen.PenId).Should().Be(readsAValue ? SlowValue : null);
	}

	[AvaloniaFact]
	public void ADisposedChart_NoLongerMovesTheHoldEndWithTheNewestSample()
	{
		var (chart, scheduler, _, provider) = ChartTestBuilder.CreateViewModel();
		var slow = AddSlowPen(chart);
		chart.AddPen(_fastPen);
		ConfirmTheSlowPenAndMoveTheEdge(provider, scheduler);
		AssertHeldTo(slow, _fastStamp);

		chart.Dispose();
		chart.Navigation.OnLiveEdge(_fastStamp.AddMinutes(2.0));

		chart.Navigation.NewestSample.Should().Be(_fastStamp.AddMinutes(2.0));
		AssertHeldTo(slow, _fastStamp);
	}

	private static TrendPenState AddSlowPen(TrendChartViewModel chart)
	{
		var slow = chart.AddPen(_slowPen);
		slow.LoadHistory(
			new PenHistoryEnvelope(
				_slowPen.PenId,
				[_start, _slowLastRow],
				[2.0, SlowValue],
				[2.0, SlowValue],
				[2.0, SlowValue]),
			_slowLastRow);

		return slow;
	}

	// The slow pen's value at its first row is rejected and confirms it; the fast pen's moves the newest sample.
	private static void ConfirmTheSlowPenAndMoveTheEdge(FakeDataProvider provider, TestScheduler scheduler)
	{
		provider.PushLiveSamples(
		[
			new Sample(_slowPen.PenId, _start, SlowValue),
			new Sample(_fastPen.PenId, _fastStamp, FastValue)
		]);
		AdvancePastTheBatchWindow(scheduler);
	}

	private static void PushTheLossBreaks(FakeDataProvider provider, TestScheduler scheduler)
	{
		provider.PushLiveSamples(
		[
			new Sample(_slowPen.PenId, _start.AddMilliseconds(1.0), null),
			new Sample(_fastPen.PenId, _fastStamp.AddMilliseconds(1.0), null)
		]);
		AdvancePastTheBatchWindow(scheduler);
	}

	private static void AdvancePastTheBatchWindow(TestScheduler scheduler)
	{
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);
	}

	private static void AssertHeldTo(TrendPenState slow, DateTime edgeUtc)
	{
		var span = slow.Line.ReadHeldSpan();
		span.Should().NotBeNull();
		span.Value.FromX.Should().Be(LocalTimeAxis.ToAxis(_slowLastRow));
		span.Value.ToX.Should().Be(LocalTimeAxis.ToAxis(edgeUtc));
		span.Value.Value.Should().Be(slow.CurrentValue, "the held line and the sidebar read one value");
		slow.CurrentValue.Should().Be(SlowValue);
	}

	public enum NoReading
	{
		PastTheHoldEnd,
		SessionDown,
		HiddenPen
	}

	public enum HeldSpanEdge
	{
		Start,
		End,
		OneMillisecondPastTheEnd
	}
}
