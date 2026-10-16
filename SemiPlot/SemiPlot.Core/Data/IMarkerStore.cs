using FluentResults;

namespace SemiPlot.Core.Data;

/// <summary>Reads and writes the time markers of <c>semiplot_markers</c>.</summary>
public interface IMarkerStore
{
	Task<Result<IReadOnlyList<StoredMarker>>> ReadAsync(CancellationToken cancellationToken = default);

	Task<Result<long>> AddAsync(DateTime timeUtc, string? label);

	Task<Result> MoveAsync(StoredMarker marker, DateTime timeUtc);

	Task<Result> RenameAsync(StoredMarker marker, string? label);

	Task<Result> DeleteAsync(StoredMarker marker);
}

/// <summary>One <c>semiplot_markers</c> row as stored; a null label is a stored <c>NULL</c>.</summary>
public sealed record StoredMarker(long Id, DateTime TimeUtc, string? Label);
