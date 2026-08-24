using Npgsql;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The gated tests of the fixture itself: they assert that it produced a server the suite can seed and
// clone, and nothing beyond that. That the server arrives already provisioned is asserted by the
// container's own wait strategy, and by every test that clones the provisioned source.
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

	// The bench tracks a moving tag on purpose, so the one thing a run owes its reader is the identity of
	// the provisioner it ran: that is what separates "SemiBase moved" from "this repository broke" when an
	// unchanged commit fails tomorrow. What is asserted is that the run resolved an immutable manifest —
	// a build pinned to the moving tag instead would leave nothing to name.
	[Fact]
	public void TheContainerPathReportsTheProvisionerItResolved()
	{
		postgresContainerFixture.RequireAvailable();

		if (postgresContainerFixture.Provisioner is not { } provisioner)
		{
			Assert.Skip(
				$"{TestEnvironment.TestServerVariable} names the server, so the suite resolved no image: "
					+ $"the provisioner is the binary {TestEnvironment.SemibaseExecutableVariable} points at.");

			return;
		}

		TestContext.Current.TestOutputHelper?.WriteLine(provisioner.Describe());

		Assert.Contains("sha256:", provisioner.Digest, StringComparison.Ordinal);
	}
}
