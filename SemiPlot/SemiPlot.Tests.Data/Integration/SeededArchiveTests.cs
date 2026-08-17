using Npgsql;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// What a seeded archive actually holds, read the way production reads it: every read connects as
// semiplot_reader rather than as the superuser, so a grant that never reached the reader fails here
// instead of on commissioning day. `semibase create` sets statement_timeout = 30 s and
// idle_in_transaction_session_timeout = 60 s on that role, so a slow query fails with 57014 rather
// than hanging — TheReaderCarriesTheProductionTimeouts pins it.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class SeededArchiveTests(PostgresContainerFixture postgresContainerFixture, SeededArchive seededArchive)
	: IClassFixture<SeededArchive>
{
	private const string TotalRowsCommand = "SELECT count(*) FROM public.trends;";

	private const string InsertCommand =
		"INSERT INTO public.trends (id, l, t, v, q) VALUES (@id, @l, @t, @v, @q);";

	// The rows the template was seeded with. Generating them again is what the counts in the database
	// are compared against, so the comparison covers the generator, the COPY and the partition routing
	// in one assertion.
	private static readonly Lazy<IReadOnlyList<(short Layer, long Rows)>> _generated = new(GenerateLayerCounts);

	[Fact]
	public async Task PerLayerRowCountsMatchTheGenerator()
	{
		postgresContainerFixture.RequireAvailable();

		var counts = new List<(short Layer, long Rows)>();

		await using var connection = await OpenReaderAsync();
		await using var command = new NpgsqlCommand(
			"SELECT l, count(*) FROM public.trends GROUP BY l ORDER BY l;",
			connection);

		await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

		while (await reader.ReadAsync(TestContext.Current.CancellationToken))
		{
			counts.Add((reader.GetInt16(0), reader.GetInt64(1)));
		}

		Assert.Equal(_generated.Value, counts);
	}

	// A missing daily partition sends rows to the catch-all instead of failing the write, so a non-empty
	// tpdefault is the documented fault signal (docs/architecture/scada-archive.md#reader-hazards).
	[Fact]
	public async Task TheDefaultPartitionIsEmpty()
	{
		postgresContainerFixture.RequireAvailable();

		Assert.Equal(0L, await ReaderScalarAsync<long>("SELECT count(*) FROM public.tpdefault;"));
	}

	// The insert runs as scada_writer, since semiplot_reader cannot insert at all and would fail on the
	// privilege instead of on the key. The transaction is rolled back in the finally block: the database
	// is shared by every test in this class and a leaked row would corrupt the counts they assert.
	[Fact]
	public async Task ThePrimaryKeyRejectsADuplicateRow()
	{
		postgresContainerFixture.RequireAvailable();

		var row = await FirstRawRowAsync();
		var before = await ReaderScalarAsync<long>(TotalRowsCommand);

		await using var connection = new NpgsqlConnection(seededArchive.Database.WriterConnectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

		try
		{
			await using var command = Insert(connection, transaction, row);

			var rejected = await Assert.ThrowsAsync<PostgresException>(
				() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

			Assert.Equal(PostgresErrorCodes.UniqueViolation, rejected.SqlState);
		}
		finally
		{
			await transaction.RollbackAsync(CancellationToken.None);
			await transaction.DisposeAsync();
		}

		Assert.Equal(before, await ReaderScalarAsync<long>(TotalRowsCommand));
	}

	// Asked of the session's own role rather than of a role name, so this fails if the reader connects
	// as anything other than semiplot_reader.
	[Fact]
	public async Task TheReaderHoldsSelectAndNotInsert()
	{
		postgresContainerFixture.RequireAvailable();

		Assert.Equal(
			SemibaseProvisioner.ReaderRole,
			await ReaderScalarAsync<string>("SELECT current_user;"));

		Assert.True(await ReaderScalarAsync<bool>("SELECT has_table_privilege('public.trends', 'SELECT');"));
		Assert.False(await ReaderScalarAsync<bool>("SELECT has_table_privilege('public.trends', 'INSERT');"));
	}

	// The catalogue check above is the grant as PostgreSQL records it; this is the grant as a write
	// attempt meets it. Both are needed: a privilege can be recorded and then shadowed by ownership or
	// by a row-level policy.
	[Fact]
	public async Task TheReaderIsRefusedAWrite()
	{
		postgresContainerFixture.RequireAvailable();

		var row = await FirstRawRowAsync() with { Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, 1) };

		await using var connection = await OpenReaderAsync();

		var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

		try
		{
			await using var command = Insert(connection, transaction, row);

			var refused = await Assert.ThrowsAsync<PostgresException>(
				() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

			Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
		}
		finally
		{
			await transaction.RollbackAsync(CancellationToken.None);
			await transaction.DisposeAsync();
		}
	}

	[Fact]
	public async Task TheTagCatalogueHoldsOneRowPerSeededPen()
	{
		postgresContainerFixture.RequireAvailable();

		var expected = RawLayerGenerator.SelectPens(ArchiveTemplate.Slice.PenCount)
			.Select(pen => ((int)pen.PenId, pen.Name))
			.OrderBy(pen => pen.Item1)
			.ToArray();

		Assert.Equal(expected, await TagsAsync(seededArchive.Database.ReaderConnectionString));
	}

	private static async Task<IReadOnlyList<(int Id, string Name)>> TagsAsync(string connectionString)
	{
		var tags = new List<(int, string)>();

		await using var connection = new NpgsqlConnection(connectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		await using var command = new NpgsqlCommand(
			"SELECT id, name FROM public.semiplot_tags ORDER BY id;",
			connection);
		await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

		while (await reader.ReadAsync(TestContext.Current.CancellationToken))
		{
			tags.Add((reader.GetInt32(0), reader.GetString(1)));
		}

		return tags;
	}

	// The upsert is what makes a template rebuild idempotent, and nothing else reaches its ON CONFLICT
	// branch: ArchiveTemplate returns early when public.trends exists, and the container path always
	// starts from a fresh database. A second write has to update the rows rather than double them or
	// fail on the key. It runs against a clone of its own, since it rewrites what it wrote.
	[Fact]
	public async Task WritingTheTagCatalogueTwiceUpdatesTheRowsInPlace()
	{
		postgresContainerFixture.RequireAvailable();

		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		var pens = RawLayerGenerator.SelectPens(ArchiveTemplate.Slice.PenCount);
		var renamed = pens.Select(pen => pen with { Name = pen.Name + " rewritten" }).ToArray();
		var writer = new TagCatalogWriter(database.AdminConnectionString);

		var first = await writer.WriteAsync(pens, TestContext.Current.CancellationToken);
		var second = await writer.WriteAsync(renamed, TestContext.Current.CancellationToken);

		Assert.True(first.IsSuccess, string.Join("; ", first.Errors.Select(error => error.Message)));
		Assert.True(second.IsSuccess, string.Join("; ", second.Errors.Select(error => error.Message)));
		Assert.Equal(pens.Count, second.Value);

		Assert.Equal(
			renamed.Select(pen => ((int)pen.PenId, pen.Name)).OrderBy(tag => tag.Item1).ToArray(),
			await TagsAsync(database.AdminConnectionString));
	}

	// The "never destroys" guarantee, asserted three times in the plan and verifiable only here: the
	// seeder run through its own entry point, against a database that already carries an archive.
	[Fact]
	public async Task TheSeederRefusesToWriteIntoASeededDatabase()
	{
		postgresContainerFixture.RequireAvailable();

		var before = await ReaderScalarAsync<long>(TotalRowsCommand);

		var exitCode = await Program.Main(
		[
			"--connection",
			seededArchive.Database.WriterConnectionString,
			"--end",
			"2026-01-02T00:00:00",
			"--days",
			"1",
			"--pens",
			"1"
		]);

		Assert.Equal(1, exitCode);
		Assert.Equal(before, await ReaderScalarAsync<long>(TotalRowsCommand));
	}

	// Production parity rather than a test setting: a slow query fails with 57014 here exactly as it
	// would on a site, and an abandoned transaction is closed rather than left holding its snapshot.
	[Fact]
	public async Task TheReaderCarriesTheProductionTimeouts()
	{
		postgresContainerFixture.RequireAvailable();

		Assert.Equal("30s", await ReaderScalarAsync<string>("SHOW statement_timeout;"));
		Assert.Equal("1min", await ReaderScalarAsync<string>("SHOW idle_in_transaction_session_timeout;"));
	}

	private static NpgsqlCommand Insert(NpgsqlConnection connection, NpgsqlTransaction transaction, ArchiveRow row)
	{
		var command = new NpgsqlCommand(InsertCommand, connection, transaction);

		command.Parameters.AddWithValue("id", row.Id);
		command.Parameters.AddWithValue("l", row.Layer);
		command.Parameters.AddWithValue("t", row.Timestamp);
		command.Parameters.AddWithValue("v", row.Value);
		command.Parameters.AddWithValue("q", row.Quality);

		return command;
	}

	private static IReadOnlyList<(short Layer, long Rows)> GenerateLayerCounts()
	{
		var rawRows = RawLayerGenerator.Generate(ArchiveTemplate.Slice);

		return rawRows.Concat(LayerThinner.ThinAll(rawRows))
			.GroupBy(row => row.Layer)
			.OrderBy(layer => layer.Key)
			.Select(layer => (layer.Key, layer.LongCount()))
			.ToArray();
	}

	private async Task<ArchiveRow> FirstRawRowAsync()
	{
		await using var connection = await OpenReaderAsync();
		await using var command = new NpgsqlCommand(
			$"SELECT id, l, t, v, q FROM public.trends WHERE l = {ArchiveRow.RawLayer} ORDER BY id, t LIMIT 1;",
			connection);

		await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

		Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken), "the archive holds no raw row.");

		return new ArchiveRow(
			reader.GetInt32(0),
			reader.GetInt16(1),
			reader.GetDateTime(2),
			reader.GetDouble(3),
			reader.GetInt32(4));
	}

	private async Task<T> ReaderScalarAsync<T>(string statement)
	{
		await using var connection = await OpenReaderAsync();
		await using var command = new NpgsqlCommand(statement, connection);

		return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
	}

	private async Task<NpgsqlConnection> OpenReaderAsync()
	{
		var connection = new NpgsqlConnection(seededArchive.Database.ReaderConnectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		return connection;
	}
}
