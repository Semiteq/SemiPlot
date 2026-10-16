using System.Reactive.Linq;
using System.Reactive.Subjects;

using SemiPlot.Core.Data;
using SemiPlot.Tests.Unit.UI.Markers;
using SemiPlot.UI.Chart;

namespace SemiPlot.Tests.Unit.UI.Chart;

/// <summary>A marker channel the test drives: <see cref="Push"/> raises a read.</summary>
internal sealed class FakeMarkerChannel : FakeMarkerWriter, IMarkerChannel
{
	private readonly Subject<IReadOnlyList<StoredMarker>> _reads = new();

	public IObservable<IReadOnlyList<StoredMarker>> Reads => _reads.AsObservable();

	public void Push(params StoredMarker[] markers)
	{
		_reads.OnNext(markers);
	}

	public void Fail(Exception failure)
	{
		_reads.OnError(failure);
	}
}
