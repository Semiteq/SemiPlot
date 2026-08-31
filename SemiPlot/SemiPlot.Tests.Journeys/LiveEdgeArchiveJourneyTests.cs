using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tests.Data.Integration;
using SemiPlot.Tools.ArchiveSeeder;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Journeys;

// The other half of the archive's story. BreakRenderArchiveJourneyTests proves history reaches the canvas;
// this one proves a row written after the application is already running reaches the chart's live edge, and
// reaches it once. RealtimeSubscriptionTests asserts the same rule over the provider alone — the composed
// path adds TrendCoordinator's buffering and folding, TrendChartViewModel's applier and the navigation
// controller, each of which can lose a sample or replay one without the provider noticing.
//
// This class appends, so it never takes SeededArchive, whose contract is that the class leaves the database
// as it found it. It clones the seeded template rather than the provisioned source, because the chart it
// drives is opened over seeded history.
//
// Nothing here waits on a timeout. Every synchronisation point is an awaited emission or an awaited write:
// the Connected state a subscription's first successful tick reports, then a batch that has been delivered.
// That order is load-bearing rather than tidy. A subscription can never emit a row that already existed when
// it subscribed, so a test appending before the baseline read has run is racing it, and the losing side of
// that race is an await that never returns — a hung xunit v3 executable that locks the next build with
// MSB3027/MSB3021.
[Collection(ArchiveJourneyCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Integration")]
public sealed class LiveEdgeArchiveJourneyTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Template)
{
	private static readonly ArchiveTimeConverter _timeConverter = new(ArchiveProviderFactory.SourceTimeZone);

	[AvaloniaFact]
	public async Task ARowWrittenAfterStartupReachesTheChartOnceAndMovesItsLiveEdge()
	{
		Fixture.RequireAvailable();

		await using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);
		var dataProvider = services.GetRequiredService<IDataProvider>();
		var dataScheduler = services.GetRequiredService<IScheduler>();
		var catalogue = await dataProvider.QueryPensAsync();
		var extent = await dataProvider.QueryArchiveExtentAsync();

		catalogue.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(catalogue));
		extent.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(extent));
		extent.Value.IsEmpty.Should().BeFalse("the live edge is asserted against the archive's own last row");

		var seededLastUtc = extent.Value.LastUtc;
		var seededLastLocal = _timeConverter.ToArchiveLocal(seededLastUtc);
		var firstAppendLocal = seededLastLocal.AddSeconds(1);
		var secondAppendLocal = seededLastLocal.AddSeconds(2);
		var firstAppendUtc = _timeConverter.ToUtc(firstAppendLocal);
		var secondAppendUtc = _timeConverter.ToUtc(secondAppendLocal);

		using var coordinator = new TrendCoordinator(
			dataProvider, catalogue.Value, dataScheduler, ImmediateScheduler.Instance);

		// Before the view model, because its constructor takes the first RefCount subscription on
		// RealtimeBatches and that is what starts the poll. A gate opened after the poll's first tick would
		// have nothing left to wait for and would never complete.
		using var armed = new ArmedGate(coordinator.ConnectionFaults);
		using var chart = new TrendChartViewModel(
			coordinator, dataScheduler, ImmediateScheduler.Instance, NullLogger<TrendChartViewModel>.Instance);
		using var batches = new BatchCollector(coordinator.RealtimeBatches);

		foreach (var pen in catalogue.Value)
		{
			chart.AddPen(pen);
		}

		chart.Navigation.SeedFromArchiveExtent(extent.Value);
		coordinator.Start();

		var historyApplied = chart.HistoryApplied.FirstAsync().ToTask();
		chart.RequestInitialHistory();
		await historyApplied;

		chart.Navigation.ActiveLayer.Should().Be(
			AggregationLayer.Raw,
			"only the raw layer appends a realtime sample as a point of its own — a coarse layer folds it "
			+ "into the last column instead, and the live-edge assertions below read the appended point");
		chart.Navigation.IsSticky.Should().BeTrue("a window that does not follow the edge cannot be moved by it");
		chart.Navigation.To.Should().Be(seededLastUtc, "the window opens on the archive's own last sample");

		await armed.Reached;

		// The gate is taken before the write and awaited after it, so the delivery cannot slip between them.
		var firstDelivery = batches.NextBatch();

		await AppendAsync(firstAppendLocal, catalogue.Value, tick: 1);
		await firstDelivery;

		var afterTheFirstWrite = batches.Batches;

		afterTheFirstWrite.Should().HaveCount(
			1,
			"one write of one row per variable is one batch: a second batch here is the same row delivered "
			+ "twice");
		AssertCarries(afterTheFirstWrite[0], firstAppendUtc, catalogue.Value, tick: 1);
		firstAppendUtc.Should().BeAfter(
			seededLastUtc, "a delivered sample must be newer than everything the archive already held");

		var secondDelivery = batches.NextBatch();

		await AppendAsync(secondAppendLocal, catalogue.Value, tick: 2);
		await secondDelivery;

		var afterTheSecondWrite = batches.Batches;

		afterTheSecondWrite.Should().HaveCount(2);
		AssertCarries(afterTheSecondWrite[1], secondAppendUtc, catalogue.Value, tick: 2);

		// The monotonic lastSeen rule, read from the consumer's side: two writes, two timestamps, in order
		// and each of them once. A poll binding its lower bound inclusively would repeat the first here.
		afterTheSecondWrite
			.SelectMany(batch => batch.Timestamps)
			.Should().Equal([firstAppendUtc, secondAppendUtc]);

		chart.Navigation.To.Should().Be(
			secondAppendUtc,
			"ChartRealtimeApplier hands the last timestamp of a batch to ChartNavigationController.OnLiveEdge, "
			+ "so the whole path down to the axis is covered rather than the pen states alone");
		chart.Pens.Should().AllSatisfy(pen => pen.CurrentValue.Should().Be(ValueFor(pen.Pen.PenId, tick: 2)));
	}

	// The shape the retired stub could not produce and the archive produces all the time: it is
	// per-variable and change-based with a deadband, so each variable carries its own t and one poll tick
	// into one buffer window spans as many distinct timestamps as there are variables. A pen with no sample
	// at a timestamp must be left alone there — appending it as a null would draw a break the archive never
	// recorded, and TrendPenState encodes a null as the NaN that is the gap column.
	[AvaloniaFact]
	public async Task RowsOnAVariableOfTheirOwnReachTheChartWithoutBreakingAnyPen()
	{
		Fixture.RequireAvailable();

		await using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);
		var dataProvider = services.GetRequiredService<IDataProvider>();
		var dataScheduler = services.GetRequiredService<IScheduler>();
		var catalogue = await dataProvider.QueryPensAsync();
		var extent = await dataProvider.QueryArchiveExtentAsync();

		catalogue.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(catalogue));
		extent.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(extent));
		catalogue.Value.Count.Should().BeGreaterThan(
			1, "a timestamp only one pen sampled needs a second pen to be absent from it");

		var seededLastLocal = _timeConverter.ToArchiveLocal(extent.Value.LastUtc);

		using var coordinator = new TrendCoordinator(
			dataProvider, catalogue.Value, dataScheduler, ImmediateScheduler.Instance);

		using var armed = new ArmedGate(coordinator.ConnectionFaults);
		using var chart = new TrendChartViewModel(
			coordinator, dataScheduler, ImmediateScheduler.Instance, NullLogger<TrendChartViewModel>.Instance);
		using var batches = new BatchCollector(coordinator.RealtimeBatches);

		foreach (var pen in catalogue.Value)
		{
			chart.AddPen(pen);
		}

		chart.Navigation.SeedFromArchiveExtent(extent.Value);
		coordinator.Start();

		var historyApplied = chart.HistoryApplied.FirstAsync().ToTask();
		chart.RequestInitialHistory();
		await historyApplied;

		chart.Navigation.ActiveLayer.Should().Be(
			AggregationLayer.Raw, "a coarse layer folds a realtime sample instead of appending a point");

		await armed.Reached;

		var delivery = batches.NextBatch();

		await AppendStaggeredAsync(seededLastLocal, catalogue.Value, tick: 1);
		await delivery;

		// One COPY is one transaction, so the whole set becomes visible to a single poll tick and reaches
		// the coordinator as one buffer window.
		var batch = batches.Batches.Should().ContainSingle().Subject;

		batch.Timestamps.Should().HaveCount(catalogue.Value.Count);
		batch.Pens.Should().AllSatisfy(values => values.TimestampsUtc.Should().ContainSingle());
		batch.Pens.SelectMany(values => values.TimestampsUtc).Should().OnlyHaveUniqueItems();

		chart.Pens.Should().AllSatisfy(pen =>
		{
			pen.CenterPoints.Should().NotContain(point => double.IsNaN(point.Y));
			pen.CurrentValue.Should().Be(ValueFor(pen.Pen.PenId, tick: 1));
		});
	}

	// One row per variable, written through the appending path ArchiveWriter takes for a follow run: the
	// archive already carries the template's rows, so the seeded refusal has to be stood down. The day
	// partition the rows fall into already exists, and CreateStatement's IF NOT EXISTS passes through it.
	private async Task AppendAsync(DateTime archiveLocal, IReadOnlyList<Pen> pens, int tick)
	{
		var rows = pens
			.Select(pen => new ArchiveRow(
				pen.PenId,
				ArchiveRow.RawLayer,
				archiveLocal,
				ValueFor(pen.PenId, tick),
				ArchiveRow.OrdinaryQuality))
			.ToArray();

		var written = await Writer()
			.WriteAsync(rows, archiveLocal, archiveLocal.AddSeconds(1), allowExistingRows: true);

		written.IsSuccess.Should().BeTrue(string.Join("; ", written.Errors.Select(error => error.Message)));
		written.Value.Should().Be(rows.Length);
	}

	// One row per variable, each on a second of its own, so no two variables share a timestamp. Written in
	// one COPY, which is one transaction: the whole set becomes visible to the poll at the same instant.
	private async Task AppendStaggeredAsync(DateTime baseLocal, IReadOnlyList<Pen> pens, int tick)
	{
		var rows = pens
			.Select((pen, index) => new ArchiveRow(
				pen.PenId,
				ArchiveRow.RawLayer,
				baseLocal.AddSeconds(index + 1),
				ValueFor(pen.PenId, tick),
				ArchiveRow.OrdinaryQuality))
			.ToArray();

		var written = await Writer().WriteAsync(
			rows,
			baseLocal.AddSeconds(1),
			baseLocal.AddSeconds(pens.Count + 1),
			allowExistingRows: true);

		written.IsSuccess.Should().BeTrue(string.Join("; ", written.Errors.Select(error => error.Message)));
		written.Value.Should().Be(rows.Length);
	}

	private static void AssertCarries(RealtimeBatch batch, DateTime timestampUtc, IReadOnlyList<Pen> pens, int tick)
	{
		batch.Timestamps.Should().Equal([timestampUtc]);
		batch.Pens.Select(values => values.PenId).Should().BeEquivalentTo(pens.Select(pen => pen.PenId));
		batch.Pens.Should().AllSatisfy(values => values.Values.Should().Equal([ValueFor(values.PenId, tick)]));
	}

	// Distinct per variable and per write, so a batch carrying the right timestamp with somebody else's
	// value still fails.
	private static double ValueFor(int penId, int tick)
	{
		return penId + (tick * 0.25);
	}

	// Completed from the poll's own thread, inside the connection stream's notification. It runs its
	// continuation asynchronously because an inline one would resume the test body on that thread, off the
	// Avalonia dispatcher this test builds ScottPlot and view-model state on.
	private sealed class ArmedGate : IDisposable
	{
		private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);

		private readonly IDisposable _subscription;

		public ArmedGate(IObservable<ArchiveConnectionState> states)
		{
			_subscription = states.Subscribe(state =>
			{
				if (state.IsConnected)
				{
					_reached.TrySetResult();
				}
			});
		}

		public Task Reached => _reached.Task;

		public void Dispose()
		{
			_subscription.Dispose();
		}
	}

	// Collects every delivered batch and hands out a gate for the next one. One subscription for the whole
	// test rather than one per await: a repeat arriving between two awaits is still recorded, so the
	// exactly-once assertion runs over everything that arrived rather than over the batches the test
	// happened to be listening for.
	private sealed class BatchCollector : IDisposable
	{
		private readonly List<RealtimeBatch> _batches = [];

		private readonly Lock _guard = new();

		private readonly IDisposable _subscription;

		private TaskCompletionSource? _nextBatch;

		public BatchCollector(IObservable<RealtimeBatch> batches)
		{
			_subscription = batches.Subscribe(Collect);
		}

		public IReadOnlyList<RealtimeBatch> Batches
		{
			get
			{
				lock (_guard)
				{
					return _batches.ToArray();
				}
			}
		}

		public Task NextBatch()
		{
			lock (_guard)
			{
				var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

				_nextBatch = gate;

				return gate.Task;
			}
		}

		public void Dispose()
		{
			_subscription.Dispose();
		}

		private void Collect(RealtimeBatch batch)
		{
			lock (_guard)
			{
				_batches.Add(batch);

				_nextBatch?.TrySetResult();
			}
		}
	}
}
