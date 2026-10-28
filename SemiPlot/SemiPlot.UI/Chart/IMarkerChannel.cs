using FluentResults;

using SemiPlot.Core.Data;

namespace SemiPlot.UI.Chart;

/// <summary>The chart's way to the stored markers: every read on the UI thread.</summary>
public interface IMarkerChannel
{
	/// <summary>Every marker read that no write has overtaken, on the UI thread, in the order read.</summary>
	IObservable<IReadOnlyList<StoredMarker>> Reads { get; }

	Task<Result<long>> AddAsync(DateTime timeUtc, string? label);

	Task<Result> MoveAsync(StoredMarker marker, DateTime timeUtc);

	Task<Result> RenameAsync(StoredMarker marker, string? label);

	Task<Result> SetLockedAsync(StoredMarker marker, bool isLocked);

	Task<Result> DeleteAsync(StoredMarker marker);
}
