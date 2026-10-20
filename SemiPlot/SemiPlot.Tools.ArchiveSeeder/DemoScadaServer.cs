using System.Runtime.ExceptionServices;

using Microsoft.Extensions.Logging;

using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

using SemiPlot.DataSource.Postgres;

namespace SemiPlot.Tools.ArchiveSeeder;

/// <summary>
/// A Simple-Scada-shaped OPC UA server on loopback that serves the lattice the demo writer archives.
/// </summary>
public sealed class DemoScadaServer : IAsyncDisposable
{
	private const string ApplicationName = "SemiPlotDemoScada";

	private readonly StandardServer _server;
	private readonly CancellationTokenSource _stopping;
	private int _disposed;

	private DemoScadaServer(
		StandardServer server,
		CancellationTokenSource stopping,
		string endpointUrl,
		Task completion)
	{
		_server = server;
		_stopping = stopping;
		EndpointUrl = endpointUrl;
		Completion = completion;
	}

	public string EndpointUrl { get; }

	/// <summary>
	/// The publishing loop: it completes successfully only when <see cref="DisposeAsync"/> cancels it; a fault
	/// in the loop faults it.
	/// </summary>
	public Task Completion { get; }

	public static async Task<DemoScadaServer> StartAsync(
		FollowOptions options,
		TimeZoneInfo archiveZone,
		string certificateDirectory,
		ILoggerFactory loggerFactory)
	{
		var telemetry = new Telemetry(loggerFactory);
		var endpointUrl = $"opc.tcp://127.0.0.1:{options.OpcPort}";
		var configuration = CreateConfiguration(endpointUrl, certificateDirectory, telemetry);

		await configuration.ValidateAsync(ApplicationType.Server, CancellationToken.None);
		var certificateUsable = await new ApplicationInstance(configuration, telemetry)
			.CheckApplicationInstanceCertificatesAsync(true, null, CancellationToken.None);

		if (!certificateUsable)
		{
			throw new SeederException(
				$"The OPC UA application certificate under {certificateDirectory} is not usable; delete the " +
				"directory and start again.");
		}

		var converter = new ArchiveTimeConverter(archiveZone);
		var startUtc = DateTime.UtcNow;
		var startLocal = converter.ToArchiveLocal(startUtc);
		var factory = new DemoScadaNodeManager.Factory(options, converter, startUtc, startLocal);
		var server = new StandardServer(telemetry);
		server.AddNodeManager(factory);
		DemoScadaNodeManager manager;

		try
		{
			await server.StartAsync(configuration, CancellationToken.None);
			manager = factory.Manager
				?? throw new InvalidOperationException("The OPC UA server started without its node manager.");
		}
		catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadNoCommunication)
		{
			await server.DisposeAsync();

			throw new SeederException(
				$"OPC UA port {options.OpcPort} is in use on 127.0.0.1; a running Simple-Scada holds " +
				$"{FollowOptions.SimpleScadaPort} by default. Pass a free --opc-port.",
				exception);
		}
		catch
		{
			await server.DisposeAsync();

			throw;
		}

		var stopping = new CancellationTokenSource();
		var completion = PublishAsync(manager, options, converter, startLocal, stopping.Token);

		return new DemoScadaServer(server, stopping, endpointUrl, completion);
	}

	/// <summary>Stops the loop, stops the server and disposes it; a second call does nothing.</summary>
	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		var faults = new List<Exception>();

		try
		{
			await _stopping.CancelAsync();
			await Completion;
		}
		catch (Exception exception)
		{
			faults.Add(exception);
		}

		try
		{
			await _server.StopAsync(CancellationToken.None);
		}
		catch (Exception exception)
		{
			faults.Add(exception);
		}

		try
		{
			await _server.DisposeAsync();
		}
		catch (Exception exception)
		{
			faults.Add(exception);
		}

		_stopping.Dispose();

		if (faults.Count == 1)
		{
			ExceptionDispatchInfo.Throw(faults[0]);
		}

		if (faults.Count > 1)
		{
			throw new AggregateException(faults);
		}
	}

	private static ApplicationConfiguration CreateConfiguration(
		string endpointUrl,
		string certificateDirectory,
		ITelemetryContext telemetry)
	{
		return new ApplicationConfiguration(telemetry)
		{
			ApplicationName = ApplicationName,
			ApplicationUri = $"urn:localhost:{ApplicationName}",
			ApplicationType = ApplicationType.Server,
			SecurityConfiguration = new SecurityConfiguration
			{
				ApplicationCertificate = new CertificateIdentifier
				{
					StoreType = CertificateStoreType.Directory,
					StorePath = Path.Combine(certificateDirectory, "own"),
					SubjectName = $"CN={ApplicationName}",
				},
				TrustedIssuerCertificates = new CertificateTrustList
				{
					StoreType = CertificateStoreType.Directory,
					StorePath = Path.Combine(certificateDirectory, "issuer"),
				},
				TrustedPeerCertificates = new CertificateTrustList
				{
					StoreType = CertificateStoreType.Directory,
					StorePath = Path.Combine(certificateDirectory, "trusted"),
				},
				RejectedCertificateStore = new CertificateStoreIdentifier
				{
					StoreType = CertificateStoreType.Directory,
					StorePath = Path.Combine(certificateDirectory, "rejected"),
				},
			},
			TransportQuotas = new TransportQuotas(),
			ServerConfiguration = new ServerConfiguration
			{
				BaseAddresses = [endpointUrl],
				SecurityPolicies =
				[
					new ServerSecurityPolicy
					{
						SecurityMode = MessageSecurityMode.None,
						SecurityPolicyUri = SecurityPolicies.None,
					},
					new ServerSecurityPolicy
					{
						SecurityMode = MessageSecurityMode.Sign,
						SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
					},
					new ServerSecurityPolicy
					{
						SecurityMode = MessageSecurityMode.SignAndEncrypt,
						SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
					},
				],
				UserTokenPolicies = [new UserTokenPolicy(UserTokenType.Anonymous)],
				MaxRegistrationInterval = 0,
			},
		};
	}

	/// <summary>Writes the newest lattice change, once per instant, until the token is cancelled.</summary>
	private static async Task PublishAsync(
		DemoScadaNodeManager manager,
		FollowOptions options,
		ArchiveTimeConverter converter,
		DateTime startLocal,
		CancellationToken cancellationToken)
	{
		var intervalTicks = RawLayerGenerator.ChangeIntervalTicks(options.ChangeSeconds);
		var servedTicks = RawLayerGenerator.LatestChangeTicks(startLocal.Ticks, intervalTicks);

		try
		{
			while (true)
			{
				var now = converter.ToArchiveLocal(DateTime.UtcNow);
				var changeTicks = RawLayerGenerator.LatestChangeTicks(now.Ticks, intervalTicks);

				if (changeTicks > servedTicks)
				{
					manager.Write(LiveTailGenerator.ChangesAt(options, now));
					servedTicks = changeTicks;
				}

				var untilNextChange = TimeSpan.FromTicks(changeTicks + intervalTicks - now.Ticks);
				var wholeMilliseconds = Math.Ceiling(untilNextChange.TotalMilliseconds);
				await Task.Delay(TimeSpan.FromMilliseconds(wholeMilliseconds), cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	private sealed class Telemetry(ILoggerFactory loggerFactory) : TelemetryContextBase(loggerFactory);
}
