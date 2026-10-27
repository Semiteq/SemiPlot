using System.Reactive.Concurrency;
using System.Reactive.Linq;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.Tools.ArchiveSeeder;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Messages;

using Xunit;

namespace SemiPlot.Tests.Integration.Journeys;

// Proves a row written while the app runs reaches the chart's live edge exactly once, composing
// TrendCoordinator, TrendChartViewModel and navigation over RealtimeSubscriptionTests' live source rule.
// Clones the template because this class appends; every wait is an awaited emission bounded by GateBound.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Integration")]
public sealed class LiveEdgeArchiveJourneyTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Template)
{
	[AvaloniaFact]
	public async Task ARowWrittenAfterStartupReachesTheChartOnceAndMovesItsLiveEdge()
	{
		await using var stand = await OpenStandAsync();
		var catalogue = stand.Catalogue;
		var chart = stand.Chart;
		var batches = stand.Batches;

		stand.Extent.IsEmpty.Should().BeFalse("the live edge is asserted against the archive's own last row");

		var seededLastUtc = stand.Extent.LastUtc;
		var seededLastLocal = ArchiveProviderFactory.TimeConverter.ToArchiveLocal(seededLastUtc);
		var firstAppendLocal = seededLastLocal.AddSeconds(1);
		var secondAppendLocal = seededLastLocal.AddSeconds(2);
		var firstAppendUtc = ArchiveProviderFactory.TimeConverter.ToUtc(firstAppendLocal);
		var secondAppendUtc = ArchiveProviderFactory.TimeConverter.ToUtc(secondAppendLocal);

		chart.Navigation.IsSticky.Should().BeTrue("a window that does not follow the edge cannot be moved by it");
		chart.Navigation.NewestSample.Should().Be(
			seededLastUtc, "the navigation opens on the archive's own last sample");

		// The gate is taken before the write and awaited after it, so the delivery cannot slip between them.
		var firstDelivery = batches.NextBatch();

		await AppendAsync(firstAppendLocal, catalogue, tick: 1);
		await firstDelivery.WaitAsync(GateBound, TestContext.Current.CancellationToken);

		var afterTheFirstWrite = batches.Batches;

		afterTheFirstWrite.Should().HaveCount(
			1,
			"one write of one row per variable is one batch: a second batch here is the same row delivered "
			+ "twice");
		AssertCarries(afterTheFirstWrite[0], firstAppendUtc, catalogue, tick: 1);

		var secondDelivery = batches.NextBatch();

		await AppendAsync(secondAppendLocal, catalogue, tick: 2);
		await secondDelivery.WaitAsync(GateBound, TestContext.Current.CancellationToken);

		var afterTheSecondWrite = batches.Batches;

		afterTheSecondWrite.Should().HaveCount(2);
		AssertCarries(afterTheSecondWrite[1], secondAppendUtc, catalogue, tick: 2);

		// The monotonic lastSeen rule, read from the consumer's side: two writes, two timestamps, in order
		// and each of them once. A poll binding its lower bound inclusively would repeat the first here.
		afterTheSecondWrite
			.SelectMany(batch => batch.Timestamps)
			.Should().Equal([firstAppendUtc, secondAppendUtc]);

		chart.Navigation.NewestSample.Should().Be(
			secondAppendUtc,
			"ChartRealtimeApplier hands the last timestamp of a batch to ChartNavigationController.OnLiveEdge, "
			+ "so the whole path down to the navigation is covered rather than the pen states alone");
		chart.Navigation.To.Should().BeOnOrAfter(secondAppendUtc, "a sticky window follows the poll's answer");
		chart.Pens.Should().AllSatisfy(pen => pen.CurrentValue.Should().Be(ValueFor(pen.Pen.PenId, tick: 2)));
	}

