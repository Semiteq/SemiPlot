using Docker.DotNet.Models;

using DotNet.Testcontainers.Configurations;
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

	// The reaper's own session label on the built image is the only visible trace of WithCleanUp(true): a
	// revert to WithCleanUp(false) drops the label and passes every other test in the suite. The
	// repository label is asserted with it, because the dangling-image count is filtered on it.
	[Fact]
	public async Task TheBuiltBenchImageIsLabelledForTheReaperAndForThisRepository()
	{
		postgresContainerFixture.RequireAvailable();

		// Non-null on every path that reaches here: RequireAvailable above already skipped or failed a machine
		// whose daemon did not answer, and the fixture resolved this same endpoint to pull the provisioner.
		Assert.NotNull(TestcontainersSettings.OS.DockerEndpointAuthConfig);

		using var client = TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientBuilder().Build();

		var listed = await client.Images.ListImagesAsync(
			new ImagesListParameters
			{
				Filters = new Dictionary<string, IDictionary<string, bool>>
				{
					["reference"] = Filter(PostgresContainerFixture.BenchImageFor(TestEnvironment.Image)),
					["label"] = Filter(
						$"{PostgresContainerFixture.BenchLabel}={PostgresContainerFixture.BenchLabelValue}",
						$"{ReaperSessionLabel}={ResourceReaper.DefaultSessionId:D}")
				}
			},
			TestContext.Current.CancellationToken);

		Assert.Single(listed);
	}

	// An OperationCanceledException escaping here would pass straight through the fixture's own filter and
	// fail the whole collection instead of skipping it, and returning at all proves it did not.
	[Fact]
	public async Task AnUnmeetableProvisionerBoundIsAStatedReasonRatherThanACancellation()
	{
		postgresContainerFixture.RequireAvailable();

		var resolved = await ProvisionerImage.ResolveAsync(
			TimeSpan.FromMilliseconds(1),
			TestContext.Current.CancellationToken);

		Assert.True(resolved.IsFailed);
		Assert.NotEmpty(resolved.Errors[0].Message);
	}

	// The bench tracks a moving tag on purpose, so the one thing a run owes its reader is the identity of
	// the provisioner it ran: that is what separates "SemiBase moved" from "this repository broke" when an
	// unchanged commit fails tomorrow. What is asserted is that the run resolved an immutable manifest —
	// a build pinned to the moving tag instead would leave nothing to name.
	[Fact]
	public void TheContainerPathReportsTheProvisionerItResolved()
	{
		postgresContainerFixture.RequireAvailable();

		var provisioner = postgresContainerFixture.Provisioner;

		TestContext.Current.TestOutputHelper?.WriteLine(provisioner.Describe());

		Assert.Contains("sha256:", provisioner.Digest, StringComparison.Ordinal);
	}

	private static Dictionary<string, bool> Filter(params string[] values)
	{
		return values.ToDictionary(value => value, _ => true, StringComparer.Ordinal);
	}
}
