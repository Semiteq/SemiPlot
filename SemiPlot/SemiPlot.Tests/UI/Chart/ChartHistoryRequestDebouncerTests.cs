using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartHistoryRequestDebouncerTests
{
	private static readonly TimeSpan _debounceWindow = TimeSpan.FromMilliseconds(150);
	private static readonly DateTime _from = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _to = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void RapidRequests_CollapseToOneTrailingQuery()
	{
		var scheduler = new TestScheduler();
		var queryCount = 0;
		using var debouncer = new ChartHistoryRequestDebouncer(
			request =>
			{
				queryCount++;
				return Observable
					.Return(Ok(request))
					.ToTask();
			},
			(_, _) => { },
			_debounceWindow,
			scheduler,
			ImmediateScheduler.Instance);

		for (var notch = 0; notch < 5; notch++)
		{
			debouncer.Request(RequestForLayer(AggregationLayer.Raw));
			scheduler.AdvanceBy(TimeSpan.FromMilliseconds(20).Ticks);
		}

		queryCount.Should().Be(0);

		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		queryCount.Should().Be(1);
	}

	[Fact]
	public async Task StaleResponse_IsDroppedWhenANewerWindowSupersedesAnInFlightQuery()
	{
		// A slow first query is still in flight when a newer window arrives; Switch must unsubscribe the
		// stale query so its late response never overwrites the newer window (latest-wins). Real schedulers
		// are used here because the latest-wins guard spans a real async query boundary.
		var firstQueryGate = new TaskCompletionSource();
		var firstQueryStarted = new TaskCompletionSource();
		var applied = new List<AggregationLayer>();
		var secondApplied = new TaskCompletionSource();
		var shortWindow = TimeSpan.FromMilliseconds(20);

		using var debouncer = new ChartHistoryRequestDebouncer(
			async request =>
			{
				if (request.Layer == AggregationLayer.Minute)
				{
					firstQueryStarted.TrySetResult();
					await firstQueryGate.Task;
				}

				return Ok(request);
			},
			(history, _) =>
			{
				applied.Add(history.Layer);
				if (history.Layer == AggregationLayer.Hour)
				{
					secondApplied.TrySetResult();
				}
			},
			shortWindow,
			DefaultScheduler.Instance,
			ImmediateScheduler.Instance);

		debouncer.Request(RequestForLayer(AggregationLayer.Minute));
		await firstQueryStarted.Task;

		debouncer.Request(RequestForLayer(AggregationLayer.Hour));
		await secondApplied.Task;

		// Release the stale first query only after the newer one has already been applied; its result must
		// be dropped by Switch.
		firstQueryGate.SetResult();
		await Task.Delay(50);

		applied.Should().ContainSingle();
		applied[0].Should().Be(AggregationLayer.Hour);
	}

	private static Result<IReadOnlyList<PenHistoryEnvelope>> Ok(HistoryRequest request)
	{
		return Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
			[new PenHistoryEnvelope(request.PenIds[0], [request.FromUtc], [0.0], [0.0], [0.0])]);
	}

	private static HistoryRequest RequestForLayer(AggregationLayer layer)
	{
		return new HistoryRequest([1], _from, _to, layer, 0L);
	}
}