	[AvaloniaFact]
	public async Task ARowMarkingTheLastSampleBeforeABreakReachesTheChartAsItsValueAndThenABreak()
	{
		await using var stand = await OpenStandAsync();
		var pen = stand.Catalogue[0];
		var seededLastLocal = ArchiveProviderFactory.TimeConverter.ToArchiveLocal(stand.Extent.LastUtc);
		var appendLocal = seededLastLocal.AddSeconds(1);
		var appendUtc = ArchiveProviderFactory.TimeConverter.ToUtc(appendLocal);
		var value = ValueFor(pen.PenId, tick: 1);
		var delivery = stand.Batches.NextBatch();

		await AppendAsync(appendLocal, [pen], tick: 1, ArchiveRow.LastBeforeBreakQuality);
		await delivery.WaitAsync(GateBound, TestContext.Current.CancellationToken);

		// The batch is what the poll delivered, which no history read can race.
		var batch = stand.Batches.Batches.Should().ContainSingle().Subject;
		var breakUtc = appendUtc.AddMilliseconds(1);

		batch.Timestamps.Should().Equal([appendUtc, breakUtc]);
		var penValues = batch.Pens.Should().ContainSingle().Subject;
		penValues.PenId.Should().Be(pen.PenId);
		penValues.Values.Should().Equal([value, null]);
		stand.Chart.Navigation.NewestSample.Should().Be(breakUtc, "the break is the batch's last timestamp");

		var state = stand.Chart.Pens.Should().ContainSingle(candidate => candidate.Pen.PenId == pen.PenId).Subject;
		var columns = state.Line.SnapshotColumns();

		// A landed history read may add a NaN ahead of the break:
		// docs/architecture/charting.md#per-pen-plottable-envelopeline
		var lastRealColumn = columns.Last(column => !double.IsNaN(column.Center));

		lastRealColumn.X.Should().Be(LocalTimeAxis.ToAxis(appendUtc));
		lastRealColumn.Center.Should().Be(value);
		columns[^1].X.Should().Be(LocalTimeAxis.ToAxis(breakUtc));
		double.IsNaN(columns[^1].Center).Should().BeTrue("the row marks the last sample before a break");
		state.CurrentValue.Should().Be(value);
	}

	// The archive is per-variable and change-based with a deadband, so one poll tick spans as many distinct
	// timestamps as there are variables; a pen with no sample at a timestamp is left alone rather than
	// appended as null, which would draw a break the archive never recorded.
	[AvaloniaFact]
	public async Task RowsOnAVariableOfTheirOwnReachTheChartWithoutBreakingAnyPen()
	{
		await using var stand = await OpenStandAsync();
		var catalogue = stand.Catalogue;
		var chart = stand.Chart;

		catalogue.Count.Should().BeGreaterThan(
			1, "a timestamp only one pen sampled needs a second pen to be absent from it");

		var seededLastLocal = ArchiveProviderFactory.TimeConverter.ToArchiveLocal(stand.Extent.LastUtc);
		var delivery = stand.Batches.NextBatch();

		await AppendStaggeredAsync(seededLastLocal, catalogue, tick: 1);
		await delivery.WaitAsync(GateBound, TestContext.Current.CancellationToken);

		// One COPY is one transaction, so the whole set becomes visible to a single poll tick and reaches
		// the coordinator as one buffer window.
		var batch = stand.Batches.Batches.Should().ContainSingle().Subject;

		batch.Timestamps.Should().HaveCount(catalogue.Count);
		batch.Pens.Should().AllSatisfy(values => values.TimestampsUtc.Should().ContainSingle());
		batch.Pens.SelectMany(values => values.TimestampsUtc).Should().OnlyHaveUniqueItems();

		chart.Pens.Should().AllSatisfy(pen =>
		{
			pen.Line.SnapshotColumns().Should().NotContain(column => double.IsNaN(column.Center));
			pen.CurrentValue.Should().Be(ValueFor(pen.Pen.PenId, tick: 1));
		});
	}

