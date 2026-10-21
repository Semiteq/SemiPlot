using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;

namespace SemiPlot.Core.Data;

/// <summary>The live edge of a pen set and its session (docs/architecture/data-integration.md#realtime).</summary>
public interface ILiveSource
{
	IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<int> penIds);

	/// <summary>Hot, replays the current state to each subscriber, never faults or completes before disposal.</summary>
	IObservable<LiveSourceState> States { get; }

	/// <summary>Hot, one error per pen fault, never faults or completes before disposal.</summary>
	IObservable<LiveSourceError> PenFaults { get; }
}
