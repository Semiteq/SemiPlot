using AwesomeAssertions;

using Npgsql;

using Xunit;

namespace SemiPlot.Tests.Integration;

// The template-and-clone lifecycle. What each test proves reaches back through the whole chain: a
// container came up already provisioned by its own image, the template cloned that source,
// ArchiveWriter seeded it as scada_writer, and a clone carried the result.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class ArchiveDatabaseTests(PostgresContainerFixture postgresContainerFixture)
{
	private const string CountDatabasesCommand = "SELECT count(*) FROM pg_database WHERE datname = @name;";

	[Fact]
	public async Task ACloneCarriesTheSeededRows()
	{
		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		var connectionString = database.AdminConnectionString;

		var trends = await ScalarAsync<long>(connectionString, "SELECT count(*) FROM public.trends;");
		var tags = await ScalarAsync<long>(connectionString, "SELECT count(*) FROM public.semiplot_tags;");

		(trends > 0).Should().BeTrue($"the clone holds {trends} archive rows.");
		(tags > 0).Should().BeTrue($"the clone holds {tags} tag rows.");
	}

	[Fact]
	public async Task TheDatabaseIsGoneAfterDisposal()
	{
		var database = await postgresContainerFixture.CloneProvisionedAsync(
			TestContext.Current.CancellationToken);

		(await CountDatabasesAsync(database.Name)).Should().Be(1L);

		await database.DisposeAsync();

		(await CountDatabasesAsync(database.Name)).Should().Be(0L);
	}

	private async Task<long> CountDatabasesAsync(string name)
	{
		await using var connection = new NpgsqlConnection(postgresContainerFixture.Server.AdminConnectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		await using var command = new NpgsqlCommand(CountDatabasesCommand, connection);

		command.Parameters.AddWithValue("name", name);

		return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
	}

	private static async Task<T> ScalarAsync<T>(string connectionString, string statement)
	{
		await using var connection = new NpgsqlConnection(connectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		await using var command = new NpgsqlCommand(statement, connection);

		return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
	}
}
