using Npgsql;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The write goes into a clone of the provisioned source, as scada_writer: the writer requires the
// archive table provisioning creates, and refuses a database that does not carry one. What the
// transaction owns is everything it creates itself — the day partitions and the rows — so that is
// what the rollback has to take with it.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class ArchiveWriterTransactionTests(PostgresContainerFixture postgresContainerFixture)
{
	private static readonly DateTime _start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

	private static readonly DateTime _end = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified);

	// The COPY carries one primary key twice, so the server rejects it well after the day partitions
	// were created — a failure part-way through, forced without a race.
	[Fact]
	public async Task ACopyThatFailsPartWayLeavesNoArchiveBehind()
	{
		postgresContainerFixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;

		await using var database = await postgresContainerFixture.CloneProvisionedAsync(cancellationToken);

		Assert.True(await ScalarIsTrueAsync(
			database.AdminConnectionString,
			ArchiveWriter.ArchiveExistsCommand,
			cancellationToken));

		Assert.False(await ScalarIsTrueAsync(
			database.AdminConnectionString,
			ArchiveWriter.ArchiveIsSeededCommand,
			cancellationToken));

		var written = await new ArchiveWriter(database.WriterConnectionString)
			.WriteAsync(DuplicatingRows(), _start, _end, cancellationToken);

		Assert.True(written.IsFailed);
		Assert.NotEmpty(written.Errors);

		Assert.False(
			await ScalarIsTrueAsync(
				database.AdminConnectionString,
				ArchiveWriter.ArchiveIsSeededCommand,
				cancellationToken),
			"a failed COPY rolled back the day partitions with it, so neither a row nor a partition may remain.");
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

	private static async Task<bool> ScalarIsTrueAsync(
		string connectionString,
		string statement,
		CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(connectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(statement, connection);

		return await command.ExecuteScalarAsync(cancellationToken) is true;
	}
}
