using System.Reactive.Concurrency;
using System.Reactive.Disposables;

using Opc.Ua.Client;

using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.OpcUa;

/// <summary>
/// The source's open pen subscriptions, the project they are built under and the session they live on. Every member
/// but <see cref="Open"/> runs on the source's loop, so the set holds no lock.
/// </summary>
internal sealed class PenSubscriptionSet(PenSubscriptionContext context, CancellationToken stoppingToken)
{
	// A creation that failed on a connected session is tried again after this; no Connected may come to retry it.
	private static readonly TimeSpan _createRetry = TimeSpan.FromSeconds(10);

	private readonly List<PenSubscription> _open = [];

	// Let go of while the session was down, or not deleted when it was up: deleted at the next Connected.
	private readonly List<Subscription> _stale = [];

	private string? _projectId;
	private ushort _namespaceIndex;

	private ManagedSession? _connectedSession;
	private IDisposable? _createRetryTimer;

	/// <summary>
	/// Registers a pen set from any thread; created at once while connected, else at the next Connected.
	/// </summary>
	public IDisposable Open(IReadOnlyList<int> penIds, IObserver<IReadOnlyList<Sample>> observer)
	{
		var pen = new PenSubscription(penIds, observer, context);
		context.Loop.Enqueue(() => AddAsync(pen));

		return Disposable.Create(() =>
		{
			pen.Close();
			context.Loop.Enqueue(() => Remove(pen));
		});
	}

	/// <summary>
	/// Takes the project ID a new session browsed: false when there is none. A changed ID deletes every subscription,
	/// because each names the old nodes; the same ID reads the item statuses of the subscriptions the stack recreated.
	/// </summary>
	public async Task<bool> ApplyProjectAsync(ManagedSession session, string? browsed)
	{
		var change = ProjectFolder.Next(_projectId, browsed);

		if (change == ProjectFolderChange.Missing)
		{
			return false;
		}

		if (change == ProjectFolderChange.Recreate)
		{
			await DeleteAllAsync(session).ConfigureAwait(false);
		}
		else
		{
			ReadItemStatuses();
		}

		_projectId = browsed;
		_namespaceIndex = (ushort)session.NamespaceUris.GetIndex(ProjectFolder.NamespaceUri);

		return true;
	}

	public async Task AttachAllAsync(ManagedSession session, CancellationToken token)
	{
		_connectedSession = session;

		foreach (var subscription in _stale)
		{
			await ServerCalls.RemoveSubscriptionAsync(session, subscription, context.Logger, token)
				.ConfigureAwait(false);
		}

		_stale.Clear();
		await CreateMissingAsync(session, token).ConfigureAwait(false);
	}

	public void OnLost()
	{
		_connectedSession = null;

		foreach (var pen in _open)
		{
			if (!pen.IsClosed)
			{
				pen.EmitLossBreaks();
			}
		}
	}

	/// <summary>The session ends with every subscription on it; the next session creates them anew.</summary>
	public void DetachAll()
	{
		_connectedSession = null;

		foreach (var pen in _open)
		{
			pen.Detach();
		}

		_stale.Clear();
	}

	/// <summary>A set added while the session is down breaks its pens as a set open over the loss would have.</summary>
	private async Task AddAsync(PenSubscription pen)
	{
		if (pen.IsClosed)
		{
			return;
		}

		_open.Add(pen);

		if (_connectedSession is { } session)
		{
			await CreateAsync(pen, session, stoppingToken).ConfigureAwait(false);
		}
		else
		{
			pen.EmitLossBreaks();
		}
	}

	private void Remove(PenSubscription pen)
	{
		if (_open.Remove(pen) && pen.Detach() is { } subscription)
		{
			Discard(subscription);
		}
	}

	private async Task CreateMissingAsync(ManagedSession session, CancellationToken token)
	{
		foreach (var pen in _open)
		{
			await CreateAsync(pen, session, token).ConfigureAwait(false);
		}
	}

	private async Task CreateAsync(PenSubscription pen, ManagedSession session, CancellationToken token)
	{
		if (pen.IsClosed || pen.Subscription is not null || _projectId is not { } projectId)
		{
			return;
		}

		var subscription = pen.Build(projectId, _namespaceIndex);

		if (await ServerCalls.CreateSubscriptionAsync(session, subscription, context.Logger, token)
			.ConfigureAwait(false))
		{
			pen.Attach(subscription);

			return;
		}

		Discard(subscription);
		_createRetryTimer ??= DefaultScheduler.Instance.Schedule(
			_createRetry,
			() => context.Loop.Enqueue(RetryCreateAsync));
	}

	private async Task RetryCreateAsync()
	{
		_createRetryTimer = null;

		if (_connectedSession is { } session)
		{
			await CreateMissingAsync(session, stoppingToken).ConfigureAwait(false);
		}
	}

	private void Discard(Subscription subscription)
	{
		if (_connectedSession is { } session)
		{
			// Never faults: RemoveSubscriptionAsync never throws, and Enqueue only queues.
			_ = DeleteOrKeepAsync(session, subscription);
		}
		else
		{
			_stale.Add(subscription);
		}
	}

	private async Task DeleteOrKeepAsync(ManagedSession session, Subscription subscription)
	{
		if (!await ServerCalls.RemoveSubscriptionAsync(session, subscription, context.Logger, stoppingToken)
			.ConfigureAwait(false))
		{
			context.Loop.Enqueue(() => _stale.Add(subscription));
		}
	}

	private async Task DeleteAllAsync(ManagedSession session)
	{
		foreach (var pen in _open)
		{
			if (pen.Detach() is { } subscription
				&& !await ServerCalls.RemoveSubscriptionAsync(session, subscription, context.Logger, stoppingToken)
					.ConfigureAwait(false))
			{
				_stale.Add(subscription);
			}
		}
	}

	private void ReadItemStatuses()
	{
		foreach (var pen in _open)
		{
			pen.ReadItemStatuses();
		}
	}
}
