using AwesomeAssertions;

using Npgsql;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

// What a seeded archive actually holds, read the way production reads it: every read connects as
// semiplot rather than as the superuser, so a grant that never reached semiplot fails here
// instead of on commissioning day.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class SeededArchiveTests(SeededArchive seededArchive)
	: IClassFixture<SeededArchive>
{
	private const string TotalRowsCommand = "SELECT count(*) FROM public.trends;";

	private const string InsertCommand =
		"INSERT INTO public.trends (id, l, t, v, q) VALUES (@id, @l, @t, @v, @q);";

	// The rows the template was seeded with. Generating them again is what the counts in the database
	// are compared against, so the comparison covers the generator, the COPY and the partition routing.
	private static readonly Lazy<IReadOnlyList<(short Layer, long Rows)>> _generated = new(GenerateLayerCounts);

	[Fact]
	public async Task PerLayerRowCountsMatchTheGenerator()
	{
		var counts = await ArchiveDatabase.ReadAsync(
			seededArchive.Database.PlotConnectionString,
			"SELECT l, count(*) FROM public.trends GROUP BY l ORDER BY l;",
			reader => (reader.GetInt16(0), reader.GetInt64(1)),
			cancellationToken: TestContext.Current.CancellationToken);

		counts.Should().Equal(_generated.Value);
	}

	// A missing daily partition sends rows to the catch-all instead of failing the write. The seeder creates
	// every partition its rows need and the bench runs no SCADA, so tpdefault holds nothing here.
	[Fact]
	public async Task TheDefaultPartitionIsEmpty()
	{
		(await PlotScalarAsync<long>("SELECT count(*) FROM public.tpdefault;")).Should().Be(0L);
	}

	// The insert runs as scada_writer, since semiplot cannot insert into the archive at all and would fail on the
	// privilege instead of on the key. The transaction is rolled back in the finally block: the database
	// is shared by every test in this class and a leaked row would corrupt the counts they assert.
	[Fact]
	public async Task ThePrimaryKeyRejectsADuplicateRow()
	{
		var row = await FirstRawRowAsync();
		var before = await PlotScalarAsync<long>(TotalRowsCommand);

		await using var connection = new NpgsqlConnection(seededArchive.Database.WriterConnectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

		try
		{
			await using var command = Insert(connection, transaction, row);

			Func<Task> act = () => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
			var rejected = (await act.Should().ThrowAsync<PostgresException>()).Which;

			rejected.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
		}
		finally
		{
			await transaction.RollbackAsync(CancellationToken.None);
			await transaction.DisposeAsync();
		}

		(await PlotScalarAsync<long>(TotalRowsCommand)).Should().Be(before);
	}

	// Asked of the session's own role rather than of a role name, so this fails if the session connects
	// as anything other than semiplot.
	[Fact]
	public async Task ThePlotRoleHoldsSelectAndNotInsert()
	{
		(await PlotScalarAsync<string>("SELECT current_user;")).Should().Be(BenchRoles.PlotRole);

		(await PlotScalarAsync<bool>("SELECT has_table_privilege('public.trends', 'SELECT');")).Should().BeTrue();
		(await PlotScalarAsync<bool>("SELECT has_table_privilege('public.trends', 'INSERT');")).Should().BeFalse();
	}

	// The catalogue check above is the grant as PostgreSQL records it; this is the grant as a write
	// attempt meets it. Both are needed: a privilege can be recorded and then shadowed by ownership or
	// by a row-level policy.
	[Fact]
	public async Task ThePlotRoleIsRefusedAWrite()
	{
		var row = await FirstRawRowAsync() with { Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, 1) };

		await using var connection = await OpenPlotConnectionAsync();

		var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

		try
		{
			await using var command = Insert(connection, transaction, row);

			Func<Task> act = () => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
			var refused = (await act.Should().ThrowAsync<PostgresException>()).Which;

			refused.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
		}
		finally
		{
			await transaction.RollbackAsync(CancellationToken.None);
			await transaction.DisposeAsync();
		}
	}

	// The seeder through its own entry point, against a database that already carries an archive.
	[Fact]
	public async Task TheSeederRefusesToWriteIntoASeededDatabase()
	{
		var before = await PlotScalarAsync<long>(TotalRowsCommand);

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

		exitCode.Should().Be(1);
		(await PlotScalarAsync<long>(TotalRowsCommand)).Should().Be(before);
	}

	// Production parity rather than a test setting: a slow query fails with 57014 here exactly as it
	// would on a site, and an abandoned transaction is closed rather than left holding its snapshot.
	[Fact]
	public async Task ThePlotRoleCarriesTheProductionTimeouts()
	{
		(await PlotScalarAsync<string>("SHOW statement_timeout;")).Should().Be("30s");
		(await PlotScalarAsync<string>("SHOW idle_in_transaction_session_timeout;")).Should().Be("1min");
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

		return [.. rawRows.Concat(LayerThinner.ThinAll(rawRows))
			.GroupBy(row => row.Layer)
			.OrderBy(layer => layer.Key)
			.Select(layer => (layer.Key, layer.LongCount()))];
	}

	private async Task<ArchiveRow> FirstRawRowAsync()
	{
		var rows = await ArchiveDatabase.ReadAsync(
			seededArchive.Database.PlotConnectionString,
			$"SELECT id, l, t, v, q FROM public.trends WHERE l = {ArchiveRow.RawLayer} ORDER BY id, t LIMIT 1;",
			reader => new ArchiveRow(
				reader.GetInt32(0),
				reader.GetInt16(1),
				reader.GetDateTime(2),
				reader.GetDouble(3),
				reader.GetInt32(4)),
			cancellationToken: TestContext.Current.CancellationToken);

		rows.Should().ContainSingle("the archive holds no raw row.");

		return rows[0];
	}

	private Task<T> PlotScalarAsync<T>(string statement)
	{
		return ArchiveDatabase.ScalarAsync<T>(
			seededArchive.Database.PlotConnectionString,
			statement,
			cancellationToken: TestContext.Current.CancellationToken);
	}

	private async Task<NpgsqlConnection> OpenPlotConnectionAsync()
	{
		var connection = new NpgsqlConnection(seededArchive.Database.PlotConnectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		return connection;
	}
}
