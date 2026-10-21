using AwesomeAssertions;

using SemiPlot.DataSource.OpcUa.Configuration;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

using static SemiPlot.Tests.Unit.ResultText;

namespace SemiPlot.Tests.Unit;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class OpcFileWriterTests : IDisposable
{
	private readonly TemporaryDirectory _directory = new("semiplot-opc-file");

	public void Dispose()
	{
		_directory.Dispose();
	}

	[Fact]
	public async Task TheWrittenFileLoadsThroughTheOpcLoader()
	{
		await OpcFileWriter.WriteAsync(_directory, "10.20.30.40", 55875, TestContext.Current.CancellationToken);

		var result = OpcUaConnectionLoader.Load(Path.Combine(_directory, OpcFileWriter.DirectoryName));

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Host.Should().Be("10.20.30.40");
		result.Value.Port.Should().Be(55875);
	}

	[Fact]
	public async Task TheSectionDirectoryIsCreatedWhenItDoesNotExist()
	{
		var nested = Path.Combine(_directory, "nested");

		await OpcFileWriter.WriteAsync(nested, "127.0.0.1", 55875, TestContext.Current.CancellationToken);

		File.Exists(OpcFileWriter.PathFor(nested)).Should().BeTrue();
	}
}
