using Npgsql;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The gated tests of this slice: they assert that the fixture produced a server the later tasks can
// provision and seed, and nothing beyond that. Provisioning belongs to the task that runs
// 'semibase create'.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class PostgresContainerFixtureTests(PostgresContainerFixture postgresContainerFixture)
{
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

	// A floor rather than the exact major: SEMIPLOT_TEST_PG may name a site server the bench does not
	// choose the version of. The features the bench actually executes bottom out at 13
	// (`DROP DATABASE ... WITH (FORCE)`); COPY routing into a partitioned parent is 10. Fourteen is a
	// deliberate margin over that 13, not a requirement any shipped statement makes.
	[Fact]
	public async Task TheServerIsPostgresFourteenOrNewer()
	{
		postgresContainerFixture.RequireAvailable();

		await using var connection = new NpgsqlConnection(postgresContainerFixture.Server.AdminConnectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		Assert.True(connection.PostgreSqlVersion.Major >= 14);
	}

	[Fact]
	public async Task TheResolvedBinaryReportsThePinnedVersion()
	{
		postgresContainerFixture.RequireAvailable();

		var reported = await SemibaseProvisioner.RunAsync(
			postgresContainerFixture.Server.SemibaseExecutable,
			["version"],
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(reported.IsSuccess, string.Join("; ", reported.Errors.Select(error => error.Message)));
		Assert.Equal(SemibaseBinary.PinnedVersion, reported.Value.Trim());
	}
}
