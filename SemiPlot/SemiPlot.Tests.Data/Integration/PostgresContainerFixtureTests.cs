using System.Text.Json;

using DotNet.Testcontainers.Containers;

using Npgsql;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class PostgresContainerFixtureTests(PostgresContainerFixture postgresContainerFixture)
{
	// Testcontainers' own label, stated as the daemon carries it rather than read off an internal
	// constant: what the assertion below proves is what `docker images --filter label=...` would show.
	private const string ReaperSessionLabel = "org.testcontainers.resource-reaper-session";

	[Fact]
	public async Task TheServerAnswersAQueryOnTheAdminConnection()
	{
		postgresContainerFixture.RequireAvailable();

		await using var connection = new NpgsqlConnection(postgresContainerFixture.Server.AdminConnectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		await using var command = new NpgsqlCommand("SELECT 1;", connection);

		var scalar = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

		Assert.Equal(1, Assert.IsType<int>(scalar));
	}

	// The features the bench executes bottom out at 13 (`DROP DATABASE ... WITH (FORCE)`); fourteen is a
	// deliberate margin over that, not a requirement any shipped statement makes.
	[Fact]
	public async Task TheServerIsPostgresFourteenOrNewer()
	{
		postgresContainerFixture.RequireAvailable();

		await using var connection = new NpgsqlConnection(postgresContainerFixture.Server.AdminConnectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		Assert.True(connection.PostgreSqlVersion.Major >= 14);
	}

	// The reaper's session label is the only visible trace of WithCleanUp(true).
	[Fact]
	public async Task TheBuiltBenchImageIsLabelledForTheReaperAndForThisRepository()
	{
		postgresContainerFixture.RequireAvailable();

		var image = PostgresContainerFixture.BenchImageFor(TestEnvironment.Image);
		var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(
			await DockerCli.InspectImageLabelsAsync(image, TestContext.Current.CancellationToken));

		Assert.NotNull(labels);

		Assert.Equal(PostgresContainerFixture.BenchLabelValue, labels[PostgresContainerFixture.BenchLabel]);
		Assert.Equal(ResourceReaper.DefaultSessionId.ToString("D"), labels[ReaperSessionLabel]);
	}
}
