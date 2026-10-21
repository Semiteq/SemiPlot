using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data.Errors;

namespace SemiPlot.DataSource.OpcUa;

/// <summary>What every pen subscription of one source shares.</summary>
internal sealed record PenSubscriptionContext(
	StackTelemetry Telemetry,
	SerialLoop Loop,
	DeliveredStamps Stamps,
	ReportedUnservedSets ReportedSets,
	IObserver<LiveSourceError> PenFaults,
	ILogger Logger,
	string Endpoint);
