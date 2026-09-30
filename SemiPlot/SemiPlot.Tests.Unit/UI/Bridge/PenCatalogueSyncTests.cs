using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

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

	private readonly TestScheduler _scheduler = new();
	private readonly FakeDataProvider _provider;
	private readonly MessagePanelViewModel _messagePanel = new();
	private readonly PenCatalogueSync _sync;
	private readonly List<PenListDelta> _deltas = [];

	public PenCatalogueSyncTests()
	{
		_provider = new FakeDataProvider(_scheduler, TimeSpan.FromSeconds(1));
		_sync = new PenCatalogueSync(
			_provider, _provider.Pens, _messagePanel, _scheduler, NullLogger<PenCatalogueSync>.Instance);
		_sync.Deltas.Subscribe(_deltas.Add);
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

		Advance(PenCatalogueSync.ReadInterval - _tick);
		_provider.PensQueryCount.Should().Be(0);

		Advance(_tick);
		_provider.PensQueryCount.Should().Be(1);

		Advance(PenCatalogueSync.ReadInterval);
		_provider.PensQueryCount.Should().Be(2);
	}

	[Fact]
	public void NoRead_StartsWhileOneIsHeld()
	{
		_provider.GatePens = true;
		StartAndEnterTheWait();
		Advance(PenCatalogueSync.ReadInterval);

		Advance(PenCatalogueSync.ReadInterval * 4);
		_provider.PensQueryCount.Should().Be(1);

		_provider.PensGate.SetResult(Result.Ok(_provider.Pens));
		Advance(PenCatalogueSync.ReadInterval - _tick);
		_provider.PensQueryCount.Should().Be(1, "the wait after a read is a full interval");

		Advance(_tick);
		_provider.PensQueryCount.Should().Be(2);
	}

	[Fact]
	public void ReadNow_DuringAHeldRead_GivesExactlyOneReadAfterIt()
	{
		_provider.GatePens = true;
		StartAndEnterTheWait();
		Advance(PenCatalogueSync.ReadInterval);

		_sync.ReadNow();
		_sync.ReadNow();
		_provider.PensQueryCount.Should().Be(1);

		_provider.PensGate.SetResult(Result.Ok(_provider.Pens));
		_provider.PensQueryCount.Should().Be(2);

		Advance(PenCatalogueSync.ReadInterval - _tick);
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

		Advance(PenCatalogueSync.ReadInterval - _tick);
		_provider.PensQueryCount.Should().Be(1);

		Advance(_tick);
		_provider.PensQueryCount.Should().Be(2);
	}

	[Fact]
	public void AnUnchangedRead_EmitsNoDelta()
	{
		StartAndEnterTheWait();

		Advance(PenCatalogueSync.ReadInterval * 2);

		_provider.PensQueryCount.Should().Be(2);
		_deltas.Should().BeEmpty();
	}

	[Fact]
	public void AChangedRead_EmitsItsDeltaOnce_AndBecomesTheBaseline()
	{
		StartAndEnterTheWait();
		_provider.Pens = [Renamed(_provider.Pens[0]), _provider.Pens[1]];

		Advance(PenCatalogueSync.ReadInterval * 2);

		_deltas.Should().ContainSingle()
			.Which.Revised.Should().ContainSingle()
			.Which.Should().Be(_provider.Pens[0]);
	}

	[Fact]
	public void TwoFailedReads_ReportNothing_TheThirdReportsOnce_AndTheNextSuccessCarriesEverythingSince()
	{
		var stored = _provider.Pens;
		_provider.FailPens = true;
		StartAndEnterTheWait();

		Advance(PenCatalogueSync.ReadInterval * 2);
		_messagePanel.Entries.Should().BeEmpty("one reconnect after a server restart must not open the panel");

		Advance(PenCatalogueSync.ReadInterval);
		_messagePanel.Entries.Should().ContainSingle();

		_provider.FailPens = false;
		_provider.Pens = [Renamed(stored[0]), stored[1] with { Color = "#0000ff" }];
		Advance(PenCatalogueSync.ReadInterval);

		_deltas.Should().ContainSingle()
			.Which.Revised.Select(pen => pen.PenId).Should().Equal(1, 2);
	}

	[Fact]
	public void ASuccess_ResetsTheFailureCount()
	{
		_provider.FailPens = true;
		StartAndEnterTheWait();
		Advance(PenCatalogueSync.ReadInterval * 2);

		_provider.FailPens = false;
		Advance(PenCatalogueSync.ReadInterval);
		_provider.FailPens = true;
		Advance(PenCatalogueSync.ReadInterval * 2);

		_messagePanel.Entries.Should().BeEmpty("no third failure in a row has happened");
	}

	[Fact]
	public void ASubscriberThatThrows_AddsOnePanelEntry_AndTheNextReadStillRuns()
	{
		using var throwing = _sync.Deltas.Subscribe(_ => throw new InvalidOperationException("the subscriber threw"));
		StartAndEnterTheWait();
		_provider.Pens = [Renamed(_provider.Pens[0]), _provider.Pens[1]];

		Advance(PenCatalogueSync.ReadInterval);
		_messagePanel.Entries.Should().ContainSingle();

		_provider.Pens = [_provider.Pens[0], Renamed(_provider.Pens[1])];
		Advance(PenCatalogueSync.ReadInterval);

		_provider.PensQueryCount.Should().Be(2);
		_deltas.Should().HaveCount(2, "the loop survived the throw and emitted the next change");
		_deltas[1].Revised.Select(pen => pen.PenId).Should().Equal(
			[2], "the read the subscriber threw on is the baseline all the same");
	}

	[Fact]
	public void AReadThatThrows_AddsOnePanelEntryAtOnce_AndTheNextReadComesOneIntervalLater()
	{
		_provider.FailPens = true;
		StartAndEnterTheWait();
		Advance(PenCatalogueSync.ReadInterval * 2);
		_provider.FailPens = false;
		_provider.PensReadException = new InvalidOperationException("the read threw");

		Advance(PenCatalogueSync.ReadInterval);
		_messagePanel.Entries.Should().ContainSingle()
			.Which.View.Detail.Should().Contain("the read threw");

		_provider.PensReadException = null;
		_provider.FailPens = true;
		Advance(PenCatalogueSync.ReadInterval - _tick);
		_provider.PensQueryCount.Should().Be(3);

		Advance(_tick);
		_provider.PensQueryCount.Should().Be(4);
		_messagePanel.Entries.Should().HaveCount(2, "the throw left two failed reads in a row standing");
	}

	// The third failure in a row lands after disposal, so only the stop check keeps it out of the panel.
	[Fact]
	public void Dispose_DuringAHeldRead_ReportsNothingWhenTheReadLands()
	{
		_provider.FailPens = true;
		StartAndEnterTheWait();
		Advance(PenCatalogueSync.ReadInterval * 2);
		_provider.FailPens = false;
		_provider.GatePens = true;
		Advance(PenCatalogueSync.ReadInterval);

		_sync.Dispose();
		_provider.PensGate.SetResult(Result.Fail<IReadOnlyList<Pen>>("the held read failed"));

		_deltas.Should().BeEmpty();
		_messagePanel.Entries.Should().BeEmpty();
	}

	[Fact]
	public void Dispose_StopsTheReads()
	{
		StartAndEnterTheWait();
		Advance(PenCatalogueSync.ReadInterval);

		_sync.Dispose();
		_sync.ReadNow();
		Advance(TimeSpan.FromMinutes(1));

		_provider.PensQueryCount.Should().Be(1);
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

	private static Pen Renamed(Pen pen)
	{
		return pen with { Name = pen.Name + " renamed" };
	}
}
