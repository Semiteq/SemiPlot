using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.OpcUa;

namespace SemiPlot.Tests.Unit.OpcUa;

/// <summary>A pen subscription context over silent telemetry and logging, with the test's own loop and sinks.</summary>
internal static class TestPenSubscriptionContext
{
	public static PenSubscriptionContext Create(
		SerialLoop loop,
		DeliveredStamps stamps,
		IObserver<LiveSourceError> penFaults,
		string endpoint)
	{
		return new PenSubscriptionContext(
			new StackTelemetry(NullLoggerFactory.Instance),
			loop,
			stamps,
			new ReportedUnservedSets(),
			penFaults,
			NullLogger.Instance,
			endpoint);
	}
}
