using System.Threading.Channels;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Opc.Ua;
using Opc.Ua.Client;

using MonitoredItem = Opc.Ua.Client.MonitoredItem;
using Subscription = Opc.Ua.Client.Subscription;

namespace SemiPlot.Tests.Integration.DemoScada;

/// <summary>An anonymous client over the None endpoint with no application certificate.</summary>
public sealed class OpcTestClient : IAsyncDisposable
{
	private const uint SessionTimeoutMilliseconds = 60000;
	private const int PublishingIntervalMilliseconds = 100;
	private const int SamplingIntervalMilliseconds = 100;

	// A session whose server has gone answers no CloseSession; the client gives up after this.
	private static readonly TimeSpan _closeTimeout = TimeSpan.FromSeconds(2);

	private static readonly ITelemetryContext _telemetry = new Telemetry(NullLoggerFactory.Instance);

	private readonly ISession _session;

	private OpcTestClient(ISession session)
	{
		_session = session;
	}

	public bool Connected => _session.Connected;

	public NamespaceTable NamespaceUris => _session.NamespaceUris;

	public static async Task<IReadOnlyList<EndpointDescription>> GetEndpointsAsync(
		string endpointUrl,
		CancellationToken cancellationToken)
	{
		using var discovery = await DiscoveryClient.CreateAsync(
			CreateConfiguration(),
			new Uri(endpointUrl),
			DiagnosticsMasks.None,
			cancellationToken);

		return [.. await discovery.GetEndpointsAsync(default, cancellationToken)];
	}

	/// <summary>Opens a session with the anonymous token policy the server's None endpoint advertises.</summary>
	public static async Task<OpcTestClient> ConnectAsync(string endpointUrl, CancellationToken cancellationToken)
	{
		var configuration = CreateConfiguration();
		var endpoints = await GetEndpointsAsync(endpointUrl, cancellationToken);
		var none = endpoints.Single(endpoint => endpoint.SecurityMode == MessageSecurityMode.None
			&& endpoint.SecurityPolicyUri == SecurityPolicies.None);
		var configured = new ConfiguredEndpoint(null, none, EndpointConfiguration.Create(configuration));
		var session = await new DefaultSessionFactory(_telemetry).CreateAsync(
			configuration,
			configured,
			false,
			false,
			nameof(OpcTestClient),
			SessionTimeoutMilliseconds,
			new UserIdentity(),
			default,
			cancellationToken);

		return new OpcTestClient(session);
	}

	public async Task<IReadOnlyList<ReferenceDescription>> BrowseAsync(
		NodeId node,
		CancellationToken cancellationToken)
	{
		var browser = new Browser(_session)
		{
			BrowseDirection = BrowseDirection.Forward,
			ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
			IncludeSubtypes = true,
			NodeClassMask = (int)(NodeClass.Object | NodeClass.Variable),
		};

		return [.. await browser.BrowseAsync(node, cancellationToken)];
	}

	public NodeId ToNodeId(ExpandedNodeId node)
	{
		return ExpandedNodeId.ToNodeId(node, _session.NamespaceUris);
	}

	public Task<Node> ReadNodeAsync(NodeId node, CancellationToken cancellationToken)
	{
		return _session.ReadNodeAsync(node, cancellationToken);
	}

	/// <summary>Monitors the nodes with a queue of one and collects every data change into a channel.</summary>
	public async Task<OpcTestSubscription> SubscribeAsync(
		IReadOnlyList<NodeId> nodes,
		CancellationToken cancellationToken)
	{
		var notifications = Channel.CreateUnbounded<OpcNotification>();
		var subscription = new Subscription(_session.DefaultSubscription)
		{
			PublishingInterval = PublishingIntervalMilliseconds,
			TimestampsToReturn = TimestampsToReturn.Both,
		};

		foreach (var node in nodes)
		{
			var item = new MonitoredItem(subscription.DefaultItem)
			{
				StartNodeId = node,
				SamplingInterval = SamplingIntervalMilliseconds,
				QueueSize = 1,
			};
			item.Notification += (monitored, arguments) =>
			{
				if (arguments.NotificationValue is MonitoredItemNotification notification)
				{
					notifications.Writer.TryWrite(
						new OpcNotification(monitored.StartNodeId, notification.Value, DateTime.UtcNow));
				}
			};
			subscription.AddItem(item);
		}

		_session.AddSubscription(subscription);
		await subscription.CreateAsync(cancellationToken);

		var statuses = subscription.MonitoredItems.ToDictionary(
			item => item.StartNodeId,
			item => item.Status.Error?.StatusCode ?? StatusCodes.Good);

		return new OpcTestSubscription(notifications.Reader, statuses);
	}

	public async ValueTask DisposeAsync()
	{
		using var giveUp = new CancellationTokenSource(_closeTimeout);

		try
		{
			await _session.CloseAsync(giveUp.Token);
		}
		catch (Exception exception) when (exception is ServiceResultException or OperationCanceledException)
		{
		}

		_session.Dispose();
	}

	private static ApplicationConfiguration CreateConfiguration()
	{
		return new ApplicationConfiguration(_telemetry)
		{
			ApplicationName = nameof(OpcTestClient),
			ApplicationUri = $"urn:localhost:{nameof(OpcTestClient)}",
			ApplicationType = ApplicationType.Client,
			SecurityConfiguration = new SecurityConfiguration(),
			TransportQuotas = new TransportQuotas(),
			ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = (int)SessionTimeoutMilliseconds },
		};
	}

	private sealed class Telemetry(ILoggerFactory loggerFactory) : TelemetryContextBase(loggerFactory);
}

/// <summary>One data change as the client received it.</summary>
public sealed record OpcNotification(NodeId Node, DataValue Value, DateTime ReceivedUtc);

/// <summary>The notifications of one subscription and the status each monitored item was created with.</summary>
public sealed record OpcTestSubscription(
	ChannelReader<OpcNotification> Notifications,
	IReadOnlyDictionary<NodeId, StatusCode> ItemStatuses);
