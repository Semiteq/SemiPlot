using System.Net;
using System.Net.Sockets;

using SemiPlot.Tests.Integration.DemoScada;

namespace SemiPlot.Tests.Integration.OpcUa;

/// <summary>
/// A loopback TCP relay to one server port that a test can cut: a cut closes every relayed connection and closes
/// each new one at once until the relay resumes, while the server keeps running.
/// </summary>
internal sealed class LoopbackRelay : IAsyncDisposable
{
	private readonly TcpListener _listener;
	private readonly int _serverPort;
	private readonly CancellationTokenSource _stopping = new();
	private readonly Lock _gate = new();
	private readonly List<TcpClient> _open = [];
	private readonly List<Task> _relays = [];
	private readonly Task _accepting;
	private bool _isCut;

	private LoopbackRelay(TcpListener listener, int serverPort)
	{
		_listener = listener;
		_serverPort = serverPort;
		_accepting = AcceptAsync(_stopping.Token);
	}

	public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

	public static LoopbackRelay Start(int serverPort)
	{
		return new LoopbackRelay(DemoScadaHost.ListenOnFreePort(), serverPort);
	}

	public void Cut()
	{
		lock (_gate)
		{
			_isCut = true;

			foreach (var client in _open)
			{
				client.Dispose();
			}

			_open.Clear();
		}
	}

	public void Resume()
	{
		lock (_gate)
		{
			_isCut = false;
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _stopping.CancelAsync();
		_listener.Stop();
		Cut();
		await _accepting;

		Task[] relays;

		lock (_gate)
		{
			relays = [.. _relays];
		}

		await Task.WhenAll(relays);
		_stopping.Dispose();
	}

	private async Task AcceptAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				var client = await _listener.AcceptTcpClientAsync(cancellationToken);
				var relay = RelayAsync(client, cancellationToken);

				lock (_gate)
				{
					_relays.Add(relay);
				}
			}
		}
		catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException
			or SocketException)
		{
		}
	}

	private async Task RelayAsync(TcpClient client, CancellationToken cancellationToken)
	{
		using var server = new TcpClient();

		using (client)
		{
			if (!TryOpen(client, server))
			{
				return;
			}

			Task[] copies = [];

			try
			{
				await server.ConnectAsync(IPAddress.Loopback, _serverPort, cancellationToken);
				var clientStream = client.GetStream();
				var serverStream = server.GetStream();
				copies =
				[
					clientStream.CopyToAsync(serverStream, cancellationToken),
					serverStream.CopyToAsync(clientStream, cancellationToken),
				];
				await Task.WhenAny(copies);
			}
			catch (Exception exception) when (IsClosedSocket(exception))
			{
			}
			finally
			{
				Close(client, server);
			}

			try
			{
				await Task.WhenAll(copies);
			}
			catch (Exception exception) when (IsClosedSocket(exception))
			{
			}
		}
	}

	private static bool IsClosedSocket(Exception exception)
	{
		return exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException
			or InvalidOperationException;
	}

	/// <summary>False while the relay is cut: the connection is closed at once.</summary>
	private bool TryOpen(TcpClient client, TcpClient server)
	{
		lock (_gate)
		{
			if (_isCut)
			{
				return false;
			}

			_open.Add(client);
			_open.Add(server);

			return true;
		}
	}

	private void Close(TcpClient client, TcpClient server)
	{
		lock (_gate)
		{
			_open.Remove(client);
			_open.Remove(server);
		}

		client.Dispose();
		server.Dispose();
	}
}
