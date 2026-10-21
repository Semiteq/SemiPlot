using System.Net;
using System.Net.Sockets;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using Opc.Ua;

using SemiPlot.Tools.ArchiveSeeder;

namespace SemiPlot.Tests.Integration.DemoScada;

/// <summary>Starts demo servers over one temporary certificate directory and one log collector.</summary>
public sealed class DemoScadaHost : IDisposable
{
	private const int StartAttempts = 3;

	// 4840 is where the stack binds when asked for port 0, and 49875 is the real Simple-Scada's port.
	private static readonly int[] _reservedPorts = [4840, 49875];

	// A port picked free can be taken in the meantime by an outgoing socket, which lets go of it within seconds.
	private static readonly TimeSpan _busyPortWait = TimeSpan.FromSeconds(1);

	private readonly ILoggerFactory _loggerFactory;

	public DemoScadaHost()
	{
		CertificateDirectory = Path.Combine(
			Path.GetTempPath(),
			"SemiPlot.Tests",
			$"DemoScada-{Guid.NewGuid():N}");
		_loggerFactory = LoggerFactory.Create(builder => builder
			.AddProvider(new FakeLoggerProvider(Logs))
			.SetMinimumLevel(LogLevel.Warning));
	}

	/// <summary>The zone every test server archives in: no DST, and nine hours off a UTC test machine.</summary>
	public static TimeZoneInfo ArchiveZone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

	/// <summary><see cref="StartAsync"/> picks the port.</summary>
	public static FollowOptions Options { get; } = new(
		"Host=127.0.0.1;Database=unused",
		TimeSpan.FromSeconds(1),
		PenCount: 8,
		Seed: 1,
		ChangeSeconds: 0.5,
		ArchiveLag: TimeSpan.Zero,
		OpcPort: 0);

	public string CertificateDirectory { get; }

	public FakeLogCollector Logs { get; } = new();

	/// <summary>Starts a server on a free loopback port, retrying when another process takes the port first.</summary>
	public async Task<DemoScadaServer> StartAsync(FollowOptions options)
	{
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				return await StartOnPortAsync(options with { OpcPort = FreePort() });
			}
			catch (SeederException exception) when (attempt < StartAttempts && IsBusyPort(exception))
			{
			}
		}
	}

	private static bool IsBusyPort(SeederException exception)
	{
		return exception.InnerException is ServiceResultException listener
			&& listener.StatusCode == StatusCodes.BadNoCommunication;
	}

	public Task<DemoScadaServer> StartOnPortAsync(FollowOptions options)
	{
		return DemoScadaServer.StartAsync(options, ArchiveZone, CertificateDirectory, _loggerFactory);
	}

	/// <summary>
	/// Starts a server on a port a source already dials, waiting while another socket still holds it.
	/// </summary>
	public async Task<DemoScadaServer> StartOnPortOnceFreeAsync(FollowOptions options)
	{
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				return await StartOnPortAsync(options);
			}
			catch (SeederException exception) when (attempt < StartAttempts && IsBusyPort(exception))
			{
				await Task.Delay(_busyPortWait);
			}
		}
	}

	/// <summary>A port nothing listens on now; another socket may take it before the caller binds it.</summary>
	public static int FreePort()
	{
		using var listener = ListenOnFreePort();

		return ((IPEndPoint)listener.LocalEndpoint).Port;
	}

	/// <summary>A started loopback listener on a free port that is not reserved.</summary>
	public static TcpListener ListenOnFreePort()
	{
		while (true)
		{
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();

			if (!_reservedPorts.Contains(((IPEndPoint)listener.LocalEndpoint).Port))
			{
				return listener;
			}

			listener.Dispose();
		}
	}

	/// <summary>A started loopback listener on a port a source dials, waiting while another socket holds it.</summary>
	public static async Task<TcpListener> ListenOnPortOnceFreeAsync(int port)
	{
		for (var attempt = 1; ; attempt++)
		{
			var listener = new TcpListener(IPAddress.Loopback, port);

			try
			{
				listener.Start();

				return listener;
			}
			catch (SocketException exception) when (attempt < StartAttempts
				&& exception.SocketErrorCode == SocketError.AddressAlreadyInUse)
			{
				listener.Dispose();
				await Task.Delay(_busyPortWait);
			}
		}
	}

	/// <summary>The server's records at Warning and above so far, for an assertion's reason.</summary>
	public string ServerLog()
	{
		var records = Logs.GetSnapshot();

		return records.Count == 0
			? "the server logged nothing at Warning or above"
			: "the server logged " + LogRecordLines.Of(records);
	}

	public void Dispose()
	{
		_loggerFactory.Dispose();

		if (Directory.Exists(CertificateDirectory))
		{
			Directory.Delete(CertificateDirectory, recursive: true);
		}
	}
}
