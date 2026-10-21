using System.Net;

using AwesomeAssertions;

using SemiPlot.Core.Data.Errors;
using SemiPlot.Tests.Integration.DemoScada;
using SemiPlot.UI;
using SemiPlot.UI.Startup;

using Serilog.Events;

using Xunit;

namespace SemiPlot.Tests.Integration;

// Opens a loopback socket toward a port nothing answers and no container, so it runs without a container
// runtime.
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Integration")]
public sealed class StartupProbeOpcSectionTests : IDisposable
{
	private readonly string _configDirectory =
		Path.Combine(Path.GetTempPath(), $"semiplot-probe-opc-{Guid.NewGuid():N}");

	public void Dispose()
	{
		Directory.Delete(_configDirectory, recursive: true);
	}

	[Fact]
	public void AValidOpcSection_PassesTheStartToTheArchiveReadAndOpensNoOpcSocket()
	{
		using var opcPort = DemoScadaHost.ListenOnFreePort();
		var port = ((IPEndPoint)opcPort.LocalEndpoint).Port;
		WriteSection(
			StartupProbe.ConnectionDirectoryName,
			"connection.yaml",
			"host: 127.0.0.1\nport: 1\ndatabase: semiplot\nuser: semiplot\npassword: unused\n");
		WriteSection(StartupProbe.OpcDirectoryName, "opc.yaml", $"host: 127.0.0.1\nport: {port}\n");

		var result = StartupProbe.Run(
			new StartupOptions(_configDirectory, Path.Combine(_configDirectory, "semiplot.log"), LogEventLevel.Fatal));

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle().Which.Should().BeOfType<ArchiveError>()
			.Which.Kind.Should().Be(ArchiveFault.Unreachable);
		opcPort.Pending().Should().BeFalse("the probe connects to nothing but the archive");
	}

	private void WriteSection(string section, string fileName, string content)
	{
		var directory = Path.Combine(_configDirectory, section);

		Directory.CreateDirectory(directory);
		File.WriteAllText(Path.Combine(directory, fileName), content);
	}
}
