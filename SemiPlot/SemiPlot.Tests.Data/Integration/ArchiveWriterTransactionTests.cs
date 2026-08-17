using Npgsql;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The write goes through the superuser rather than scada_writer: an empty database carries none of
// semibase's grants, and what is under test here is the transaction, not the privilege chain.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class ArchiveWriterTransactionTests(PostgresContainerFixture postgresContainerFixture)
{
	private static readonly DateTime _start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

	private static readonly DateTime _end = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified);

	// The COPY carries one primary key twice, so the server rejects it well after the schema and the
	// partitions were created — a failure part-way through, forced without a race.
	[Fact]
	public async Task ACopyThatFailsPartWayLeavesNoArchiveBehind()
	{
		postgresContainerFixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;

		await using var database = await postgresContainerFixture.CreateEmptyDatabaseAsync(cancellationToken);

		Assert.False(await ArchiveExistsAsync(database.AdminConnectionString, cancellationToken));

		var written = await new ArchiveWriter(database.AdminConnectionString)
			.WriteAsync(DuplicatingRows(), _start, _end, cancellationToken);

		Assert.True(written.IsFailed);
		Assert.NotEmpty(written.Errors);

		Assert.False(
			await ArchiveExistsAsync(database.AdminConnectionString, cancellationToken),
			"a failed COPY rolled back the schema with it, so public.trends must not exist.");
	}

	private static IReadOnlyList<ArchiveRow> DuplicatingRows()
	{
		var rows = new List<ArchiveRow>();

		for (var index = 0; index < 1000; index++)
		{
			rows.Add(new ArchiveRow(
				1,
				ArchiveRow.RawLayer,
				_start.AddMilliseconds(index * 100),
				index,
				ArchiveRow.OrdinaryQuality));
		}

		rows.Add(rows[0]);

		return rows;
	}

	private static async Task<bool> ArchiveExistsAsync(string connectionString, CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(connectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(ArchiveWriter.ArchiveExistsCommand, connection);

		return await command.ExecuteScalarAsync(cancellationToken) is true;
	}
}
