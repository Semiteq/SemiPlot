using Npgsql;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The write goes into a clone of the provisioned source, as scada_writer: the writer requires the
// archive table provisioning creates, and refuses a database that does not carry one. What the
// transaction owns is everything it creates itself — the day partitions and the rows — so that is
// what the rollback has to take with it.
//
// Every test here writes, so none of them may take SeededArchive, whose contract is that the class
// leaves the database as it found it. xunit constructs a test class once per test method, so the clone
// built in InitializeAsync belongs to exactly one test and is dropped with it.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class ArchiveWriterTransactionTests(PostgresContainerFixture postgresContainerFixture)
	: IAsyncLifetime
{
	private static readonly DateTime _start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

	private static readonly DateTime _end = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified);

	// A day the seeding run does not cover, so a partition created for it can only be the appending
	// run's own.
	private static readonly DateTime _laterDay = new(2026, 1, 3, 0, 0, 0, DateTimeKind.Unspecified);

	private const int SeededPenId = 1;

	private const int AppendedPenId = 2;

	// Provisioning creates tpdefault with the table, so it is present before any run and is not evidence
	// of one.
	private const string DefaultPartition = "tpdefault";

	private const string CountRowsCommand = "SELECT count(*) FROM public.trends;";

	private const string PartitionNamesCommand = """
		SELECT partition.relname
		FROM pg_inherits
		JOIN pg_class AS partition ON partition.oid = pg_inherits.inhrelid
		WHERE pg_inherits.inhparent = to_regclass('public.trends')
		ORDER BY partition.relname;
		""";

	private ArchiveDatabase? _archiveDatabase;

	private ArchiveDatabase Database =>
		_archiveDatabase ?? throw new InvalidOperationException(
			postgresContainerFixture.UnavailableReason ?? "The archive was used before it was cloned.");

	public async ValueTask InitializeAsync()
	{
		if (!postgresContainerFixture.IsAvailable)
		{
			return;
		}

		_archiveDatabase = await postgresContainerFixture.CloneProvisionedAsync();
	}

	public async ValueTask DisposeAsync()
	{
		if (_archiveDatabase is not null)
		{
			await _archiveDatabase.DisposeAsync();
		}
	}

	// The COPY carries one primary key twice, so the server rejects it well after the day partitions
	// were created — a failure part-way through, forced without a race.
	[Fact]
	public async Task ACopyThatFailsPartWayLeavesNoArchiveBehind()
	{
		postgresContainerFixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;

		Assert.True(await ScalarIsTrueAsync(ArchiveWriter.ArchiveExistsCommand, cancellationToken));

		Assert.False(await ScalarIsTrueAsync(ArchiveWriter.ArchiveIsSeededCommand, cancellationToken));

		var written = await Writer()
			.WriteAsync(DuplicatingRows(_start, SeededPenId), _start, _end, cancellationToken: cancellationToken);

		Assert.True(written.IsFailed);
		Assert.NotEmpty(written.Errors);

		Assert.False(
			await ScalarIsTrueAsync(ArchiveWriter.ArchiveIsSeededCommand, cancellationToken),
			"a failed COPY rolled back the day partitions with it, so neither a row nor a partition may remain.");
	}

	// The two paths differ in exactly one check, and the appending run's day falls on the partition the
	// seeding run already created — which is what the IF NOT EXISTS clause has to pass through.
	[Fact]
	public async Task TheAppendingRunWritesWhereTheSeedingRunIsRefused()
	{
		postgresContainerFixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;

		var seeded = await SeedAsync(cancellationToken);

		var refused = await Writer()
			.WriteAsync(OrdinaryRows(_start, AppendedPenId), _start, _end, cancellationToken: cancellationToken);

		Assert.True(refused.IsFailed);
		Assert.Contains(
			refused.Errors,
			error => error.Message.Contains("already carries rows", StringComparison.Ordinal));

		var appended = await Writer().WriteAsync(
			OrdinaryRows(_start, AppendedPenId),
			_start,
			_end,
			allowExistingRows: true,
			cancellationToken);

		Assert.True(appended.IsSuccess);
		Assert.Equal(seeded, appended.Value);
		Assert.Equal(seeded + appended.Value, await CountRowsAsync(cancellationToken));
		Assert.Equal(
			[PartitionScript.PartitionName(_start), DefaultPartition],
			await PartitionNamesAsync(cancellationToken));
	}

	[Fact]
	public async Task TheAppendingRunCreatesOnlyTheDaysItsRowsNeed()
	{
		postgresContainerFixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;

		await SeedAsync(cancellationToken);

		var appended = await Writer().WriteAsync(
			OrdinaryRows(_laterDay, SeededPenId),
			_laterDay,
			_laterDay.AddDays(1),
			allowExistingRows: true,
			cancellationToken);

		Assert.True(appended.IsSuccess);
		Assert.Equal(
			[PartitionScript.PartitionName(_start), PartitionScript.PartitionName(_laterDay), DefaultPartition],
			await PartitionNamesAsync(cancellationToken));
	}

	// The appending run owns its own partitions and its own rows exactly as the seeding run does, so a
	// COPY that fails part-way takes both back and leaves the archive the seeding run wrote.
	[Fact]
	public async Task AnAppendingCopyThatFailsPartWayLeavesTheArchiveAsItWas()
	{
		postgresContainerFixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;

		var seeded = await SeedAsync(cancellationToken);

		var appended = await Writer().WriteAsync(
			DuplicatingRows(_laterDay, SeededPenId),
			_laterDay,
			_laterDay.AddDays(1),
			allowExistingRows: true,
			cancellationToken);

		Assert.True(appended.IsFailed);
		Assert.NotEmpty(appended.Errors);
		Assert.Equal(seeded, await CountRowsAsync(cancellationToken));
		Assert.Equal(
			[PartitionScript.PartitionName(_start), DefaultPartition],
			await PartitionNamesAsync(cancellationToken));
	}

	private ArchiveWriter Writer()
	{
		return new ArchiveWriter(Database.WriterConnectionString);
	}

	private async Task<long> SeedAsync(CancellationToken cancellationToken)
	{
		var written = await Writer()
			.WriteAsync(OrdinaryRows(_start, SeededPenId), _start, _end, cancellationToken: cancellationToken);

		Assert.True(written.IsSuccess);

		return written.Value;
	}

	private static IReadOnlyList<ArchiveRow> OrdinaryRows(DateTime day, int penId)
	{
		return Enumerable
			.Range(0, 1000)
			.Select(index => new ArchiveRow(
				penId,
				ArchiveRow.RawLayer,
				day.AddMilliseconds(index * 100),
				index,
				ArchiveRow.OrdinaryQuality))
			.ToArray();
	}

	private static IReadOnlyList<ArchiveRow> DuplicatingRows(DateTime day, int penId)
	{
		var rows = OrdinaryRows(day, penId).ToList();

		rows.Add(rows[0]);

		return rows;
	}

	private async Task<bool> ScalarIsTrueAsync(string statement, CancellationToken cancellationToken)
	{
		return await ScalarAsync(statement, cancellationToken) is true;
	}

	private async Task<long> CountRowsAsync(CancellationToken cancellationToken)
	{
		return (long)(await ScalarAsync(CountRowsCommand, cancellationToken))!;
	}

	private async Task<object?> ScalarAsync(string statement, CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(Database.AdminConnectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(statement, connection);

		return await command.ExecuteScalarAsync(cancellationToken);
	}

	private async Task<IReadOnlyList<string>> PartitionNamesAsync(CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(Database.AdminConnectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(PartitionNamesCommand, connection);
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);

		var names = new List<string>();

		while (await reader.ReadAsync(cancellationToken))
		{
			names.Add(reader.GetString(0));
		}

		return names;
	}
}
