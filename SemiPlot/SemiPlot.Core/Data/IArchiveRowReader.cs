using FluentResults;

namespace SemiPlot.Core.Data;

/// <summary>
/// Stored main-layer rows, bounded and returned in the archive's wall-clock time
/// (docs/architecture/data-integration.md#the-provider-surface).
/// </summary>
public interface IArchiveRowReader
{
	Task<Result<long>> CountRowsAsync(
		IReadOnlyList<int> penIds, DateTime from, DateTime to, CancellationToken cancellationToken = default);

	Task<Result<IReadOnlyList<StoredArchiveRow>>> ReadSeedRowsAsync(
		IReadOnlyList<int> penIds, DateTime from, CancellationToken cancellationToken = default);

	Task<Result<IReadOnlyList<StoredArchiveRow>>> ReadSliceAsync(
		IReadOnlyList<int> penIds, DateTime from, DateTime to, CancellationToken cancellationToken = default);
}
