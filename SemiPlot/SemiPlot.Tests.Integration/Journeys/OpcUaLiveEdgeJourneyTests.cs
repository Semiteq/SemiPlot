using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;

using ReactiveUI.Avalonia;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tests.Integration.DemoScada;
using SemiPlot.Tests.Integration.OpcUa;
using SemiPlot.Tools.ArchiveSeeder;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Messages;

using Xunit;

namespace SemiPlot.Tests.Integration.Journeys;

// A demo server, an OpcUaLiveSource over it and a history fake ending a minute before the server's start, opened
// as TrendWindow opens the chart. The UI scheduler is the dispatcher, so history and live batches land in order.
[Collection(DemoScadaCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Integration")]
public sealed class OpcUaLiveEdgeJourneyTests(OpcUaLiveEdgeJourneyTests.ServerFixture fixture)
	: IClassFixture<OpcUaLiveEdgeJourneyTests.ServerFixture>
{
	private const int TestTimeoutMilliseconds = 180000;
	private const int HistoryColumnCount = 5;
	private const int LiveValueFloor = 3;

	private static readonly TimeSpan _historyLead = TimeSpan.FromMinutes(1);
	private static readonly TimeSpan _historyStep = TimeSpan.FromMinutes(1);
	private static readonly TimeSpan _aheadBreakStep = TimeSpan.FromMilliseconds(1);

	[AvaloniaFact(Timeout = TestTimeoutMilliseconds)]
	public async Task AServedValue_ReachesTheChartBehindABreak_AndMovesTheStickyWindowToTheNewestStamp()
	{
		var historyEndUtc = fixture.StartFloorUtc - _historyLead;
		var catalogue = Catalogue();
		var history = new FixedHistory(historyEndUtc);
		await using var live = new LiveSourceHost(LiveSourceHost.PortOf(fixture.Server));
		using var coordinator = new TrendCoordinator(
			history, live.Source, catalogue, DefaultScheduler.Instance, AvaloniaScheduler.Instance);
		using var chart = new TrendChartViewModel(
			coordinator,
			new NoMarkers(),
			DefaultScheduler.Instance,
			AvaloniaScheduler.Instance,
			TimeZoneInfo.Utc,
			new MessagePanelViewModel(),
			NullLogger<TrendChartViewModel>.Instance);
		DateTime? newestDeliveredUtc = null;
		using var newestTap = coordinator.RealtimeBatches.Subscribe(batch =>
			newestDeliveredUtc = Later(newestDeliveredUtc, batch.Timestamps[^1]));

		chart.Navigation.SeedFromArchiveExtent(history.Extent);
		chart.ApplyCatalogue(catalogue);
		chart.RequestInitialHistory();
		chart.Navigation.ActiveLayer.Should().Be(
			AggregationLayer.Raw, "a raw window appends each value as its own column");

		await WaitUntil(() => chart.Pens.All(HoldsTheHistoryAndABreak), "the history and a break", live);
		await WaitUntil(() => chart.Pens.All(HoldsLiveValues), "live values behind the break", live);

		foreach (var pen in chart.Pens)
		{
			AssertTheLine(pen, history.Columns, historyEndUtc, Because(live));
		}

		chart.Navigation.IsSticky.Should().BeTrue(Because(live));
		chart.Navigation.To.Should().Be(
			newestDeliveredUtc,
			"the sticky window follows the newest stamp the live edge delivered; {0}",
			Because(live));
		chart.Navigation.To.Should().BeAfter(historyEndUtc, Because(live));
	}

	private static IReadOnlyList<Pen> Catalogue()
	{
		// Every pen visible, the hidden-on-start one included, so each one is read for its history.
		return [.. RawLayerGenerator.SelectPens(ServerFixture.Options.PenCount)
			.Select(pen => new Pen(
				pen.PenId,
				pen.Name,
				pen.Groups,
				pen.Color,
				pen.Unit,
				pen.Format,
				EnabledOnStart: true,
				LineStyle: pen.LineStyle))];
	}

	private static DateTime Later(DateTime? newest, DateTime candidate)
	{
		return newest is { } known && known >= candidate ? known : candidate;
	}

	private static bool HoldsTheHistoryAndABreak(TrendPenState pen)
	{
		var columns = pen.Line.SnapshotColumns();

		return columns.Count > HistoryColumnCount && double.IsNaN(columns[HistoryColumnCount].Center);
	}

	private static bool HoldsLiveValues(TrendPenState pen)
	{
		return pen.Line.SnapshotColumns().Count >= HistoryColumnCount + 1 + LiveValueFloor;
	}

	private static void AssertTheLine(
		TrendPenState pen,
		IReadOnlyList<EnvelopeColumn> historyColumns,
		DateTime historyEndUtc,
		string because)
	{
		var penId = pen.Pen.PenId;
		var columns = pen.Line.SnapshotColumns();
		var aheadBreak = columns[HistoryColumnCount];
		var liveColumns = columns.Skip(HistoryColumnCount + 1).ToArray();
		var converter = new ArchiveTimeConverter(DemoScadaHost.ArchiveZone);

		columns.Take(HistoryColumnCount).Should().Equal(historyColumns, "pen {0}; {1}", penId, because);
		double.IsNaN(aheadBreak.Center).Should().BeTrue("pen {0}; {1}", penId, because);
		(LocalTimeAxis.FromAxis(liveColumns[0].X) - LocalTimeAxis.FromAxis(aheadBreak.X)).Should().Be(
			_aheadBreakStep,
			"the chart writes its break 1 ms ahead of the pen's first live column; pen {0}; {1}",
			penId,
			because);

		foreach (var column in liveColumns)
		{
			var stampUtc = LocalTimeAxis.FromAxis(column.X);
			var expected = LiveTailGenerator
				.ChangesAt(ServerFixture.Options, converter.ToArchiveLocal(stampUtc))
				.Single(row => row.Id == penId);

			stampUtc.Should().BeAfter(historyEndUtc, "pen {0}; {1}", penId, because);
			column.Center.Should().Be(expected.Value, "pen {0} at {1:O}; {2}", penId, stampUtc, because);
			column.Min.Should().Be(column.Center, "pen {0}; {1}", penId, because);
			column.Max.Should().Be(column.Center, "pen {0}; {1}", penId, because);
		}
	}

	private async Task WaitUntil(Func<bool> condition, string awaited, LiveSourceHost live)
	{
		await FluentActions.Awaiting(() => JourneyWait.Until(condition))
			.Should().NotThrowAsync("the chart should hold {0}; {1}", awaited, Because(live));
	}

	private string Because(LiveSourceHost live)
	{
		return $"{live.Describe()}; {fixture.Host.ServerLog()}";
	}

	/// <summary>One demo server for the class, and a moment taken before it started.</summary>
	public sealed class ServerFixture : IAsyncLifetime
	{
		public static readonly FollowOptions Options = DemoScadaHost.Options with { ChangeSeconds = 0.25 };

		private DemoScadaServer? _server;

		public DemoScadaHost Host { get; } = new();

		public DemoScadaServer Server =>
			_server ?? throw new InvalidOperationException("The demo server was used before it was started.");

		/// <summary>No later than the server's start instant, which the server does not expose.</summary>
		public DateTime StartFloorUtc { get; private set; }

		public async ValueTask InitializeAsync()
		{
			StartFloorUtc = DateTime.UtcNow;
			_server = await Host.StartAsync(Options);
		}

		public async ValueTask DisposeAsync()
		{
			if (_server is not null)
			{
				await _server.DisposeAsync();
			}

			Host.Dispose();
		}
	}

	/// <summary>The same history rows for every pen, ending at a fixed moment, clipped to each read's range.</summary>
	private sealed class FixedHistory(DateTime historyEndUtc) : IDataProvider
	{
		private readonly DateTime[] _timestamps =
			[.. Enumerable.Range(0, HistoryColumnCount)
				.Select(index => historyEndUtc - ((HistoryColumnCount - 1 - index) * _historyStep))];

		public IReadOnlyList<EnvelopeColumn> Columns =>
			[
				.. _timestamps.Select(
					(stamp, index) => new EnvelopeColumn(LocalTimeAxis.ToAxis(stamp), index, index, index))
			];

		public Task<Result<IReadOnlyList<Pen>>> QueryPensAsync()
		{
			return Task.FromResult(Result.Ok(Catalogue()));
		}

		public Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
			IReadOnlyList<int> penIds,
			DateTime fromUtc,
			DateTime toUtc,
			AggregationLayer layer,
			int targetColumnCount,
			CancellationToken cancellationToken = default)
		{
			var indexes = Enumerable.Range(0, HistoryColumnCount)
				.Where(index => _timestamps[index] >= fromUtc && _timestamps[index] <= toUtc)
				.ToArray();
			DateTime[] timestamps = [.. indexes.Select(index => _timestamps[index])];
			double[] values = [.. indexes.Select(index => (double)index)];
			IReadOnlyList<PenHistoryEnvelope> envelopes =
				[.. penIds.Select(penId => new PenHistoryEnvelope(penId, timestamps, values, values, values))];

			return Task.FromResult(Result.Ok(envelopes));
		}

		public ArchiveExtent Extent => new(_timestamps[0], _timestamps[^1]);

		public Task<Result<ArchiveExtent>> QueryArchiveExtentAsync()
		{
			return Task.FromResult(Result.Ok(Extent));
		}

		public Task<Result<IReadOnlyList<DateOnly>>> QueryArchiveDaysAsync(
			DateOnly firstDay,
			DateOnly lastDay,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(Result.Ok<IReadOnlyList<DateOnly>>([]));
		}
	}
}
