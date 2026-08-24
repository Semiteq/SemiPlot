using Npgsql;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The template-and-clone lifecycle. What each test proves reaches back through the whole chain: a
// container came up already provisioned by its own image, the template cloned that source,
// ArchiveWriter seeded it as scada_writer, and a clone carried the result. What the rows themselves
// are is asserted by SeededArchiveTests.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class ArchiveDatabaseTests(PostgresContainerFixture postgresContainerFixture)
{
	[Fact]
	public async Task ACloneCarriesTheSeededRows()
	{
		postgresContainerFixture.RequireAvailable();

		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		var connectionString = database.AdminConnectionString;

		var trends = await ScalarAsync<long>(connectionString, "SELECT count(*) FROM public.trends;");
		var tags = await ScalarAsync<long>(connectionString, "SELECT count(*) FROM public.semiplot_tags;");

		Assert.True(trends > 0, $"the clone holds {trends} archive rows.");
		Assert.True(tags > 0, $"the clone holds {tags} tag rows.");
	}

	// Disposal drops what the fixture created and nothing else, which is the only place in this suite
	// where a database is dropped at all.
	[Fact]
	public async Task TheDatabaseIsGoneAfterDisposal()
	{
		postgresContainerFixture.RequireAvailable();

		var database = await postgresContainerFixture.CloneProvisionedAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(1L, await CountDatabasesAsync(database.Name));

		await database.DisposeAsync();

		Assert.Equal(0L, await CountDatabasesAsync(database.Name));
	}

	private async Task<long> CountDatabasesAsync(string name)
	{
		await using var connection = new NpgsqlConnection(postgresContainerFixture.Server.AdminConnectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		await using var command = new NpgsqlCommand(ArchiveDatabase.CountDatabasesCommand, connection);

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
