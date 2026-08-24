using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The catalogue read against the states a real archive is found in. Every read connects as
// semiplot_reader, the role production uses and the one SeededArchiveTests already proves holds SELECT
// on semiplot_tags, so a 42501 here is a connection-string or role fault in this slice rather than a
// mapper bug or a missing grant.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class PostgresCatalogReadTests(
	PostgresContainerFixture postgresContainerFixture,
	SeededArchive seededArchive)
	: IClassFixture<SeededArchive>
{
	// Both nullable columns of SemiBase's semiplot_tags, written the one way the bench seeder
	// never writes them.
	private const string NullColumnTagCommand =
		"""
		INSERT INTO public.semiplot_tags (id, name, group_name, color, line_style)
		VALUES (9999, 'Uncommissioned', NULL, NULL, 0);
		""";

	private const string StoredLineStylesCommand =
		"SELECT id, line_style FROM public.semiplot_tags ORDER BY id;";

	private const int NullColumnTagId = 9999;

	// The two values the column may hold, written out rather than cast from PenLineStyle.
	private const short InterpolatedOrdinal = 0;

	private const short SteppedOrdinal = 1;

	[Fact]
	public async Task SeededCatalogueReadsEveryPenOrderedByGroupThenName()
	{
		postgresContainerFixture.RequireAvailable();

		var result = await ReadCatalogueAsync(seededArchive.Database.ReaderConnectionString);

		Assert.True(result.IsSuccess, ArchiveReadSupport.Describe(result));
		Assert.Equal(ExpectedPens(), result.Value);
	}

	// The stored values are asserted against literal 0 and 1, never against PenLineStyle.
	[Fact]
	public async Task SeededCatalogueLineStylesReadBackAsTheStoredOrdinals()
	{
		postgresContainerFixture.RequireAvailable();

		var stored = await StoredLineStylesAsync(seededArchive.Database.ReaderConnectionString);
		var result = await ReadCatalogueAsync(seededArchive.Database.ReaderConnectionString);

		Assert.True(result.IsSuccess, ArchiveReadSupport.Describe(result));
		Assert.All(stored, entry => Assert.True(
			entry.LineStyle is InterpolatedOrdinal or SteppedOrdinal,
			$"pen {entry.Id} stores line_style {entry.LineStyle}"));
		Assert.Contains(stored, entry => entry.LineStyle == InterpolatedOrdinal);
		Assert.Contains(stored, entry => entry.LineStyle == SteppedOrdinal);
		Assert.Equal(stored, ReadLineStyles(result.Value));
		Assert.Contains(result.Value, pen => pen.LineStyle == PenLineStyle.Interpolated);
		Assert.Contains(result.Value, pen => pen.LineStyle == PenLineStyle.Stepped);
	}

	[Fact]
	public async Task ANullGroupNameAndColourReadAsEmptyStrings()
	{
		postgresContainerFixture.RequireAvailable();

		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			NullColumnTagCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadCatalogueAsync(database.ReaderConnectionString);

		Assert.True(result.IsSuccess, ArchiveReadSupport.Describe(result));

		var expected = ExpectedPens();

		Assert.Equal(expected.Count + 1, result.Value.Count);

		var uncommissioned = Assert.Single(result.Value, pen => pen.PenId == NullColumnTagId);

		Assert.Equal(string.Empty, uncommissioned.Group);
		Assert.Equal(string.Empty, uncommissioned.Color);
		Assert.All(expected, pen => Assert.Contains(pen, result.Value));

		// Position, not only membership.
		Assert.Same(uncommissioned, result.Value[0]);
	}

	[Fact]
	public async Task AnEmptiedCatalogueIsASuccessfulEmptyList()
	{
		postgresContainerFixture.RequireAvailable();

		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			ArchiveReadSupport.EmptyCatalogCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadCatalogueAsync(database.ReaderConnectionString);

		Assert.True(result.IsSuccess, ArchiveReadSupport.Describe(result));
		Assert.Empty(result.Value);
	}

	[Fact]
	public async Task ADroppedCatalogueFailsNamingSemiplotTags()
	{
		postgresContainerFixture.RequireAvailable();

		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			ArchiveReadSupport.DropCatalogCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadCatalogueAsync(database.ReaderConnectionString);

		Assert.True(result.IsFailed);

		var error = Assert.Single(result.Errors.OfType<ArchiveNotInitialisedError>());

		Assert.Equal(ArchiveObject.Table, error.MissingObject);
		Assert.Equal("semiplot_tags", error.Table);
		Assert.Equal(database.Name, error.Database);
	}

	private static async Task<Result<IReadOnlyList<Pen>>> ReadCatalogueAsync(string connectionString)
	{
		await using var services = ArchiveProviderFactory.Build(connectionString);

		return await services.GetRequiredService<IDataProvider>().QueryPensAsync();
	}

	private static IReadOnlyList<Pen> ExpectedPens()
	{
		return RawLayerGenerator.SelectPens(ArchiveTemplate.Slice.PenCount)
			.Select(pen => pen.ToPen())
			.OrderBy(pen => pen.Group, StringComparer.Ordinal)
			.ThenBy(pen => pen.Name, StringComparer.Ordinal)
			.ToArray();
	}

	private static IReadOnlyList<(int Id, short LineStyle)> ReadLineStyles(IEnumerable<Pen> pens)
	{
		return pens.Select(pen => (Id: (int)pen.PenId, LineStyle: (short)pen.LineStyle))
			.OrderBy(entry => entry.Id)
			.ToArray();
	}

	// Read straight off the table rather than through the provider, so the comparison covers the stored
	// smallint itself rather than the reader's own answer twice.
	private static async Task<IReadOnlyList<(int Id, short LineStyle)>> StoredLineStylesAsync(
		string connectionString)
	{
		var lineStyles = new List<(int, short)>();

		await using var connection = new NpgsqlConnection(connectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		await using var command = new NpgsqlCommand(StoredLineStylesCommand, connection);
		await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

		while (await reader.ReadAsync(TestContext.Current.CancellationToken))
		{
			lineStyles.Add((reader.GetInt32(0), reader.GetInt16(1)));
		}

		return lineStyles;
	}
}
