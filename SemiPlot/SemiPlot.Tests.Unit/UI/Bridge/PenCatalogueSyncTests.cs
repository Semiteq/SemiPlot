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
	private readonly List<CatalogueRead> _changes = [];
	private readonly List<ArchiveConnectionState> _states = [];

	public PenCatalogueSyncTests()
	{
		_provider = new FakeDataProvider(_scheduler, TimeSpan.FromSeconds(1));
		_sync = new PenCatalogueSync(
			_provider, _provider.Catalogue, _messagePanel, _scheduler, NullLogger<PenCatalogueSync>.Instance);
		_sync.Changes.Subscribe(_changes.Add);
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
		_provider.CatalogueQueryCount.Should().Be(0, "the start sequence has just read the same catalogue");

		Advance(ArchiveReadLoop.ReadInterval - _tick);
		_provider.CatalogueQueryCount.Should().Be(0);

		Advance(_tick);
		_provider.CatalogueQueryCount.Should().Be(1);

		Advance(ArchiveReadLoop.ReadInterval);
		_provider.CatalogueQueryCount.Should().Be(2);
	}

	[Fact]
	public void NoRead_StartsWhileOneIsHeld()
	{
		_provider.GateCatalogue = true;
		StartAndEnterTheWait();
		Advance(ArchiveReadLoop.ReadInterval);

		Advance(ArchiveReadLoop.ReadInterval * 4);
		_provider.CatalogueQueryCount.Should().Be(1);

		_provider.CatalogueGate.SetResult(Result.Ok(_provider.Catalogue));
		Advance(ArchiveReadLoop.ReadInterval - _tick);
		_provider.CatalogueQueryCount.Should().Be(1, "the wait after a read is a full interval");

		Advance(_tick);
		_provider.CatalogueQueryCount.Should().Be(2);
	}

	[Fact]
	public void ReadNow_DuringAHeldRead_GivesExactlyOneReadAfterIt()
	{
		_provider.GateCatalogue = true;
		StartAndEnterTheWait();
		Advance(ArchiveReadLoop.ReadInterval);

		_sync.ReadNow();
		_sync.ReadNow();
		_provider.CatalogueQueryCount.Should().Be(1);

		_provider.CatalogueGate.SetResult(Result.Ok(_provider.Catalogue));
		_provider.CatalogueQueryCount.Should().Be(2);

		Advance(ArchiveReadLoop.ReadInterval - _tick);
		_provider.CatalogueQueryCount.Should().Be(2, "the wait after the asked read is a full interval");

		Advance(_tick);
		_provider.CatalogueQueryCount.Should().Be(3);
	}

	[Fact]
	public void ReadNow_WhileWaiting_ReadsAtOnce_AndTheNextReadComesAFullIntervalLater()
	{
		StartAndEnterTheWait();
		Advance(TimeSpan.FromSeconds(2));

		_sync.ReadNow();
		_provider.CatalogueQueryCount.Should().Be(1);

		Advance(ArchiveReadLoop.ReadInterval - _tick);
		_provider.CatalogueQueryCount.Should().Be(1);

		Advance(_tick);
		_provider.CatalogueQueryCount.Should().Be(2);
	}

	[Fact]
	public void AnUnchangedRead_EmitsNothing()
	{
		StartAndEnterTheWait();

		Advance(ArchiveReadLoop.ReadInterval * 2);

		_provider.CatalogueQueryCount.Should().BeGreaterThan(0, "the catalogue was read");
		_changes.Should().BeEmpty();
	}

	[Fact]
	public void AChangedRead_IsEmittedOnce_AndBecomesTheBaseline()
	{
		StartAndEnterTheWait();
		_provider.Pens = [Renamed(_provider.Pens[0]), _provider.Pens[1]];

		Advance(ArchiveReadLoop.ReadInterval * 2);

		_changes.Should().ContainSingle()
			.Which.Pens.Should().Equal(_provider.Pens);
	}

	[Fact]
	public void AGroupRenameAlone_IsEmitted_WithThePensUnchanged()
	{
		_provider.Stored = new PenCatalogue([], [new StoredGroup(1, "Group A", [1, 2])]);
		StartAndEnterTheWait();
		Advance(ArchiveReadLoop.ReadInterval);
		_changes.Should().ContainSingle("the group joined the catalogue the start did not hold");
		var pensBefore = _changes[0].Pens;

		_provider.Stored = new PenCatalogue([], [new StoredGroup(1, "Group B", [1, 2])]);
		Advance(ArchiveReadLoop.ReadInterval);

		_changes.Should().HaveCount(2);
		var rename = _changes[1];
		rename.Stored.Groups.Should().ContainSingle().Which.Should().BeEquivalentTo(
			new { Id = 1, Name = "Group B", MemberPenIds = new[] { 1, 2 } });
		rename.Pens.Should().Equal(pensBefore);
	}

	[Fact]
	public void ARebaseOnAnOlderRead_MakesTheNextIdenticalReadEmitAgain()
	{
		StartAndEnterTheWait();
		var stored = _provider.Pens;
		_provider.Pens = [Renamed(stored[0]), stored[1]];
		Advance(ArchiveReadLoop.ReadInterval);
		_changes.Should().ContainSingle();

		_sync.Rebase(new CatalogueRead(_provider.Stored, stored));
		Advance(ArchiveReadLoop.ReadInterval);

		_changes.Should().HaveCount(2);
		_changes[1].Pens.Should().Equal(_provider.Pens);
	}

	[Fact]
	public void TheFirstSuccess_EmitsConnectedOnce_AndLaterSuccessesEmitNothing()
	{
		StartAndReadNow();
		_states.Should().Equal(ArchiveConnectionState.Connected);

		Advance(ArchiveReadLoop.ReadInterval * 3);

		_provider.CatalogueQueryCount.Should().Be(4);
		_states.Should().ContainSingle("only the first success is news");
	}

	[Fact]
	public void TwoUnreachableReads_EmitNothing_TheThirdRaisesConnectionLost_AndTheNextSuccessCarriesEverythingSince()
	{
		var stored = _provider.Pens;
		_provider.FailCatalogue = true;
		StartAndEnterTheWait();

		Advance(ArchiveReadLoop.ReadInterval * 2);
		_states.Should().BeEmpty("one reconnect after a server restart must not raise the fault");

		Advance(ArchiveReadLoop.ReadInterval);
		_states.Should().ContainSingle();
		ShouldBeConnectionLost(_states[0]);
		_messagePanel.Entries.Should().BeEmpty("the bar writes the one entry a lost connection makes");

		_provider.FailCatalogue = false;
		_provider.Pens = [Renamed(stored[0]), stored[1] with { Color = "#0000ff" }];
		Advance(ArchiveReadLoop.ReadInterval);

		_states.Should().HaveCount(2);
		_states[1].Should().Be(ArchiveConnectionState.Connected);
		_changes.Should().ContainSingle()
			.Which.Pens.Should().Equal(_provider.Pens);
	}

	[Fact]
	public void AFourthUnreachableRead_EmitsNothing()
	{
		StartAndReadNow();
		_provider.FailCatalogue = true;

		Advance(ArchiveReadLoop.ReadInterval * 4);

		_provider.CatalogueQueryCount.Should().Be(5);
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
		_provider.FailCatalogue = true;
		Advance(ArchiveReadLoop.ReadInterval * 3);

		_states.Should().HaveCount(2);
		_states[0].Should().Be(ArchiveConnectionState.Connected);
		ShouldBeConnectionLost(_states[1]);

		_provider.FailCatalogue = false;
		_provider.Pens = [Renamed(stored[0]), stored[1] with { Color = "#0000ff" }];
		Advance(ArchiveReadLoop.ReadInterval);

		_states.Should().HaveCount(3);
		_states[2].Should().Be(ArchiveConnectionState.Connected);
		_changes.Should().ContainSingle()
			.Which.Pens.Should().Equal(_provider.Pens);
		_messagePanel.Entries.Should().BeEmpty();

		Advance(ArchiveReadLoop.ReadInterval);
		_states.Should().HaveCount(3, "only the first success after the fault is news");
	}

	[Fact]
	public void ASecondOutageAfterARecovery_RaisesConnectionLostAgain()
	{
		StartAndReadNow();
		_provider.FailCatalogue = true;
		Advance(ArchiveReadLoop.ReadInterval * 3);
		_provider.FailCatalogue = false;
		Advance(ArchiveReadLoop.ReadInterval);

		_provider.FailCatalogue = true;
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
		_provider.FailCatalogue = true;
		Advance(ArchiveReadLoop.ReadInterval * 3);

		_provider.CatalogueFailureKind = ArchiveFault.TableMissing;
		Advance(ArchiveReadLoop.ReadInterval);

		_states.Should().HaveCount(2, "only a success ends the fault");
		_messagePanel.Entries.Should().ContainSingle("the failed reads before it count toward the third in a row")
			.Which.View.Should().Be(ArchiveFailureMapper.Map(_tableMissing));

		_provider.FailCatalogue = false;
		Advance(ArchiveReadLoop.ReadInterval);
		_states.Should().HaveCount(3);
		_states[2].Should().Be(ArchiveConnectionState.Connected);
	}

	[Fact]
	public void ASuccess_ResetsTheFailureCount()
	{
		_provider.FailCatalogue = true;
		StartAndEnterTheWait();
		Advance(ArchiveReadLoop.ReadInterval * 2);

		_provider.FailCatalogue = false;
		Advance(ArchiveReadLoop.ReadInterval);
		_provider.FailCatalogue = true;
		Advance(ArchiveReadLoop.ReadInterval * 2);

		_states.Should().Equal([ArchiveConnectionState.Connected], "no third failure in a row has happened");
		_messagePanel.Entries.Should().BeEmpty();
	}

	[Fact]
	public void ThreeTableMissingReads_EmitNoState_AndAddOnePanelEntry()
	{
		StartAndReadNow();
		_provider.FailCatalogue = true;
		_provider.CatalogueFailureKind = ArchiveFault.TableMissing;

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
		_provider.FailCatalogue = true;
		Advance(ArchiveReadLoop.ReadInterval * 2);

		_provider.CatalogueFailureKind = ArchiveFault.TableMissing;
		Advance(ArchiveReadLoop.ReadInterval);

		_messagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(_tableMissing));
		_states.Should().Equal(ArchiveConnectionState.Connected);

		_provider.CatalogueFailureKind = ArchiveFault.Unreachable;
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
		_provider.FailCatalogue = true;
		_provider.CatalogueFailureKind = ArchiveFault.TableMissing;
		Advance(ArchiveReadLoop.ReadInterval * 3);

		_provider.CatalogueFailureKind = ArchiveFault.Unreachable;
		Advance(ArchiveReadLoop.ReadInterval);

		_messagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(_tableMissing));
		_states.Should().Equal(ArchiveConnectionState.Connected);
	}

	[Fact]
	public void AChangesSubscriberThatThrows_StillGetsConnectedOutFirst()
	{
		using var throwing = _sync.Changes.Subscribe(_ => throw new InvalidOperationException("the subscriber threw"));
		_provider.Pens = [Renamed(_provider.Pens[0]), _provider.Pens[1]];

		StartAndReadNow();

		_messagePanel.Entries.Should().ContainSingle("the subscriber threw inside the apply");
		_states.Should().Equal(ArchiveConnectionState.Connected);
	}

	[Fact]
	public void ASubscriberThatThrows_AddsOnePanelEntry_AndTheNextReadStillRuns()
	{
		using var throwing = _sync.Changes.Subscribe(_ => throw new InvalidOperationException("the subscriber threw"));
		StartAndEnterTheWait();
		_provider.Pens = [Renamed(_provider.Pens[0]), _provider.Pens[1]];

		Advance(ArchiveReadLoop.ReadInterval);
		_messagePanel.Entries.Should().ContainSingle();

		Advance(ArchiveReadLoop.ReadInterval);
		_changes.Should().ContainSingle("the read the subscriber threw on is the baseline all the same");

		_provider.Pens = [_provider.Pens[0], Renamed(_provider.Pens[1])];
		Advance(ArchiveReadLoop.ReadInterval);

		_changes.Should().HaveCount(2, "the loop survived the throw and emitted the next change");
		_changes[1].Pens.Should().Equal(_provider.Pens);
	}

	[Fact]
	public void AReadThatThrows_AddsOnePanelEntryAtOnce_AndTheNextReadComesOneIntervalLater()
	{
		_provider.FailCatalogue = true;
		StartAndEnterTheWait();
		Advance(ArchiveReadLoop.ReadInterval * 2);
		_provider.FailCatalogue = false;
		var thrown = new InvalidOperationException("the read threw");
		_provider.CatalogueReadException = thrown;

		Advance(ArchiveReadLoop.ReadInterval);
		_messagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(new ExceptionalError(thrown)));
		_states.Should().BeEmpty();

		_provider.CatalogueReadException = null;
		_provider.FailCatalogue = true;
		Advance(ArchiveReadLoop.ReadInterval - _tick);
		_provider.CatalogueQueryCount.Should().Be(3);

		Advance(_tick);
		_provider.CatalogueQueryCount.Should().Be(4);
		_states.Should().ContainSingle("the throw left two failed reads in a row standing");
		ShouldBeConnectionLost(_states[0]);
		_messagePanel.Entries.Should().ContainSingle("a lost connection adds no entry of the loop's own");
	}

	// The third failure in a row lands after disposal, so only the stop check keeps it out of the panel.
	[Fact]
	public void Dispose_DuringAHeldRead_ReportsNothingWhenTheReadLands()
	{
		_provider.FailCatalogue = true;
		StartAndEnterTheWait();
		Advance(ArchiveReadLoop.ReadInterval * 2);
		_provider.FailCatalogue = false;
		_provider.GateCatalogue = true;
		Advance(ArchiveReadLoop.ReadInterval);

		_sync.Dispose();
		_provider.CatalogueGate.SetResult(Result.Fail<CatalogueRead>("the held read failed"));

		_changes.Should().BeEmpty();
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
		var queriesBefore = _provider.CatalogueQueryCount;

		_sync.Dispose();
		_sync.ReadNow();
		Advance(TimeSpan.FromMinutes(1));

		_provider.CatalogueQueryCount.Should().Be(queriesBefore);
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
