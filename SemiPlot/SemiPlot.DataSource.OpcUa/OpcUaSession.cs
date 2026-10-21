using System.Reactive.Concurrency;
using System.Reactive.Disposables;

using Microsoft.Extensions.Logging;

using Opc.Ua;
using Opc.Ua.Client;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.OpcUa.Configuration;

namespace SemiPlot.DataSource.OpcUa;

/// <summary>
/// The process's one OPC UA session: the connect loop, the browse on every new session id, the pen subscriptions on
/// it and the state the source publishes (docs/architecture/data-integration.md#the-opc-ua-live-source).
/// </summary>
internal sealed class OpcUaSession : IAsyncDisposable
{
	/// <summary>Logs an error per failed connect attempt while the server is down; the state line says it.</summary>
	public const string ChannelLogCategory = "Opc.Ua.Bindings.UaSCUaBinaryClientChannel";

	private readonly OpcUaConnectionSettings _settings;
	private readonly StackTelemetry _telemetry;
	private readonly ILogger<OpcUaSession> _logger;
	private readonly TimeSpan _grace;
	private readonly IObserver<LiveSourceState> _states;
	private readonly LiveSessionStates _sessionStates;
	private readonly CancellationTokenSource _stopping = new();
	private readonly CancellationToken _stoppingToken;
	private ApplicationConfiguration? _configuration;

	// Every state input and notification runs on this loop, so the set, LiveSessionStates and the fields below hold
	// no lock.
	private readonly SerialLoop _loop;
	private readonly PenSubscriptionSet _subscriptions;
	private ManagedSession? _session;
	private TaskCompletionSource<ConnectPassEnd>? _sessionEnded;
	private StatusCode? _failedKeepAlive;

	private IDisposable _graceTimer = Disposable.Empty;
	private Task _running = Task.CompletedTask;

	public OpcUaSession(
		OpcUaConnectionSettings settings,
		ILoggerFactory loggerFactory,
		TimeSpan grace,
		IObserver<LiveSourceState> states,
		IObserver<LiveSourceError> penFaults)
	{
		_settings = settings;
		_telemetry = new StackTelemetry(loggerFactory);
		_logger = loggerFactory.CreateLogger<OpcUaSession>();
		_grace = grace;
		_states = states;
		_sessionStates = new LiveSessionStates(settings.EndpointUrl);
		_stoppingToken = _stopping.Token;
		_loop = new SerialLoop(loggerFactory.CreateLogger<SerialLoop>());
		_subscriptions = new PenSubscriptionSet(
			new PenSubscriptionContext(
				_telemetry,
				_loop,
				new DeliveredStamps(),
				new ReportedUnservedSets(),
				penFaults,
				loggerFactory.CreateLogger<PenSubscription>(),
				settings.EndpointUrl),
			_stoppingToken);
	}

	public void Start()
	{
		_graceTimer = DefaultScheduler.Instance.Schedule(
			_grace,
			() => _loop.Enqueue(() => Apply(_sessionStates.OnGraceElapsed())));
		_running = Task.WhenAll(
			Task.Run(() => _loop.PumpAsync(_stoppingToken)),
			Task.Run(() => ConnectLoopAsync(_stoppingToken)));
	}

	public IDisposable Open(IReadOnlyList<int> penIds, IObserver<IReadOnlyList<Sample>> observer)
	{
		return _subscriptions.Open(penIds, observer);
	}

	/// <summary>Stops both loops; the pass that holds a session closes it under its 2 s bound on the way out.</summary>
	public async ValueTask DisposeAsync()
	{
		_graceTimer.Dispose();
		await _stopping.CancelAsync().ConfigureAwait(false);
		_loop.Complete();
		await _running.ConfigureAwait(false);
		_stopping.Dispose();
	}

