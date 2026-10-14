using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Xunit;

namespace SemiPlot.Tests.Integration;

[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class ProviderSessionTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	private const string ShowJitStatement = "SHOW jit;";

	// Two connections held open together are two physical sessions, so the option reaches every pooled one.
	[Fact]
	public async Task EveryPooledSessionRunsWithJitOff()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		await using (var plain = new NpgsqlConnection(Database.PlotConnectionString))
		{
			await plain.OpenAsync(cancellationToken);

			(await ShowJitAsync(plain, cancellationToken)).Should().Be("on");
		}

		await using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var dataSource = services.GetRequiredService<NpgsqlDataSource>();

		await using var first = await dataSource.OpenConnectionAsync(cancellationToken);
		await using var second = await dataSource.OpenConnectionAsync(cancellationToken);

		(await ShowJitAsync(first, cancellationToken)).Should().Be("off");
		(await ShowJitAsync(second, cancellationToken)).Should().Be("off");
	}

	private static async Task<string?> ShowJitAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand(ShowJitStatement, connection);

		return (string?)await command.ExecuteScalarAsync(cancellationToken);
	}
}
