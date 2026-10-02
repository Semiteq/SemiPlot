using System.Reactive.Concurrency;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

internal static class HistoryDebouncerTestBuilder
{
	public static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(150);
	public static readonly TimeSpan CapInterval = TimeSpan.FromMilliseconds(400);
	public static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(10.0);
	public static readonly DateTime From = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	public static readonly DateTime To = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

	// The emission head runs on the TestScheduler and every delivery on the thread the query completed on.
	public static ChartHistoryRequestDebouncer CreateDebouncer(
		TestScheduler scheduler,
		Func<HistoryRequest, CancellationToken, Task<Result<IReadOnlyList<PenHistoryEnvelope>>>> queryAsync,
		Action<HistoryRequest, IReadOnlyList<PenHistoryEnvelope>>? applyHistory = null,
		Action<IReadOnlyList<IError>>? reportQueryFailure = null)
	{
		return new ChartHistoryRequestDebouncer(
			queryAsync,
			applyHistory ?? ((_, _) => { }),
			reportQueryFailure ?? (_ => { }),
			DebounceWindow,
			CapInterval,
			scheduler,
			ImmediateScheduler.Instance);
	}

	// A released query's continuation, and the query the pipeline starts behind it, run on the thread Rx
	// resumes the task on.
	public static async Task AwaitCondition(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + TestDeadline;

		while (!condition() && DateTime.UtcNow < deadline)
		{
			await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
		}

		condition().Should().BeTrue();
	}

	public static Result<IReadOnlyList<PenHistoryEnvelope>> Ok(HistoryRequest request)
	{
		return Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
			[new PenHistoryEnvelope(request.PenIds[0], [request.FromUtc], [0.0], [0.0], [0.0])]);
	}

	public static Result<IReadOnlyList<PenHistoryEnvelope>> Unreachable()
	{
		return Result.Fail<IReadOnlyList<PenHistoryEnvelope>>("The archive is unreachable.");
	}

	public static HistoryRequest RequestForLayer(AggregationLayer layer)
	{
		return RequestOver([1], From, To, layer);
	}

	public static HistoryRequest RequestAtNotch(int notch)
	{
		return RequestOver([1], From.AddSeconds(notch), To.AddSeconds(notch), AggregationLayer.Raw);
	}

	public static HistoryRequest RequestOver(
		IReadOnlyList<int> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer)
	{
		return new HistoryRequest(
			penIds,
			new FetchRange(fromUtc, toUtc, layer, HistoryColumnTarget.MaxColumns, toUtc - fromUtc),
			HistoryColumnTarget.MaxColumns);
	}
}
