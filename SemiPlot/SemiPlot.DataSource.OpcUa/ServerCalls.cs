using Microsoft.Extensions.Logging;

using Opc.Ua;
using Opc.Ua.Client;

namespace SemiPlot.DataSource.OpcUa;

/// <summary>The stack calls the source makes: discovery, session, browse, subscriptions and close.</summary>
internal static class ServerCalls
{
	private const string SessionName = "SemiPlot";

	// A listener that accepts and never answers holds discovery for 90 s.
	private static readonly TimeSpan _discoveryBound = TimeSpan.FromSeconds(10);

	// A CloseSession to a peer that never answers would otherwise wait the 15 s operation timeout.
	private static readonly TimeSpan _closeBound = TimeSpan.FromSeconds(2);

	// A browse and a creation run on the source's loop; a session that drops in the middle must not hold it.
	private static readonly TimeSpan _loopCallBound = TimeSpan.FromSeconds(10);

	/// <summary>A session on the configured URL, or none: refused when the server offers no usable endpoint.</summary>
	public static async Task<(ManagedSession? Session, bool IsRefused)> ReachAsync(
		string endpointUrl,
		ApplicationConfiguration configuration,
		ITelemetryContext telemetry,
		ILogger logger,
		CancellationToken token)
	{
		var endpoints = await DiscoverAsync(endpointUrl, configuration, logger, token).ConfigureAwait(false);

		if (endpoints is null)
		{
			return (null, false);
		}

		if (EndpointChoice.Pick(endpoints, endpointUrl) is not { } endpoint)
		{
			return (null, true);
		}

		return (await ConnectAsync(endpoint, configuration, telemetry, logger, token).ConfigureAwait(false), false);
	}

	/// <summary>The server's endpoints, or null when it does not answer within the bound.</summary>
	private static async Task<IReadOnlyList<EndpointDescription>?> DiscoverAsync(
		string endpointUrl,
		ApplicationConfiguration configuration,
		ILogger logger,
		CancellationToken token)
	{
		using var bound = CancellationTokenSource.CreateLinkedTokenSource(token);
		bound.CancelAfter(_discoveryBound);

		try
		{
			using var discovery = await DiscoveryClient.CreateAsync(
				configuration,
				new Uri(endpointUrl),
				DiagnosticsMasks.None,
				bound.Token).ConfigureAwait(false);

			return [.. await discovery.GetEndpointsAsync(default, bound.Token).ConfigureAwait(false)];
		}
		catch (Exception exception) when (!token.IsCancellationRequested)
		{
			logger.LogDebug(exception, "No endpoints from the OPC UA server at {Endpoint}", endpointUrl);

			return null;
		}
	}

	/// <summary>
	/// The stack retries inside the call until a server answers, so only a session the server rejects returns null.
	/// The session keeps the configured URL on every reconnect
	/// (docs/architecture/data-integration.md#the-opc-ua-live-source).
	/// </summary>
	private static async Task<ManagedSession?> ConnectAsync(
		EndpointDescription endpoint,
		ApplicationConfiguration configuration,
		ITelemetryContext telemetry,
		ILogger logger,
		CancellationToken token)
	{
		var configured = new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(configuration))
		{
			UpdateBeforeConnect = false,
		};

		try
		{
			return await new ManagedSessionBuilder(configuration, telemetry)
				.UseEndpoint(configured)
				.WithSessionName(SessionName)
				.WithSessionTimeout(
					TimeSpan.FromMilliseconds(ClientApplicationConfiguration.SessionTimeoutMilliseconds))
				.WithUserIdentity(new UserIdentity())
				.WithReconnectPolicy(policy => policy with { MaxTotalReconnectTime = Timeout.InfiniteTimeSpan })
				.ConnectAsync(token)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (!token.IsCancellationRequested)
		{
			logger.LogDebug(exception, "No OPC UA session to {Endpoint}", endpoint.EndpointUrl);

			return null;
		}
	}

	/// <summary>
	/// Throws <see cref="OperationCanceledException"/> when the server does not answer within the bound.
	/// </summary>
	public static async Task<string?> BrowseProjectIdAsync(ManagedSession session, CancellationToken token)
	{
		using var bound = CancellationTokenSource.CreateLinkedTokenSource(token);
		bound.CancelAfter(_loopCallBound);
		var browser = new Browser(session)
		{
			BrowseDirection = BrowseDirection.Forward,
			ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
			IncludeSubtypes = true,
			NodeClassMask = (uint)NodeClass.Object,
		};
		var children = await browser.BrowseAsync(ObjectIds.ObjectsFolder, bound.Token).ConfigureAwait(false);

		return ProjectFolder.ProjectIdOf([.. children], session.NamespaceUris);
	}

	/// <summary>
	/// False when the server did not create it within the bound; the subscription stays on the session.
	/// </summary>
	public static async Task<bool> CreateSubscriptionAsync(
		ManagedSession session,
		Subscription subscription,
		ILogger logger,
		CancellationToken token)
	{
		using var bound = CancellationTokenSource.CreateLinkedTokenSource(token);
		bound.CancelAfter(_loopCallBound);

		try
		{
			session.AddSubscription(subscription);
			await subscription.CreateAsync(bound.Token).ConfigureAwait(false);

			return true;
		}
		catch (Exception exception) when (!token.IsCancellationRequested)
		{
			logger.LogWarning(exception, "Creating an OPC UA subscription failed; the source tries again");

			return false;
		}
	}

	/// <summary>Never throws: false when the delete failed, which is logged.</summary>
	public static async Task<bool> RemoveSubscriptionAsync(
		ManagedSession session,
		Subscription subscription,
		ILogger logger,
		CancellationToken token)
	{
		using var bound = CancellationTokenSource.CreateLinkedTokenSource(token);
		bound.CancelAfter(_closeBound);

		try
		{
			await session.RemoveSubscriptionAsync(subscription, bound.Token).ConfigureAwait(false);

			return true;
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "Deleting an OPC UA subscription failed");

			return false;
		}
	}

	/// <summary>Never throws: the connect loop goes on after any close.</summary>
	public static async Task CloseAsync(ManagedSession session, ILogger logger)
	{
		using var giveUp = new CancellationTokenSource(_closeBound);

		try
		{
			await session.CloseAsync(giveUp.Token).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			logger.LogDebug(exception, "Closing the OPC UA session gave up");
		}

		try
		{
			await session.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "Disposing the OPC UA session failed");
		}
	}
}
