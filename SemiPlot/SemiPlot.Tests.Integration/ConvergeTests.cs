using AwesomeAssertions;

using Npgsql;

using SemiPlot.DataSource.OpcUa.Configuration;
using SemiPlot.DataSource.Postgres.Configuration;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

// Converge is exercised directly rather than through the CLI: ConvergeOptions is its whole surface,
// and this is what the AppHost and a developer running it by hand both call.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class ConvergeTests(PostgresContainerFixture postgresContainerFixture)
{
	// Only written into opc/opc.yaml; nothing dials it.
	private const int OpcPort = 55875;

	[Fact]
	public async Task ConvergeCreatesSeedsWritesTheFileAndRecreatesOnASecondRun()
	{
		var server = postgresContainerFixture.Server;
		var database = "semiplot_converge_" + Guid.NewGuid().ToString("N")[..12];
		var configDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		var options = new ConvergeOptions(
			server.WriterConnectionStringFor(database),
			server.AdminConnectionString,
			configDirectory,
			ArchiveTemplate.Slice.End,
			ArchiveTemplate.Slice.ChangeSeconds,
			OpcPort);

		try
		{
			var first = await Converge.RunAsync(options, TestContext.Current.CancellationToken);

			TestContext.Current.TestOutputHelper?.WriteLine($"readiness wait: {first.ReadinessWait}");

			first.RowsWritten.Should().Be(ExpectedRowCount());
			first.TagsWritten.Should().Be(ArchiveTemplate.Slice.PenCount);
			(await RowCountAsync(server, database)).Should().Be(ExpectedRowCount());
			(await TagCountAsync(server, database)).Should().Be(ArchiveTemplate.Slice.PenCount);

			File.Exists(ConnectionFileWriter.PathFor(configDirectory)).Should().BeTrue();

			var loaded = PostgresConnectionLoader.Load(
				Path.Combine(configDirectory, ConnectionFileWriter.DirectoryName));

			loaded.IsSuccess.Should().BeTrue();
			loaded.Value.Database.Should().Be(database);
			loaded.Value.Username.Should().Be(BenchRoles.PlotRole);

			var opc = OpcUaConnectionLoader.Load(Path.Combine(configDirectory, OpcFileWriter.DirectoryName));

			opc.IsSuccess.Should().BeTrue();
			opc.Value.Host.Should().Be(DemoScadaServer.Host);
			opc.Value.Port.Should().Be(OpcPort);

			var oidBefore = await DatabaseOidAsync(server, database);

			await using var held = new NpgsqlConnection(server.PlotConnectionStringFor(database));

			await held.OpenAsync(TestContext.Current.CancellationToken);

			var second = await Converge.RunAsync(options, TestContext.Current.CancellationToken);

			second.RowsWritten.Should().Be(first.RowsWritten);
			(await DatabaseOidAsync(server, database)).Should().NotBe(oidBefore);

			var brokenRead = await Record.ExceptionAsync(async () =>
				await new NpgsqlCommand("SELECT 1;", held).ExecuteScalarAsync(TestContext.Current.CancellationToken));

			brokenRead.Should().NotBeNull();
		}
		finally
		{
			await ArchiveDatabase.ExecuteAsync(
				server.AdminConnectionString,
				$"""DROP DATABASE IF EXISTS "{database}" WITH (FORCE);""",
				TestContext.Current.CancellationToken);

			if (Directory.Exists(configDirectory))
			{
				Directory.Delete(configDirectory, recursive: true);
			}
		}
	}

	private static long ExpectedRowCount()
	{
		var rawRows = RawLayerGenerator.Generate(ArchiveTemplate.Slice);

		return rawRows.Count + LayerThinner.ThinAll(rawRows).Count;
	}

	private static Task<long> RowCountAsync(PostgresServer server, string database)
	{
		return ArchiveDatabase.ScalarAsync<long>(
			server.PlotConnectionStringFor(database),
			"SELECT count(*) FROM public.trends;",
			cancellationToken: TestContext.Current.CancellationToken);
	}

	private static Task<long> TagCountAsync(PostgresServer server, string database)
	{
		return ArchiveDatabase.ScalarAsync<long>(
			server.AdminConnectionStringFor(database),
			"SELECT count(*) FROM public.semiplot_tags;",
			cancellationToken: TestContext.Current.CancellationToken);
	}

	private static Task<uint> DatabaseOidAsync(PostgresServer server, string database)
	{
		return ArchiveDatabase.ScalarAsync<uint>(
			server.AdminConnectionString,
			$"SELECT oid FROM pg_database WHERE datname = '{database}';",
			cancellationToken: TestContext.Current.CancellationToken);
	}
}
