using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

using AwesomeAssertions;

using SemiPlot.Tests.Integration.DemoScada;

using Xunit;

namespace SemiPlot.Tests.Integration.OpcUa;

// Every endpoint is a free loopback port from DemoScadaHost; the waits are bounded by the timings in
// docs/architecture/data-integration.md#the-session-state-the-live-source-reports.
[Collection(DemoScadaCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class OpcUaSessionTests : IDisposable
{
	private const int TestTimeoutMilliseconds = 180000;

	private static readonly TimeSpan _connectBound = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan _lossBound = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan _clockTolerance = TimeSpan.FromMilliseconds(50);

	// The source's own bounds.
	private static readonly TimeSpan _closeBound = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan _syncDisposeBound = TimeSpan.FromSeconds(3);

	// The connect loop's longest wait while no server answers.
	private static readonly TimeSpan _preSessionCap = TimeSpan.FromSeconds(5);

	// Room for the pool to schedule the close's continuations under a full test run.
	private static readonly TimeSpan _poolSlack = TimeSpan.FromSeconds(3);

	// Discovery gives a peer that never answers 10 s; the next pass dials 1 s later.
	private static readonly TimeSpan _discoveryBound = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan _secondDialBound = TimeSpan.FromSeconds(20);
	private static readonly TimeSpan _timerTolerance = TimeSpan.FromMilliseconds(500);

	private readonly DemoScadaHost _host = new();

	public void Dispose()
	{
		_host.Dispose();
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task ARunningServer_TakesConnectingThenConnected()
	{
		await using var server = await _host.StartAsync(DemoScadaHost.Options);
		await using var live = new LiveSourceHost(LiveSourceHost.PortOf(server));

		live.Start();

		(await live.WaitForStatesAsync(2, _connectBound)).Should().BeTrue(Because(live));
		Names(live).Should().Equal(["Connecting", "Connected"], Because(live));
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task NoServer_TakesUnreachableAfterTheGrace_ThenConnectedOnceAServerStartsOnThePort()
	{
		var port = DemoScadaHost.FreePort();
		await using var live = new LiveSourceHost(port, LiveSourceHost.ShortGrace);

		live.Start();

		(await live.WaitForStatesAsync(2, _lossBound)).Should().BeTrue(Because(live));
		Names(live).Should().Equal(["Connecting", "Unreachable"], Because(live));
		(live.States[1].ReceivedUtc - live.StartedUtc).Should().BeGreaterThanOrEqualTo(
			LiveSourceHost.ShortGrace - _clockTolerance,
			Because(live));

		await using var server = await _host.StartOnPortOnceFreeAsync(DemoScadaHost.Options with { OpcPort = port });

		(await live.WaitForStatesAsync(3, _connectBound)).Should().BeTrue(Because(live));
		Names(live).Should().Equal(["Connecting", "Unreachable", "Connected"], Because(live));
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task AServerRestartedOnItsPort_TakesConnectedUnreachableConnected()
	{
		var first = await _host.StartAsync(DemoScadaHost.Options);
		var port = LiveSourceHost.PortOf(first);
		await using var live = new LiveSourceHost(port);

		try
		{
			live.Start();
			(await live.WaitForStatesAsync(2, _connectBound)).Should().BeTrue(Because(live));
		}
		finally
		{
			await first.DisposeAsync();
		}

		(await live.WaitForStatesAsync(3, _lossBound)).Should().BeTrue(Because(live));

		await using var restarted = await _host.StartOnPortOnceFreeAsync(DemoScadaHost.Options with { OpcPort = port });

		(await live.WaitForStatesAsync(4, _connectBound)).Should().BeTrue(Because(live));
		Names(live).Should().Equal(["Connecting", "Connected", "Unreachable", "Connected"], Because(live));
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task AListenerThatAcceptsAndCloses_KeepsTheSourceInDiscoveryWithNoSetupFault()
	{
		using var listener = DemoScadaHost.ListenOnFreePort();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		var accepts = new AcceptCounter();
		using var stopAccepting = new CancellationTokenSource();
		var accepting = AcceptAndCloseAsync(listener, accepts, stopAccepting.Token);
		await using var live = new LiveSourceHost(port, LiveSourceHost.ShortGrace);

		live.Start();

		(await LiveSourceHost.WaitUntilAsync(() => accepts.Count >= 3, _lossBound)).Should().BeTrue(
			"discovery keeps dialling the listener; {0}",
			Because(live));
		Names(live).Should().Equal(["Connecting", "Unreachable"], Because(live));

		await stopAccepting.CancelAsync();
		listener.Stop();
		await accepting;
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task AListenerThatAcceptsAndNeverAnswers_IsDialledAgainAfterTheDiscoveryBound()
	{
		using var listener = DemoScadaHost.ListenOnFreePort();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		using var held = new HeldConnections();
		using var stopAccepting = new CancellationTokenSource();
		var accepting = AcceptAndHoldAsync(listener, held, stopAccepting.Token);
		await using var live = new LiveSourceHost(port, LiveSourceHost.ShortGrace);

		live.Start();

		(await LiveSourceHost.WaitUntilAsync(() => held.AcceptedUtc.Count >= 2, _secondDialBound)).Should().BeTrue(
			"discovery gives up on a silent peer and the next pass dials again; {0}",
			Because(live));
		var accepted = held.AcceptedUtc;
		(accepted[1] - accepted[0]).Should().BeGreaterThanOrEqualTo(
			_discoveryBound - _timerTolerance,
			"the first pass holds the silent peer for its whole bound; {0}",
			Because(live));
		Names(live).Should().Equal(["Connecting", "Unreachable"], Because(live));

		await stopAccepting.CancelAsync();
		listener.Stop();
		await accepting;
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task DisposingTheSourceWhileItReconnects_ReturnsWithinItsBound_PublishesNothing_AndDialsNoMore()
	{
		var server = await _host.StartAsync(DemoScadaHost.Options);
		var port = LiveSourceHost.PortOf(server);
		await using var live = new LiveSourceHost(port);

		try
		{
			live.Start();
			(await live.WaitForStatesAsync(2, _connectBound)).Should().BeTrue(Because(live));
			var hasValues = await LiveSourceHost.WaitUntilAsync(
				() => live.Primary.HasValuesForEach(LiveSourceHost.DemoPenIds), _lossBound);
			hasValues.Should().BeTrue(Because(live));
		}
		finally
		{
			await server.DisposeAsync();
		}

		(await live.WaitForStatesAsync(3, _lossBound)).Should().BeTrue(Because(live));
		var lossBreaks = live.Primary.Samples.Count(received => received.IsBreak);
		var batchesBeforeDispose = live.Primary.BatchCount;
		var watch = Stopwatch.StartNew();
		await live.Source.DisposeAsync();
		var elapsed = watch.Elapsed;

		elapsed.Should().BeLessThan(_closeBound + _poolSlack, Because(live));
		live.IsCompleted.Should().BeTrue(Because(live));
		Names(live).Should().Equal(["Connecting", "Connected", "Unreachable"], Because(live));
		lossBreaks.Should().Be(LiveSourceHost.DemoPenIds.Count, "the loss breaks every pen once; {0}", Because(live));

		using var listener = await DemoScadaHost.ListenOnPortOnceFreeAsync(port);
		var accepts = new AcceptCounter();
		using var stopAccepting = new CancellationTokenSource();
		var accepting = AcceptAndCloseAsync(listener, accepts, stopAccepting.Token);

		await Task.Delay(_preSessionCap + TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

		accepts.Count.Should().Be(0, "a disposed source dials nothing; {0}", Because(live));
		live.Primary.BatchCount.Should().Be(batchesBeforeDispose, "the dispose writes no break; {0}", Because(live));
		await stopAccepting.CancelAsync();
		listener.Stop();
		await accepting;
	}

	// Dispose returns at its 3 s bound whatever the close does, so completion is what tells a finished close from one
	// stuck on a continuation the context dropped.
	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task DisposeOnAConnectedSource_UnderAContextThatDropsEveryPost_ReturnsWithinItsBound()
	{
		await using var server = await _host.StartAsync(DemoScadaHost.Options);
		await using var live = new LiveSourceHost(LiveSourceHost.PortOf(server));
		live.Start();
		(await live.WaitForStatesAsync(2, _connectBound)).Should().BeTrue(Because(live));

		var elapsed = DisposeUnder(new DroppingSynchronizationContext(), live.Source);

		elapsed.Should().BeLessThan(_syncDisposeBound + _poolSlack, Because(live));
		(await LiveSourceHost.WaitUntilAsync(() => live.IsCompleted, _closeBound + _poolSlack)).Should().BeTrue(
			"the close finishes on the pool; {0}",
			Because(live));
	}

	private static TimeSpan DisposeUnder(SynchronizationContext context, IDisposable source)
	{
		var previous = SynchronizationContext.Current;
		SynchronizationContext.SetSynchronizationContext(context);

		try
		{
			var watch = Stopwatch.StartNew();
			source.Dispose();

			return watch.Elapsed;
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext(previous);
		}
	}

	private static async Task AcceptAndCloseAsync(
		TcpListener listener,
		AcceptCounter accepts,
		CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				using var client = await listener.AcceptTcpClientAsync(cancellationToken);
				accepts.Increment();
			}
		}
		catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException
			or SocketException)
		{
		}
	}

	private static async Task AcceptAndHoldAsync(
		TcpListener listener,
		HeldConnections held,
		CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				held.Add(await listener.AcceptTcpClientAsync(cancellationToken));
			}
		}
		catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException
			or SocketException)
		{
		}
	}

	private static IReadOnlyList<string> Names(LiveSourceHost live)
	{
		return [.. live.States.Select(state => state.Name)];
	}

	private string Because(LiveSourceHost live)
	{
		return $"{live.Describe()}; {_host.ServerLog()}";
	}

	private sealed class AcceptCounter
	{
		private int _count;

		public int Count => Volatile.Read(ref _count);

		public void Increment()
		{
			Interlocked.Increment(ref _count);
		}
	}

	/// <summary>
	/// Accepted connections the test keeps open and never answers, with the moment each was accepted.
	/// </summary>
	private sealed class HeldConnections : IDisposable
	{
		private readonly Lock _gate = new();
		private readonly List<TcpClient> _clients = [];
		private readonly List<DateTime> _acceptedUtc = [];

		public IReadOnlyList<DateTime> AcceptedUtc
		{
			get
			{
				lock (_gate)
				{
					return [.. _acceptedUtc];
				}
			}
		}

		public void Add(TcpClient client)
		{
			lock (_gate)
			{
				_clients.Add(client);
				_acceptedUtc.Add(DateTime.UtcNow);
			}
		}

		public void Dispose()
		{
			lock (_gate)
			{
				foreach (var client in _clients)
				{
					client.Dispose();
				}
			}
		}
	}

	private sealed class DroppingSynchronizationContext : SynchronizationContext
	{
		public override void Post(SendOrPostCallback d, object? state)
		{
		}
	}
}
