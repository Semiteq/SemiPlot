using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Opc.Ua;

using SemiPlot.DataSource.OpcUa;

using Xunit;

namespace SemiPlot.Tests.Unit.OpcUa;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class SubscriptionPlanTests
{
	private const string ProjectId = "FD34514A35";
	private const ushort NamespaceIndex = 3;

	private readonly StackTelemetry _telemetry = new(NullLoggerFactory.Instance);

	[Fact]
	public void AMonitoredItemsQueueIsOne_BecauseSimpleScadaSendsAnEmptyFirstValueAboveIt()
	{
		var item = SubscriptionPlan.CreateItem(_telemetry, ProjectId, NamespaceIndex, 1005);

		item.QueueSize.Should().Be(1);
	}

	[Fact]
	public void AMonitoredItem_SamplesEvery100MillisecondsTheValueOfThePensNode()
	{
		var item = SubscriptionPlan.CreateItem(_telemetry, ProjectId, NamespaceIndex, 1005);

		item.SamplingInterval.Should().Be(100);
		item.AttributeId.Should().Be(Attributes.Value);
		item.MonitoringMode.Should().Be(MonitoringMode.Reporting);
		item.StartNodeId.Should().Be(new NodeId("FD34514A351005", NamespaceIndex));
		item.Handle.Should().Be(1005);
	}

	[Fact]
	public void ASubscription_Publishes100MillisecondsWithBothStamps()
	{
		var subscription = SubscriptionPlan.CreateSubscription(_telemetry);

		subscription.PublishingInterval.Should().Be(100);
		subscription.TimestampsToReturn.Should().Be(TimestampsToReturn.Both);
		subscription.KeepAliveCount.Should().Be(10);
		subscription.LifetimeCount.Should().Be(600);
		subscription.PublishingEnabled.Should().BeTrue();
	}
}
