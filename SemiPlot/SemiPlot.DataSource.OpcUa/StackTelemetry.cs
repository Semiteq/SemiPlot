using Microsoft.Extensions.Logging;

using Opc.Ua;

namespace SemiPlot.DataSource.OpcUa;

/// <summary>Routes the OPC UA stack's logging into the container's logger factory.</summary>
internal sealed class StackTelemetry(ILoggerFactory loggerFactory) : TelemetryContextBase(loggerFactory);