	// Only the raw layer appends a realtime value as a point of its own; a coarse layer folds a value
	// into the last column instead, and the assertions read the appended point.
	private async Task<LiveEdgeStand> OpenStandAsync()
	{
		var armed = new ArmedGate();
		var services = ArchiveProviderFactory.Build(Database.PlotConnectionString, armed);
		LiveEdgeStand? stand = null;

		try
		{
			var dataProvider = services.GetRequiredService<IDataProvider>();
			var liveSource = services.GetRequiredService<ILiveSource>();
			var dataScheduler = services.GetRequiredService<IScheduler>();
			var catalogue = await dataProvider.QueryPensAsync();
			var extent = await dataProvider.QueryArchiveExtentAsync();

			catalogue.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(catalogue));
			extent.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(extent));

			var coordinator = new TrendCoordinator(
				dataProvider, liveSource, catalogue.Value, dataScheduler, ImmediateScheduler.Instance);

			var messagePanel = new MessagePanelViewModel();
			var chart = new TrendChartViewModel(
				coordinator,
				new NoMarkers(),
				dataScheduler,
				ImmediateScheduler.Instance,
				TimeZoneInfo.Utc,
				messagePanel,
				NullLogger<TrendChartViewModel>.Instance);
			var batches = new BatchCollector<RealtimeBatch>(
				coordinator.RealtimeBatches.Where(batch => batch.Timestamps.Count > 0));
			stand = new LiveEdgeStand(
				services, coordinator, armed, chart, messagePanel, batches, catalogue.Value, extent.Value);

			chart.ApplyCatalogue(catalogue.Value);

			chart.Navigation.SeedFromArchiveExtent(extent.Value);

			var scalesBefore = chart.ScalesRevision;
			chart.RequestInitialHistory();
			await JourneyWait.Until(() => chart.ScalesRevision > scalesBefore);

			// A failed history read also bumps ScalesRevision, so the wait alone does not prove the read landed.
			messagePanel.HasEntries.Should().BeFalse("the initial history read has to succeed");

			chart.Navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);

			await armed.Armed(1).WaitAsync(GateBound, TestContext.Current.CancellationToken);

			return stand;
		}
		catch
		{
			if (stand is null)
			{
				await services.DisposeAsync();
				armed.Dispose();
			}
			else
			{
				await stand.DisposeAsync();
			}

			throw;
		}
	}

	// One row per variable, written through the appending path ArchiveWriter takes for a follow run: the
	// archive already carries the template's rows, so the seeded refusal has to be stood down. The day
	// partition the rows fall into already exists, and CreateStatement's IF NOT EXISTS passes through it.
	private async Task AppendAsync(
		DateTime archiveLocal,
		IReadOnlyList<Pen> pens,
		int tick,
		int quality = ArchiveRow.OrdinaryQuality)
	{
		var rows = pens
			.Select(pen => new ArchiveRow(
				pen.PenId,
				ArchiveRow.RawLayer,
				archiveLocal,
				ValueFor(pen.PenId, tick),
				quality))
			.ToArray();

		var written = await Writer()
			.WriteAsync(rows, archiveLocal, archiveLocal.AddSeconds(1), allowExistingRows: true);

		written.Should().Be(rows.Length);
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

		written.Should().Be(rows.Length);
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

	private sealed class LiveEdgeStand(
		ServiceProvider services,
		TrendCoordinator coordinator,
		ArmedGate armed,
		TrendChartViewModel chart,
		MessagePanelViewModel messagePanel,
		BatchCollector<RealtimeBatch> batches,
		IReadOnlyList<Pen> catalogue,
		ArchiveExtent extent) : IAsyncDisposable
	{
		public TrendChartViewModel Chart => chart;

		public BatchCollector<RealtimeBatch> Batches => batches;

		public IReadOnlyList<Pen> Catalogue => catalogue;

		public ArchiveExtent Extent => extent;

		public async ValueTask DisposeAsync()
		{
			batches.Dispose();
			chart.Dispose();
			messagePanel.Dispose();
			coordinator.Dispose();

			await services.DisposeAsync();
			armed.Dispose();
		}
	}
}