	private async Task ConnectLoopAsync(CancellationToken token)
	{
		var backoff = new ConnectBackoff();

		try
		{
			while (true)
			{
				var end = await RunGuardedPassAsync(token).ConfigureAwait(false);
				await Task.Delay(backoff.Next(end), token).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
		}
	}

	/// <summary>A pass that throws counts as unanswered, so no failure ends the connect loop.</summary>
	private async Task<ConnectPassEnd> RunGuardedPassAsync(CancellationToken token)
	{
		var end = ConnectPassEnd.Unanswered;

		try
		{
			_configuration ??= await ClientApplicationConfiguration.CreateValidatedAsync(_settings, _telemetry, token)
				.ConfigureAwait(false);
			end = await RunPassAsync(_configuration, token).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
		{
			_logger.LogError(exception, "A connect pass to {Endpoint} failed", _settings.EndpointUrl);
		}

		if (end == ConnectPassEnd.Unanswered)
		{
			_loop.Enqueue(() => Apply(_sessionStates.OnUnanswered()));
		}

		return end;
	}

	private async Task<ConnectPassEnd> RunPassAsync(ApplicationConfiguration configuration, CancellationToken token)
	{
		var (session, isRefused) = await ServerCalls
			.ReachAsync(_settings.EndpointUrl, configuration, _telemetry, _logger, token)
			.ConfigureAwait(false);

		if (isRefused)
		{
			await _loop.InvokeAsync(() => Apply(_sessionStates.OnSetupFault(LiveSourceFault.EndpointRefused)))
				.WaitAsync(token)
				.ConfigureAwait(false);

			return ConnectPassEnd.SetupFault;
		}

		if (session is null)
		{
			return ConnectPassEnd.Unanswered;
		}

		var ended = new TaskCompletionSource<ConnectPassEnd>(TaskCreationOptions.RunContinuationsAsynchronously);

		try
		{
			await _loop.InvokeAsync(() => OpenAsync(session, ended, token)).WaitAsync(token).ConfigureAwait(false);

			return await ended.Task.WaitAsync(token).ConfigureAwait(false);
		}
		finally
		{
			session.ConnectionStateChanged -= OnConnectionStateChanged;
			session.KeepAlive -= OnKeepAlive;

			// A pass that failed still holds the session: it ends as lost. Otherwise the session was let go already.
			_loop.Enqueue(() => ApplyStateChangeAsync(session, ConnectionState.Closed, null));
			await ServerCalls.CloseAsync(session, _logger).ConfigureAwait(false);
		}
	}

	private async Task OpenAsync(
		ManagedSession session, TaskCompletionSource<ConnectPassEnd> ended, CancellationToken token)
	{
		_session = session;
		_sessionEnded = ended;
		session.ConnectionStateChanged += OnConnectionStateChanged;
		session.KeepAlive += OnKeepAlive;

		await OnConnectedAsync(session, token).ConfigureAwait(false);
	}

	/// <summary>The stack raises no event for the first connect, so <see cref="OpenAsync"/> calls this too.</summary>
	private async Task OnConnectedAsync(ManagedSession session, CancellationToken token)
	{
		if (_sessionStates.IsNewSession(session.SessionId) && !await BrowseAsync(session, token).ConfigureAwait(false))
		{
			return;
		}

		await _subscriptions.AttachAllAsync(session, token).ConfigureAwait(false);
		_failedKeepAlive = null;
		Apply(_sessionStates.OnStateChanged(ConnectionState.Connected));
	}

	/// <summary>Reads the project ID of a new session; false when the session ended over it.</summary>
	private async Task<bool> BrowseAsync(ManagedSession session, CancellationToken token)
	{
		string? browsed;

		try
		{
			browsed = await ServerCalls.BrowseProjectIdAsync(session, token).ConfigureAwait(false);
		}
		catch (Exception exception) when (!token.IsCancellationRequested)
		{
			_logger.LogInformation(exception, "Browsing {Endpoint} failed", _settings.EndpointUrl);
			EndSession(ConnectPassEnd.Unanswered);

			return false;
		}

		if (await _subscriptions.ApplyProjectAsync(session, browsed).ConfigureAwait(false))
		{
			return true;
		}

		Apply(_sessionStates.OnSetupFault(LiveSourceFault.ProjectMissing));
		EndSession(ConnectPassEnd.SetupFault);

		return false;
	}

	private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs change)
	{
		_loop.Enqueue(() => ApplyStateChangeAsync(sender, change.NewState, change.Error?.StatusCode));
	}

	private void OnKeepAlive(ISession session, KeepAliveEventArgs keepAlive)
	{
		if (keepAlive.Status is { } status && ServiceResult.IsBad(status))
		{
			_loop.Enqueue(() => _failedKeepAlive = status.StatusCode);
		}
	}

	private async Task ApplyStateChangeAsync(object? sender, ConnectionState next, StatusCode? status)
	{
		if (sender is not ManagedSession session || !ReferenceEquals(session, _session))
		{
			return;
		}

		if (next == ConnectionState.Connected)
		{
			await OnConnectedAsync(session, _stoppingToken).ConfigureAwait(false);

			return;
		}

		Apply(_sessionStates.OnStateChanged(next), status ?? _failedKeepAlive);

		if (next == ConnectionState.Closed)
		{
			EndSession(ConnectPassEnd.SessionEnded);
		}
	}

	/// <summary>
	/// Hands the session back to the connect loop to close; its Closing and Closed publish nothing, because
	/// <see cref="ApplyStateChangeAsync"/> drops the events of a session the source no longer holds.
	/// </summary>
	private void EndSession(ConnectPassEnd end)
	{
		_subscriptions.DetachAll();
		_session = null;
		_sessionEnded?.TrySetResult(end);
	}

	private void Apply(LiveSessionChange change, StatusCode? status = null)
	{
		if (_stoppingToken.IsCancellationRequested)
		{
			return;
		}

		if (change.IsLoss)
		{
			_subscriptions.OnLost();
		}

		if (change.Published is not { } state)
		{
			return;
		}

		_logger.LogInformation(
			"OPC UA session to {Endpoint}: {State}, status {Status}",
			_settings.EndpointUrl, state, status?.ToString() ?? "none");
		_states.OnNext(state);
	}
}
