using Opc.Ua;
using Opc.Ua.Client;

namespace SemiPlot.DataSource.OpcUa;

/// <summary>The shape of every OPC UA subscription the source creates and of each pen's monitored item.</summary>
internal static class SubscriptionPlan
{
	public const int PublishingIntervalMilliseconds = 100;

	public const uint KeepAliveCount = 10;

	/// <summary>The stack's default of 100 logs a lifetime shorter than the 60 s session timeout.</summary>
	public const uint LifetimeCount = 600;

	public const int SamplingIntervalMilliseconds = 100;

	/// <summary>
	/// Simple-Scada answers a larger queue with an empty first value, so a static variable never shows.
	/// </summary>
	public const uint QueueSize = 1;

	public static Subscription CreateSubscription(ITelemetryContext telemetry)
	{
		return new Subscription(telemetry, new SubscriptionOptions
		{
			DisplayName = ClientApplicationConfiguration.ApplicationName,
			PublishingInterval = PublishingIntervalMilliseconds,
			KeepAliveCount = KeepAliveCount,
			LifetimeCount = LifetimeCount,
			PublishingEnabled = true,
			TimestampsToReturn = TimestampsToReturn.Both,
		});
	}

	/// <summary>The pen id rides on the item as its <see cref="MonitoredItem.Handle"/>.</summary>
	public static MonitoredItem CreateItem(
		ITelemetryContext telemetry,
		string projectId,
		ushort namespaceIndex,
		int penId)
	{
		return new MonitoredItem(telemetry, new MonitoredItemOptions
		{
			StartNodeId = ProjectFolder.NodeOf(projectId, namespaceIndex, penId),
			AttributeId = Attributes.Value,
			NodeClass = NodeClass.Variable,
			MonitoringMode = MonitoringMode.Reporting,
			SamplingInterval = SamplingIntervalMilliseconds,
			QueueSize = QueueSize,
			DiscardOldest = true,
		})
		{
			Handle = penId,
		};
	}
}
