using FluentResults;

using SemiPlot.Core.Trends;

namespace SemiPlot.Core.Data;

public interface IDataProvider
{
	Task<Result<CatalogueRead>> QueryCatalogueAsync();

	Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		IReadOnlyList<int> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int targetColumnCount,
		CancellationToken cancellationToken = default);

	Task<Result<ArchiveExtent>> QueryArchiveExtentAsync();

	// Archive wall-clock days, ascending: the days in [firstDay, lastDay] that hold a raw row of a catalogue pen.
	Task<Result<IReadOnlyList<DateOnly>>> QueryArchiveDaysAsync(
		DateOnly firstDay,
		DateOnly lastDay,
		CancellationToken cancellationToken = default);
}
