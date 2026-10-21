using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Opc.Ua;

using SemiPlot.DataSource.OpcUa;
using SemiPlot.DataSource.OpcUa.Configuration;

using Xunit;

namespace SemiPlot.Tests.Unit.OpcUa;

// The store root sits under a temp directory, never under the tracked configuration set.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ClientApplicationConfigurationTests : IDisposable
{
	private readonly TemporaryDirectory _directory = new("semiplot-opc-client");

	public void Dispose()
	{
		_directory.Dispose();
	}

	private string StoreRoot => Path.Combine(_directory, OpcUaConnectionLoader.CertificateStoreDirectoryName);

	[Fact]
	public async Task TheConfigurationValidatesWithTheClientsIdentityAndTimeouts()
	{
		var configuration = await CreateAsync();

		configuration.ApplicationType.Should().Be(ApplicationType.Client);
		configuration.ApplicationName.Should().Be("SemiPlot");
		configuration.ApplicationUri.Should().Be("urn:localhost:SemiPlot");
		configuration.TransportQuotas.Should().NotBeNull();
		configuration.TransportQuotas.OperationTimeout.Should().Be(15000);
		configuration.ClientConfiguration.Should().NotBeNull();
		configuration.ClientConfiguration.DefaultSessionTimeout.Should().Be(60000);
	}

	[Fact]
	public async Task TheFourStoresSitUnderTheSettingsStoreRoot()
	{
		var security = (await CreateAsync()).SecurityConfiguration;

		security.Should().NotBeNull();
		security.ApplicationCertificate.Should().NotBeNull();
		security.ApplicationCertificate.StoreType.Should().Be(CertificateStoreType.Directory);
		security.ApplicationCertificate.StorePath.Should().Be(Path.Combine(StoreRoot, "own"));
		security.ApplicationCertificate.SubjectName.Should().Be("CN=SemiPlot");
		security.TrustedIssuerCertificates.Should().NotBeNull();
		security.TrustedIssuerCertificates.StorePath.Should().Be(Path.Combine(StoreRoot, "issuer"));
		security.TrustedPeerCertificates.Should().NotBeNull();
		security.TrustedPeerCertificates.StorePath.Should().Be(Path.Combine(StoreRoot, "trusted"));
		security.RejectedCertificateStore.Should().NotBeNull();
		security.RejectedCertificateStore.StorePath.Should().Be(Path.Combine(StoreRoot, "rejected"));
	}

	[Fact]
	public async Task ValidationCreatesNoFileOrFolder()
	{
		await CreateAsync();

		Directory.EnumerateFileSystemEntries(_directory).Should().BeEmpty();
	}

	private Task<ApplicationConfiguration> CreateAsync()
	{
		var settings = new OpcUaConnectionSettings("127.0.0.1", 4841, StoreRoot);

		return ClientApplicationConfiguration.CreateValidatedAsync(
			settings,
			new StackTelemetry(NullLoggerFactory.Instance),
			TestContext.Current.CancellationToken);
	}
}
