namespace SemiPlot.DataSource.OpcUa;

/// <summary>How one pass of the connect loop ended.</summary>
internal enum ConnectPassEnd
{
	/// <summary>No session ran: no server answered, the browse failed or the pass threw.</summary>
	Unanswered,

	/// <summary>The server answered without a usable endpoint or project folder.</summary>
	SetupFault,

	/// <summary>A session ran and ended without the source closing it.</summary>
	SessionEnded
}

/// <summary>
/// The wait before the next pass; a setup fault waits for the operator's fix in the SCADA
/// (docs/architecture/data-integration.md#the-opc-ua-live-source).
/// </summary>
internal sealed class ConnectBackoff
{
	private static readonly TimeSpan _firstRetry = TimeSpan.FromSeconds(1);

	private static readonly TimeSpan _preSessionCap = TimeSpan.FromSeconds(5);

	private static readonly TimeSpan _setupFaultRetry = TimeSpan.FromSeconds(30);

	private TimeSpan _nextUnanswered = _firstRetry;

	public TimeSpan Next(ConnectPassEnd end)
	{
		if (end != ConnectPassEnd.Unanswered)
		{
			_nextUnanswered = _firstRetry;

			return end == ConnectPassEnd.SetupFault ? _setupFaultRetry : _firstRetry;
		}

		var wait = _nextUnanswered;
		var doubled = _nextUnanswered * 2;
		_nextUnanswered = doubled < _preSessionCap ? doubled : _preSessionCap;

		return wait;
	}
}
