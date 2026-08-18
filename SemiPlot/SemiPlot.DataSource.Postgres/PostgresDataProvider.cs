using System.Reactive.Linq;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Scaffold provider: every Result-returning member fails with <see cref="ProviderNotImplementedError"/>
/// so a mis-wired composition reports the gap instead of drawing an empty chart. Later slices replace
/// one body at a time.
/// </summary>
public sealed class PostgresDataProvider : IDataProvider
{
	public IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<long> penIds)
	{
		return Observable.Empty<IReadOnlyList<Sample>>();
	}

	public Task<Result<IReadOnlyList<Pen>>> QueryPensAsync()
	{
		return Task.FromResult(Result.Fail<IReadOnlyList<Pen>>(
			new ProviderNotImplementedError(nameof(QueryPensAsync))));
	}

	public Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		IReadOnlyList<long> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int targetColumnCount)
	{
		return Task.FromResult(Result.Fail<IReadOnlyList<PenHistoryEnvelope>>(
			new ProviderNotImplementedError(nameof(QueryHistoryAsync))));
	}

	public Task<Result<ArchiveExtent>> QueryArchiveExtentAsync()
	{
		return Task.FromResult(Result.Fail<ArchiveExtent>(
			new ProviderNotImplementedError(nameof(QueryArchiveExtentAsync))));
	}
}
