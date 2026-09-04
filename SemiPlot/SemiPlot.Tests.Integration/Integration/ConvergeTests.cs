using AwesomeAssertions;

using Npgsql;

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
			ArchiveTemplate.Slice.ChangeSeconds);

		try
		{
			var first = await Converge.RunAsync(options, TestContext.Current.CancellationToken);

			Console.WriteLine($"readiness wait: {first.ReadinessWait}");

			first.RowsWritten.Should().Be(ExpectedRowCount());
			first.TagsWritten.Should().Be(ArchiveTemplate.Slice.PenCount);
			(await RowCountAsync(server, database)).Should().Be(ExpectedRowCount());
			(await TagCountAsync(server, database)).Should().Be(ArchiveTemplate.Slice.PenCount);

			var loaded = PostgresConnectionLoader.Load(Path.Combine(configDirectory, ConnectionFileWriter.FileName));

			loaded.IsSuccess.Should().BeTrue();
			loaded.Value.Database.Should().Be(database);
			loaded.Value.Username.Should().Be(BenchRoles.ReaderRole);
			loaded.Value.SourceTimeZone.Id.Should().Be(TimeZoneInfo.Local.Id);

			var oidBefore = await DatabaseOidAsync(server, database);

			await using var held = new NpgsqlConnection(server.ReaderConnectionStringFor(database));

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
		return ScalarAsync<long>(server.ReaderConnectionStringFor(database), "SELECT count(*) FROM public.trends;");
	}

	private static Task<long> TagCountAsync(PostgresServer server, string database)
	{
		return ScalarAsync<long>(
			server.AdminConnectionStringFor(database), "SELECT count(*) FROM public.semiplot_tags;");
	}

	private static Task<uint> DatabaseOidAsync(PostgresServer server, string database)
	{
		return ScalarAsync<uint>(server.AdminConnectionString, $"SELECT oid FROM pg_database WHERE datname = '{database}';");
	}

	private static async Task<T> ScalarAsync<T>(string connectionString, string statement)
	{
		await using var connection = new NpgsqlConnection(connectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		await using var command = new NpgsqlCommand(statement, connection);

		return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
	}
}
