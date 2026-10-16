using System.Reactive.Linq;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.UI.Chart;

namespace SemiPlot.Tests.Integration.Journeys;

internal sealed class NoMarkers : IMarkerChannel
{
	public IObservable<IReadOnlyList<StoredMarker>> Reads => Observable.Never<IReadOnlyList<StoredMarker>>();

	public Task<Result<long>> AddAsync(DateTime timeUtc, string? label)
	{
		throw new NotSupportedException();
	}

	public Task<Result> MoveAsync(StoredMarker marker, DateTime timeUtc)
	{
		throw new NotSupportedException();
	}

	public Task<Result> RenameAsync(StoredMarker marker, string? label)
	{
		throw new NotSupportedException();
	}

	public Task<Result> DeleteAsync(StoredMarker marker)
	{
		throw new NotSupportedException();
	}
}
