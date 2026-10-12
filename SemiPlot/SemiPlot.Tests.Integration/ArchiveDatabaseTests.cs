using AwesomeAssertions;

using Xunit;

namespace SemiPlot.Tests.Integration;

[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class ArchiveDatabaseTests(PostgresContainerFixture postgresContainerFixture)
{
	private const string CountDatabasesCommand = "SELECT count(*) FROM pg_database WHERE datname = @name;";

	[Fact]
	public async Task TheDatabaseIsGoneAfterDisposal()
	{
		var database = await postgresContainerFixture.CloneAsync(
			CloneSource.Provisioned, cancellationToken: TestContext.Current.CancellationToken);

		(await CountDatabasesAsync(database.Name)).Should().Be(1L);

		await database.DisposeAsync();

		(await CountDatabasesAsync(database.Name)).Should().Be(0L);
	}

	[Fact]
	public async Task ACloneWhoseSeedThrowsIsDropped()
	{
		var cloneName = string.Empty;

		var clone = () => postgresContainerFixture.CloneAsync(
			CloneSource.Provisioned,
			database =>
			{
				cloneName = database.Name;

				throw new InvalidOperationException("the seed failed");
			},
			TestContext.Current.CancellationToken);

		await clone.Should().ThrowAsync<InvalidOperationException>();

		cloneName.Should().NotBeEmpty();
		(await CountDatabasesAsync(cloneName)).Should().Be(0L);
	}

	private Task<long> CountDatabasesAsync(string name)
	{
		return ArchiveDatabase.ScalarAsync<long>(
			postgresContainerFixture.Server.AdminConnectionString,
			CountDatabasesCommand,
			parameters => parameters.AddWithValue("name", name),
			TestContext.Current.CancellationToken);
	}
}
