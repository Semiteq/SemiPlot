using SemiPlot.Core.Data.Errors;

namespace SemiPlot.Core.Data;

/// <summary>Whether the live source's session is up, and the fault that keeps it down when one is known.</summary>
public sealed record LiveSourceState
{
	public static readonly LiveSourceState Connecting = new(isConnected: false, fault: null);

	public static readonly LiveSourceState Connected = new(isConnected: true, fault: null);

	private LiveSourceState(bool isConnected, LiveSourceError? fault)
	{
		IsConnected = isConnected;
		Fault = fault;
	}

	public bool IsConnected { get; }

	public LiveSourceError? Fault { get; }

	public static LiveSourceState FaultedBy(LiveSourceError fault)
	{
		return new LiveSourceState(isConnected: false, fault);
	}

	/// <summary>The state's name for a log line: <c>Connected</c>, <c>Connecting</c> or the fault's kind.</summary>
	public override string ToString()
	{
		if (IsConnected)
		{
			return nameof(Connected);
		}

		return Fault?.Kind.ToString() ?? nameof(Connecting);
	}
}
