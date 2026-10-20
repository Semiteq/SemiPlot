using SemiPlot.Core.Trends;

namespace SemiPlot.Core.Data;

/// <summary>The live edge of a pen set (docs/architecture/data-integration.md#realtime).</summary>
public interface ILiveSource
{
	IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<int> penIds);
}
