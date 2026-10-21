using Opc.Ua;

using SemiPlot.DataSource.OpcUa.Configuration;

namespace SemiPlot.DataSource.OpcUa;

/// <summary>
/// The client's validated application configuration. Its certificate stores sit under the settings' store root
/// and stay empty: no application certificate is loaded or created
/// (docs/architecture/data-integration.md#the-opc-ua-live-source).
/// </summary>
internal static class ClientApplicationConfiguration
{
	public const string ApplicationName = "SemiPlot";

	public const string ApplicationUri = "urn:localhost:SemiPlot";

	private const int OperationTimeoutMilliseconds = 15000;

	public const int SessionTimeoutMilliseconds = 60000;

	public static async Task<ApplicationConfiguration> CreateValidatedAsync(
		OpcUaConnectionSettings settings,
		ITelemetryContext telemetry,
		CancellationToken token)
	{
		var configuration = Create(settings.CertificateStoreRoot, telemetry);
		await configuration.ValidateAsync(ApplicationType.Client, token).ConfigureAwait(false);

		return configuration;
	}

	private static ApplicationConfiguration Create(string storeRoot, ITelemetryContext telemetry)
	{
		return new ApplicationConfiguration(telemetry)
		{
			ApplicationName = ApplicationName,
			ApplicationUri = ApplicationUri,
			ApplicationType = ApplicationType.Client,
			SecurityConfiguration = new SecurityConfiguration
			{
				ApplicationCertificate = new CertificateIdentifier
				{
					StoreType = CertificateStoreType.Directory,
					StorePath = Path.Combine(storeRoot, "own"),
					SubjectName = $"CN={ApplicationName}",
				},
				TrustedIssuerCertificates = new CertificateTrustList
				{
					StoreType = CertificateStoreType.Directory,
					StorePath = Path.Combine(storeRoot, "issuer"),
				},
				TrustedPeerCertificates = new CertificateTrustList
				{
					StoreType = CertificateStoreType.Directory,
					StorePath = Path.Combine(storeRoot, "trusted"),
				},
				RejectedCertificateStore = new CertificateStoreIdentifier
				{
					StoreType = CertificateStoreType.Directory,
					StorePath = Path.Combine(storeRoot, "rejected"),
				},
			},
			TransportQuotas = new TransportQuotas { OperationTimeout = OperationTimeoutMilliseconds },
			ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = SessionTimeoutMilliseconds },
		};
	}
}
