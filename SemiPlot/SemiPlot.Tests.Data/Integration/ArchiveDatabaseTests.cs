using Npgsql;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The template-and-clone lifecycle. What each test proves reaches back through the whole chain: a
// container came up, `semibase create` provisioned it, ArchiveWriter seeded it as scada_writer, and a
// clone carried the result. The assertions on what the rows actually are belong to the next task.
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

	[Fact]
	public async Task AnEmptyDatabaseCarriesNoArchive()
	{
		postgresContainerFixture.RequireAvailable();

		await using var database = await postgresContainerFixture.CreateEmptyDatabaseAsync(
			TestContext.Current.CancellationToken);

		Assert.False(
			await ScalarAsync<bool>(database.AdminConnectionString, ArchiveWriter.ArchiveExistsCommand));
	}

	// EmptyAsync states encoding and locale rather than inheriting the server's template1, so that a
	// test asking for an empty database gets the same one on every machine. That is a guarantee only if
	// it is read back.
	[Fact]
	public async Task AnEmptyDatabaseCarriesTheStatedEncodingAndLocale()
	{
		postgresContainerFixture.RequireAvailable();

		await using var database = await postgresContainerFixture.CreateEmptyDatabaseAsync(
			TestContext.Current.CancellationToken);

		var settings = await SettingsAsync(database.Name);

		Assert.Equal(("UTF8", "C", "C"), settings);
	}

	private async Task<(string Encoding, string Collate, string Ctype)> SettingsAsync(string name)
	{
		await using var connection = new NpgsqlConnection(postgresContainerFixture.Server.AdminConnectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		await using var command = new NpgsqlCommand(
			"SELECT pg_encoding_to_char(encoding), datcollate, datctype FROM pg_database WHERE datname = @name;",
			connection);

		command.Parameters.AddWithValue("name", name);

		await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

		Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken), $"'{name}' does not exist.");

		return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
	}

	// Disposal drops what the fixture created and nothing else, which is the only place in this slice
	// where a database is dropped at all.
	[Fact]
	public async Task TheDatabaseIsGoneAfterDisposal()
	{
		postgresContainerFixture.RequireAvailable();

		var database = await postgresContainerFixture.CreateEmptyDatabaseAsync(
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
