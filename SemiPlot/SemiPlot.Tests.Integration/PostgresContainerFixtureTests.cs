using AwesomeAssertions;

using Npgsql;

using Xunit;

namespace SemiPlot.Tests.Integration;

[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class PostgresContainerFixtureTests(PostgresContainerFixture postgresContainerFixture)
{
	[Fact]
	public async Task TheServerAnswersAQueryOnTheAdminConnection()
	{
		await using var connection = new NpgsqlConnection(postgresContainerFixture.Server.AdminConnectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		await using var command = new NpgsqlCommand("SELECT 1;", connection);

		var scalar = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

		scalar.Should().BeOfType<int>().Which.Should().Be(1);
	}
}
