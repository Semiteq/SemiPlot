using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Markers;
using SemiPlot.UI.Messages;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Markers;

[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class MarkerSyncTests : IDisposable
{
	private static readonly TimeSpan _tick = TimeSpan.FromTicks(1);
	private static readonly TimeSpan _readInterval = ArchiveReadLoop.ReadInterval;
	private static readonly StoredMarker _etchStart =
		new(1, new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc), "Etch start");

	private static readonly StoredMarker _etchEnd = new(2, new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc), null);

	private readonly TestScheduler _scheduler = new();
	private readonly FakeMarkerStore _store = new();
	private readonly MessagePanelViewModel _messagePanel = new();
	private readonly FakeLogger<MarkerSync> _logger = new();
	private readonly MarkerSync _sync;
	private readonly List<IReadOnlyList<StoredMarker>> _reads = [];

	public MarkerSyncTests()
	{
		_sync = new MarkerSync(_store, _messagePanel, _scheduler, _logger);
		_sync.Reads.Subscribe(_reads.Add);
	}

	public void Dispose()
	{
		_sync.Dispose();
		_messagePanel.Dispose();
	}

	[Fact]
	public void ReadNow_RightAfterStart_ReadsAtOnce()
	{
		_sync.Start();
		_sync.ReadNow();

		Advance(_tick);

		_store.ReadCount.Should().Be(1, "the window shows its markers right after it opens");
		_reads.Should().ContainSingle();
	}

	[Fact]
	public void AnUnchangedRead_Emits()
	{
		_store.ReadResult = Stored(_etchStart);
		StartAndEnterTheWait();

		Advance(_readInterval * 2);

		_reads.Should().HaveCount(2, "the chart's marker set is the one diff");
		_reads[1].Should().Equal(_reads[0]);
	}

	[Fact]
	public async Task AReadHeldAcrossASuccessfulWrite_IsDiscarded_AndTheReadNowAfterTheWriteEmits()
	{
		StartAndEnterTheWait();
		var heldRead = _store.HoldNextRead();
		_sync.ReadNow();

		(await _sync.AddAsync(_etchEnd.TimeUtc, null)).IsSuccess.Should().BeTrue();
		_store.ReadResult = Stored(_etchStart, _etchEnd);
		heldRead.SetResult(Stored(_etchStart));

		_store.ReadCount.Should().Be(2);
		_reads.Should().ContainSingle()
			.Which.Should().Equal(_etchStart, _etchEnd);
	}

	[Fact]
	public async Task ADiscardedRead_CountsAsNoFailure()
	{
		_store.ReadResult = MarkersTableMissing();
		StartAndEnterTheWait();
		Advance(_readInterval * 2);
		var heldRead = _store.HoldNextRead();
		_sync.ReadNow();

		await _sync.AddAsync(_etchStart.TimeUtc, _etchStart.Label);
		heldRead.SetResult(Stored(_etchStart));

		_store.ReadCount.Should().Be(4, "the read after the write failed as well");
		_messagePanel.Entries.Should().BeEmpty("the discarded read broke the run of failures");

		Advance(_readInterval);
		_messagePanel.Entries.Should().BeEmpty("two failures in a row stand since the discarded read");

		Advance(_readInterval);
		_messagePanel.Entries.Should().ContainSingle();
	}

	[Fact]
	public async Task AReadHeldAcrossAFailedWrite_Emits()
	{
		StartAndEnterTheWait();
		var heldRead = _store.HoldNextRead();
		_sync.ReadNow();
		_store.WriteResult = Result.Fail(FakeDataProvider.BenchArchiveError(ArchiveFault.MarkerGone, "Etch start"));

		(await _sync.MoveAsync(_etchStart, _etchEnd.TimeUtc)).IsFailed.Should().BeTrue();
		heldRead.SetResult(Stored(_etchStart));

		_reads.Should().HaveCount(2, "no write landed, so the held read is current, and the write asked for one more");
		_reads[0].Should().Equal(_etchStart);
	}

	[Fact]
	public async Task ASecondWrite_StartsOnlyAfterTheFirstLands()
	{
		var heldWrite = _store.HoldNextWrite();
		var firstMove = _sync.MoveAsync(_etchStart, _etchEnd.TimeUtc);
		var secondMove = _sync.MoveAsync(_etchStart, _etchStart.TimeUtc);

		_store.Writes.Should().ContainSingle();

		heldWrite.SetResult();
		await firstMove;
		await secondMove;

		_store.Writes.Should().Equal(
			new FakeMarkerWrite.Move(_etchStart, _etchEnd.TimeUtc),
			new FakeMarkerWrite.Move(_etchStart, _etchStart.TimeUtc));
	}

	[Fact]
	public async Task AFailedWrite_ReachesThePanelOnce_AndReturnsItsFailure()
	{
		var gone = FakeDataProvider.BenchArchiveError(ArchiveFault.MarkerGone, "Etch start");
		_store.WriteResult = Result.Fail(gone);

		var renamed = await _sync.RenameAsync(_etchStart, "Etch end");

		renamed.Errors.Should().ContainSingle().Which.Should().Be(gone);
		_messagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(gone));
	}

	[Fact]
	public async Task EveryWrite_EndsWithARead_WhateverItsOutcome()
	{
		StartAndEnterTheWait();

		await _sync.AddAsync(_etchStart.TimeUtc, _etchStart.Label);
		_store.ReadCount.Should().Be(1);

		_store.AddResult = Result.Fail<long>(FakeDataProvider.UnreachableArchive());
		await _sync.AddAsync(_etchEnd.TimeUtc, null);
		_store.ReadCount.Should().Be(2);

		await _sync.MoveAsync(_etchStart, _etchEnd.TimeUtc);
		await _sync.RenameAsync(_etchStart, null);
		await _sync.DeleteAsync(_etchStart);
		_store.ReadCount.Should().Be(5);
	}

	[Fact]
	public void TwoFailedReads_LogTheMarkerRead_TheThirdReportsOnce()
	{
		_store.ReadResult = MarkersTableMissing();
		StartAndEnterTheWait();

		Advance(_readInterval * 2);

		_messagePanel.Entries.Should().BeEmpty();
		_logger.Collector.GetSnapshot().Should().HaveCount(2)
			.And.AllSatisfy(record =>
			{
				record.Level.Should().Be(LogLevel.Warning);
				record.Message.Should().Contain("The marker read failed");
			});

		Advance(_readInterval);
		_messagePanel.Entries.Should().ContainSingle();
	}

	[Fact]
	public void ThreeUnreachableMarkerReads_AddNoEntry_AndLogEach()
	{
		_store.ReadResult = Result.Fail<IReadOnlyList<StoredMarker>>(FakeDataProvider.UnreachableArchive());
		StartAndEnterTheWait();

		Advance(_readInterval * 3);

		_store.ReadCount.Should().Be(3);
		_messagePanel.Entries.Should().BeEmpty("the catalogue read's state carries a lost connection to the bar");
		_logger.Collector.GetSnapshot().Should().HaveCount(3)
			.And.AllSatisfy(record => record.Level.Should().Be(LogLevel.Warning));
	}

	[Fact]
	public void DisposingDuringAHeldRead_CancelsTheReadAndReportsNothing()
	{
		StartAndEnterTheWait();
		var heldRead = _store.HoldNextRead();
		_sync.ReadNow();

		_sync.Dispose();

		heldRead.Task.IsCanceled.Should().BeTrue("the loop hands the read its stop token");
		_messagePanel.Entries.Should().BeEmpty("a window closing during a read is no failure");
		_logger.Collector.GetSnapshot().Should().NotContain(record => record.Level >= LogLevel.Warning);
		_reads.Should().BeEmpty();
	}

	// TestScheduler runs work scheduled for now one tick later, so the loop enters its first wait on that tick.
	private void StartAndEnterTheWait()
	{
		_sync.Start();
		Advance(_tick);
	}

	private void Advance(TimeSpan time)
	{
		_scheduler.AdvanceBy(time.Ticks);
	}

	private static Result<IReadOnlyList<StoredMarker>> Stored(params StoredMarker[] markers)
	{
		return Result.Ok<IReadOnlyList<StoredMarker>>(markers);
	}

	private static Result<IReadOnlyList<StoredMarker>> MarkersTableMissing()
	{
		return Result.Fail<IReadOnlyList<StoredMarker>>(
			FakeDataProvider.BenchArchiveError(ArchiveFault.TableMissing, "semiplot_markers"));
	}
}
