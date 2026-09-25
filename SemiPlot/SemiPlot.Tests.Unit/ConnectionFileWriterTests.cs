using AwesomeAssertions;

using SemiPlot.DataSource.Postgres.Configuration;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Unit;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ConnectionFileWriterTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("semiplot-connection-file-").FullName;

	public void Dispose()
	{
		Directory.Delete(_directory, recursive: true);
	}

	[Fact]
	public async Task TheWrittenFileRoundTripsThroughTheLoader()
	{
		await ConnectionFileWriter.WriteAsync(
			_directory,
			"10.20.30.40",
			5433,
			"semiplot_dev",
			"semiplot_reader",
			"s3cret",
			TimeSpan.FromSeconds(1),
			TestContext.Current.CancellationToken);

		var result = PostgresConnectionLoader.Load(
			Path.Combine(_directory, ConnectionFileWriter.DirectoryName));

		result.IsSuccess.Should().BeTrue();
		result.Value.Host.Should().Be("10.20.30.40");
		result.Value.Port.Should().Be(5433);
		result.Value.Database.Should().Be("semiplot_dev");
		result.Value.Username.Should().Be("semiplot_reader");
		result.Value.Password.Should().Be("s3cret");
		result.Value.PollInterval.Should().Be(TimeSpan.FromSeconds(1));
	}

	[Fact]
	public async Task TheSectionDirectoryIsCreatedWhenItDoesNotExist()
	{
		var nested = Path.Combine(_directory, "nested");

		await ConnectionFileWriter.WriteAsync(
			nested, "127.0.0.1", 5432, "db", "user", "pw", TimeSpan.FromSeconds(1),
			TestContext.Current.CancellationToken);

		File.Exists(Path.Combine(nested, ConnectionFileWriter.DirectoryName, ConnectionFileWriter.FileName))
			.Should().BeTrue();
	}
}
