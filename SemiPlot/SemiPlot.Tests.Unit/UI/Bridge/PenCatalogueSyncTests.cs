using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Messages;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Bridge;

// Plain facts: with no SynchronizationContext the loop resumes inline, so a read the scheduler or ReadNow
// starts has run by the time the call returns.
[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class PenCatalogueSyncTests : IDisposable
{
	private static readonly TimeSpan _tick = TimeSpan.FromTicks(1);
	private static readonly ArchiveError _tableMissing = FakeDataProvider.BenchArchiveError(ArchiveFault.TableMissing);

	private readonly TestScheduler _scheduler = new();
	private readonly FakeDataProvider _provider;
	private readonly MessagePanelViewModel _messagePanel = new();
	private readonly PenCatalogueSync _sync;
	private readonly List<PenListDelta> _deltas = [];
	private readonly List<ArchiveConnectionState> _states = [];

	public PenCatalogueSyncTests()
	{
		_provider = new FakeDataProvider(_scheduler, TimeSpan.FromSeconds(1));
		_sync = new PenCatalogueSync(
			_provider, _provider.Pens, _messagePanel, _scheduler, NullLogger<PenCatalogueSync>.Instance);
		_sync.Deltas.Subscribe(_deltas.Add);
		_sync.ConnectionStates.Subscribe(_states.Add);
	}

	public void Dispose()
	{
		_sync.Dispose();
		_messagePanel.Dispose();
	}

	[Fact]
	public void TheFirstRead_ComesOneIntervalAfterStart_ThenOneEveryInterval()
	{
		StartAndEnterTheWait();
		_provider.PensQueryCount.Should().Be(0, "the start sequence has just read the same catalogue");

		Advance(ArchiveReadLoop.ReadInterval - _tick);
		_provider.PensQueryCount.Should().Be(0);

		Advance(_tick);
		_provider.PensQueryCount.Should().Be(1);

		Advance(ArchiveReadLoop.ReadInterval);
		_provider.PensQueryCount.Should().Be(2);
	}

	[Fact]
	public void NoRead_StartsWhileOneIsHeld()
	{
		_provider.GatePens = true;
		StartAndEnterTheWait();
		Advance(ArchiveReadLoop.ReadInterval);

		Advance(ArchiveReadLoop.ReadInterval * 4);
		_provider.PensQueryCount.Should().Be(1);

		_provider.PensGate.SetResult(Result.Ok(_provider.Pens));
		Advance(ArchiveReadLoop.ReadInterval - _tick);
		_provider.PensQueryCount.Should().Be(1, "the wait after a read is a full interval");

		Advance(_tick);
		_provider.PensQueryCount.Should().Be(2);
	}

	[Fact]
	public void ReadNow_DuringAHeldRead_GivesExactlyOneReadAfterIt()
	{
		_provider.GatePens = true;
		StartAndEnterTheWait();
		Advance(ArchiveReadLoop.ReadInterval);

		_sync.ReadNow();
		_sync.ReadNow();
		_provider.PensQueryCount.Should().Be(1);

		_provider.PensGate.SetResult(Result.Ok(_provider.Pens));
		_provider.PensQueryCount.Should().Be(2);

		Advance(ArchiveReadLoop.ReadInterval - _tick);
		_provider.PensQueryCount.Should().Be(2, "the wait after the asked read is a full interval");

		Advance(_tick);
		_provider.PensQueryCount.Should().Be(3);
	}

	[Fact]
	public void ReadNow_WhileWaiting_ReadsAtOnce_AndTheNextReadComesAFullIntervalLater()
	{
		StartAndEnterTheWait();
		Advance(TimeSpan.FromSeconds(2));

		_sync.ReadNow();
		_provider.PensQueryCount.Should().Be(1);

		Advance(ArchiveReadLoop.ReadInterval - _tick);
		_provider.PensQueryCount.Should().Be(1);

		Advance(_tick);
		_provider.PensQueryCount.Should().Be(2);
	}

	[Fact]
	public void AnUnchangedRead_EmitsNoDelta()
	{
		StartAndEnterTheWait();

		Advance(ArchiveReadLoop.ReadInterval * 2);

		_provider.PensQueryCount.Should().BeGreaterThan(0, "the catalogue was read");
		_deltas.Should().BeEmpty();
	}

	[Fact]
	public void AChangedRead_EmitsItsDeltaOnce_AndBecomesTheBaseline()
	{
		StartAndEnterTheWait();
		_provider.Pens = [Renamed(_provider.Pens[0]), _provider.Pens[1]];

		Advance(ArchiveReadLoop.ReadInterval * 2);

		_deltas.Should().ContainSingle()
			.Which.Revised.Should().ContainSingle()
			.Which.Should().Be(_provider.Pens[0]);
	}

	[Fact]
	public void TheFirstSuccess_EmitsConnectedOnce_AndLaterSuccessesEmitNothing()
	{
		StartAndReadNow();
		_states.Should().Equal(ArchiveConnectionState.Connected);

		Advance(ArchiveReadLoop.ReadInterval * 3);

		_provider.PensQueryCount.Should().Be(4);
		_states.Should().ContainSingle("only the first success is news");
	}

	[Fact]
	public void TwoUnreachableReads_EmitNothing_TheThirdRaisesConnectionLost_AndTheNextSuccessCarriesEverythingSince()
	{
		var stored = _provider.Pens;
		_provider.FailPens = true;
		StartAndEnterTheWait();

		Advance(ArchiveReadLoop.ReadInterval * 2);
		_states.Should().BeEmpty("one reconnect after a server restart must not raise the fault");

		Advance(ArchiveReadLoop.ReadInterval);
		_states.Should().ContainSingle();
		ShouldBeConnectionLost(_states[0]);
		_messagePanel.Entries.Should().BeEmpty("the bar writes the one entry a lost connection makes");

		_provider.FailPens = false;
		_provider.Pens = [Renamed(stored[0]), stored[1] with { Color = "#0000ff" }];
		Advance(ArchiveReadLoop.ReadInterval);

		_states.Should().HaveCount(2);
		_states[1].Should().Be(ArchiveConnectionState.Connected);
		_deltas.Should().ContainSingle()
			.Which.Revised.Select(pen => pen.PenId).Should().Equal(1, 2);
	}

	[Fact]
	public void AFourthUnreachableRead_EmitsNothing()
	{
		StartAndReadNow();
		_provider.FailPens = true;

		Advance(ArchiveReadLoop.ReadInterval * 4);

		_provider.PensQueryCount.Should().Be(5);
		_states.Should().HaveCount(2);
		_states[0].Should().Be(ArchiveConnectionState.Connected);
		ShouldBeConnectionLost(_states[1]);
		_messagePanel.Entries.Should().BeEmpty();
	}

	[Fact]
	public void TheFirstSuccessAfterTheFault_EmitsConnected_AndCarriesEverythingSince()
	{
		var stored = _provider.Pens;
		StartAndReadNow();
		_provider.FailPens = true;
		Advance(ArchiveReadLoop.ReadInterval * 3);

		_states.Should().HaveCount(2);
		_states[0].Should().Be(ArchiveConnectionState.Connected);
		ShouldBeConnectionLost(_states[1]);

		_provider.FailPens = false;
		_provider.Pens = [Renamed(stored[0]), stored[1] with { Color = "#0000ff" }];
		Advance(ArchiveReadLoop.ReadInterval);

		_states.Should().HaveCount(3);
		_states[2].Should().Be(ArchiveConnectionState.Connected);
		_deltas.Should().ContainSingle()
			.Which.Revised.Select(pen => pen.PenId).Should().Equal(1, 2);
		_messagePanel.Entries.Should().BeEmpty();

		Advance(ArchiveReadLoop.ReadInterval);
		_states.Should().HaveCount(3, "only the first success after the fault is news");
	}

	[Fact]
	public void ASecondOutageAfterARecovery_RaisesConnectionLostAgain()
	{
		StartAndReadNow();
		_provider.FailPens = true;
		Advance(ArchiveReadLoop.ReadInterval * 3);
		_provider.FailPens = false;
		Advance(ArchiveReadLoop.ReadInterval);

		_provider.FailPens = true;
		Advance(ArchiveReadLoop.ReadInterval * 2);
		_states.Should().HaveCount(3);

		Advance(ArchiveReadLoop.ReadInterval);
		_states.Should().HaveCount(4);
		ShouldBeConnectionLost(_states[3]);
		_messagePanel.Entries.Should().BeEmpty();
	}

	[Fact]
	public void ATableMissingReadAfterTheFault_LeavesTheFault_AndReachesThePanelAtOnce()
	{
		StartAndReadNow();
		_provider.FailPens = true;
		Advance(ArchiveReadLoop.ReadInterval * 3);

		_provider.PensFailureKind = ArchiveFault.TableMissing;
		Advance(ArchiveReadLoop.ReadInterval);

		_states.Should().HaveCount(2, "only a success ends the fault");
		_messagePanel.Entries.Should().ContainSingle("the failed reads before it count toward the third in a row")
			.Which.View.Should().Be(ArchiveFailureMapper.Map(_tableMissing));

		_provider.FailPens = false;
		Advance(ArchiveReadLoop.ReadInterval);
		_states.Should().HaveCount(3);
		_states[2].Should().Be(ArchiveConnectionState.Connected);
	}

	[Fact]
	public void ASuccess_ResetsTheFailureCount()
	{
		_provider.FailPens = true;
		StartAndEnterTheWait();
		Advance(ArchiveReadLoop.ReadInterval * 2);

		_provider.FailPens = false;
		Advance(ArchiveReadLoop.ReadInterval);
		_provider.FailPens = true;
		Advance(ArchiveReadLoop.ReadInterval * 2);

		_states.Should().Equal([ArchiveConnectionState.Connected], "no third failure in a row has happened");
		_messagePanel.Entries.Should().BeEmpty();
	}

	[Fact]
	public void ThreeTableMissingReads_EmitNoState_AndAddOnePanelEntry()
	{
		StartAndReadNow();
		_provider.FailPens = true;
		_provider.PensFailureKind = ArchiveFault.TableMissing;

		Advance(ArchiveReadLoop.ReadInterval * 2);
		_messagePanel.Entries.Should().BeEmpty();

		Advance(ArchiveReadLoop.ReadInterval);
		_messagePanel.Entries.Should().ContainSingle();

		Advance(ArchiveReadLoop.ReadInterval);

		_messagePanel.Entries.Should().ContainSingle("a repeat coalesces into the newest entry")
			.Which.View.Should().Be(ArchiveFailureMapper.Map(_tableMissing));
		_states.Should().Equal(ArchiveConnectionState.Connected);
	}

	[Fact]
	public void TwoUnreachableReadsThenTableMissing_AddOneTableMissingEntry_AndRestartTheConnectionRun()
	{
		StartAndReadNow();
		_provider.FailPens = true;
		Advance(ArchiveReadLoop.ReadInterval * 2);

		_provider.PensFailureKind = ArchiveFault.TableMissing;
		Advance(ArchiveReadLoop.ReadInterval);

		_messagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(_tableMissing));
		_states.Should().Equal(ArchiveConnectionState.Connected);

		_provider.PensFailureKind = ArchiveFault.Unreachable;
		Advance(ArchiveReadLoop.ReadInterval * 2);
		_states.Should().Equal([ArchiveConnectionState.Connected], "the TableMissing read ended the connection run");

		Advance(ArchiveReadLoop.ReadInterval);
		_states.Should().HaveCount(2);
		ShouldBeConnectionLost(_states[1]);
		_messagePanel.Entries.Should().ContainSingle();
	}

	[Fact]
	public void ThreeTableMissingReadsThenUnreachable_AddOneTableMissingEntry_AndEmitNoState()
	{
		StartAndReadNow();
		_provider.FailPens = true;
		_provider.PensFailureKind = ArchiveFault.TableMissing;
		Advance(ArchiveReadLoop.ReadInterval * 3);

		_provider.PensFailureKind = ArchiveFault.Unreachable;
		Advance(ArchiveReadLoop.ReadInterval);

		_messagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(_tableMissing));
		_states.Should().Equal(ArchiveConnectionState.Connected);
	}

	[Fact]
	public void ADeltasSubscriberThatThrows_StillGetsConnectedOutFirst()
	{
		using var throwing = _sync.Deltas.Subscribe(_ => throw new InvalidOperationException("the subscriber threw"));
		_provider.Pens = [Renamed(_provider.Pens[0]), _provider.Pens[1]];

		StartAndReadNow();

		_messagePanel.Entries.Should().ContainSingle("the subscriber threw inside the apply");
		_states.Should().Equal(ArchiveConnectionState.Connected);
	}

	[Fact]
	public void ASubscriberThatThrows_AddsOnePanelEntry_AndTheNextReadStillRuns()
	{
		using var throwing = _sync.Deltas.Subscribe(_ => throw new InvalidOperationException("the subscriber threw"));
		StartAndEnterTheWait();
		_provider.Pens = [Renamed(_provider.Pens[0]), _provider.Pens[1]];

		Advance(ArchiveReadLoop.ReadInterval);
		_messagePanel.Entries.Should().ContainSingle();

		_provider.Pens = [_provider.Pens[0], Renamed(_provider.Pens[1])];
		Advance(ArchiveReadLoop.ReadInterval);

		_deltas.Should().HaveCount(2, "the loop survived the throw and emitted the next change");
		_deltas[1].Revised.Select(pen => pen.PenId).Should().Equal(
			[2], "the read the subscriber threw on is the baseline all the same");
	}

	[Fact]
	public void AReadThatThrows_AddsOnePanelEntryAtOnce_AndTheNextReadComesOneIntervalLater()
	{
		_provider.FailPens = true;
		StartAndEnterTheWait();
		Advance(ArchiveReadLoop.ReadInterval * 2);
		_provider.FailPens = false;
		var thrown = new InvalidOperationException("the read threw");
		_provider.PensReadException = thrown;

		Advance(ArchiveReadLoop.ReadInterval);
		_messagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(new ExceptionalError(thrown)));
		_states.Should().BeEmpty();

		_provider.PensReadException = null;
		_provider.FailPens = true;
		Advance(ArchiveReadLoop.ReadInterval - _tick);
		_provider.PensQueryCount.Should().Be(3);

		Advance(_tick);
		_provider.PensQueryCount.Should().Be(4);
		_states.Should().ContainSingle("the throw left two failed reads in a row standing");
		ShouldBeConnectionLost(_states[0]);
		_messagePanel.Entries.Should().ContainSingle("a lost connection adds no entry of the loop's own");
	}

	// The third failure in a row lands after disposal, so only the stop check keeps it out of the panel.
	[Fact]
	public void Dispose_DuringAHeldRead_ReportsNothingWhenTheReadLands()
	{
		_provider.FailPens = true;
		StartAndEnterTheWait();
		Advance(ArchiveReadLoop.ReadInterval * 2);
		_provider.FailPens = false;
		_provider.GatePens = true;
		Advance(ArchiveReadLoop.ReadInterval);

		_sync.Dispose();
		_provider.PensGate.SetResult(Result.Fail<IReadOnlyList<Pen>>("the held read failed"));

		_deltas.Should().BeEmpty();
		_messagePanel.Entries.Should().BeEmpty();
	}

	[Fact]
	public void Dispose_CompletesTheConnectionStates()
	{
		var isCompleted = false;
		using var subscription = _sync.ConnectionStates.Subscribe(_ => { }, () => isCompleted = true);

		_sync.Dispose();

		isCompleted.Should().BeTrue();
	}

	[Fact]
	public void Dispose_StopsTheReads()
	{
		StartAndEnterTheWait();
		Advance(ArchiveReadLoop.ReadInterval);
		var queriesBefore = _provider.PensQueryCount;

		_sync.Dispose();
		_sync.ReadNow();
		Advance(TimeSpan.FromMinutes(1));

		_provider.PensQueryCount.Should().Be(queriesBefore);
	}

	// TestScheduler runs work scheduled for now one tick later, so the loop enters its first wait on that tick.
	private void StartAndEnterTheWait()
	{
		_sync.Start();
		Advance(_tick);
	}

	// The window's start: the read ReadNow asks for runs on the tick the loop starts.
	private void StartAndReadNow()
	{
		_sync.Start();
		_sync.ReadNow();
		Advance(_tick);
	}

	private static void ShouldBeConnectionLost(ArchiveConnectionState state)
	{
		var expected = FakeDataProvider.BenchArchiveError(ArchiveFault.ConnectionLost, "3");

		state.Fault.Should().BeEquivalentTo(new
		{
			expected.Kind,
			expected.Host,
			expected.Port,
			expected.Database,
			expected.Detail
		});
	}

	private void Advance(TimeSpan time)
	{
		_scheduler.AdvanceBy(time.Ticks);
	}

	private static Pen Renamed(Pen pen)
	{
		return pen with { Name = pen.Name + " renamed" };
	}
}
