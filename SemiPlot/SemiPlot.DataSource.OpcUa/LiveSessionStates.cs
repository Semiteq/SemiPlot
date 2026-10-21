using Opc.Ua;
using Opc.Ua.Client;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;

namespace SemiPlot.DataSource.OpcUa;

/// <summary>
/// What one input changes: the state to publish, null when it stays, and whether the session was just lost, which
/// makes every open subscription emit its loss breaks.
/// </summary>
internal readonly record struct LiveSessionChange(LiveSourceState? Published, bool IsLoss)
{
	public static readonly LiveSessionChange None = new(null, false);
}

/// <summary>
/// The source's session state as a pure function of the stack's transitions, the connect loop's outcomes and the
/// grace timer. Only the source's loop calls it, so it holds no lock.
/// </summary>
internal sealed class LiveSessionStates(string endpoint)
{
	private NodeId _sessionId = NodeId.Null;
	private LiveSourceState _current = LiveSourceState.Connecting;

	/// <summary>True for a first connect or a recreated session, whose project folder is browsed again.</summary>
	public bool IsNewSession(NodeId sessionId)
	{
		var isNew = sessionId != _sessionId;
		_sessionId = sessionId;

		return isNew;
	}

	public LiveSessionChange OnStateChanged(ConnectionState next)
	{
		if (next == ConnectionState.Connected)
		{
			return new LiveSessionChange(Publish(LiveSourceState.Connected), IsLoss: false);
		}

		if (!_current.IsConnected)
		{
			return LiveSessionChange.None;
		}

		return new LiveSessionChange(Publish(Unreachable()), IsLoss: true);
	}

	/// <summary>The grace after the loop's start: the fault, once, only when nothing was published before it.</summary>
	public LiveSessionChange OnGraceElapsed()
	{
		if (_current != LiveSourceState.Connecting)
		{
			return LiveSessionChange.None;
		}

		return new LiveSessionChange(Publish(Unreachable()), IsLoss: false);
	}

	/// <summary>A server without a usable endpoint or project folder; the same fault again publishes nothing.</summary>
	public LiveSessionChange OnSetupFault(LiveSourceFault kind)
	{
		return new LiveSessionChange(Publish(FaultedBy(kind)), IsLoss: false);
	}

	/// <summary>A pass that ended with no session: a standing setup fault becomes the fault of a server gone.</summary>
	public LiveSessionChange OnUnanswered()
	{
		if (_current == LiveSourceState.Connecting)
		{
			return LiveSessionChange.None;
		}

		var wasConnected = _current.IsConnected;

		return new LiveSessionChange(Publish(Unreachable()), IsLoss: wasConnected);
	}

	private LiveSourceState FaultedBy(LiveSourceFault kind)
	{
		return LiveSourceState.FaultedBy(new LiveSourceError(kind, endpoint));
	}

	private LiveSourceState Unreachable()
	{
		return FaultedBy(LiveSourceFault.Unreachable);
	}

	private LiveSourceState? Publish(LiveSourceState next)
	{
		if (next.IsConnected == _current.IsConnected && next.Fault?.Kind == _current.Fault?.Kind)
		{
			return null;
		}

		_current = next;

		return next;
	}
}
