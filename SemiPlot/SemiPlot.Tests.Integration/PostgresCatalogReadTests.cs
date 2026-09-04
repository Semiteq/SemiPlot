using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

// The catalogue read against the states a real archive is found in. Every read connects as
// semiplot_reader, the role production uses.
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
		var result = await ReadCatalogueAsync(seededArchive.Database.ReaderConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Should().Equal(ExpectedPens());
	}

	[Fact]
	public async Task SeededCatalogueLineStylesReadBackAsTheStoredOrdinals()
	{
		var stored = await StoredLineStylesAsync(seededArchive.Database.ReaderConnectionString);
		var result = await ReadCatalogueAsync(seededArchive.Database.ReaderConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		stored.Should().AllSatisfy(entry => (entry.LineStyle is InterpolatedOrdinal or SteppedOrdinal).Should().BeTrue(
			$"pen {entry.Id} stores line_style {entry.LineStyle}"));
		stored.Should().Contain(entry => entry.LineStyle == InterpolatedOrdinal);
		stored.Should().Contain(entry => entry.LineStyle == SteppedOrdinal);
		ReadLineStyles(result.Value).Should().Equal(stored);
		result.Value.Should().Contain(pen => pen.LineStyle == PenLineStyle.Interpolated);
		result.Value.Should().Contain(pen => pen.LineStyle == PenLineStyle.Stepped);
	}

	[Fact]
	public async Task ANullGroupNameAndColourReadAsEmptyStrings()
	{
		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			NullColumnTagCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadCatalogueAsync(database.ReaderConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		var expected = ExpectedPens();

		result.Value.Count.Should().Be(expected.Count + 1);

		var uncommissioned = result.Value.Should().ContainSingle(pen => pen.PenId == NullColumnTagId).Which;

		uncommissioned.Group.Should().Be(string.Empty);
		uncommissioned.Color.Should().Be(string.Empty);
		expected.Should().AllSatisfy(pen => result.Value.Should().Contain(pen));

		// Position, not only membership.
		result.Value[0].Should().BeSameAs(uncommissioned);
	}

	[Fact]
	public async Task AnEmptiedCatalogueIsASuccessfulEmptyList()
	{
		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			ArchiveReadSupport.EmptyCatalogCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadCatalogueAsync(database.ReaderConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Should().BeEmpty();
	}

	[Fact]
	public async Task ADroppedCatalogueFailsNamingSemiplotTags()
	{
		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			ArchiveReadSupport.DropCatalogCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadCatalogueAsync(database.ReaderConnectionString);

		result.IsFailed.Should().BeTrue();

		var error = result.Errors.OfType<ArchiveError>().Should().ContainSingle().Which;

		error.Kind.Should().Be(ArchiveFault.TableMissing);
		error.Detail.Should().Be("semiplot_tags");
		error.Database.Should().Be(database.Name);
	}

	private static async Task<Result<IReadOnlyList<Pen>>> ReadCatalogueAsync(string connectionString)
	{
		await using var services = ArchiveProviderFactory.Build(connectionString);

		return await services.GetRequiredService<IDataProvider>().QueryPensAsync();
	}

	private static IReadOnlyList<Pen> ExpectedPens()
	{
		return [.. RawLayerGenerator.SelectPens(ArchiveTemplate.Slice.PenCount)
			.Select(pen => pen.ToPen())
			.OrderBy(pen => pen.Group, StringComparer.Ordinal)
			.ThenBy(pen => pen.Name, StringComparer.Ordinal)];
	}

	private static IReadOnlyList<(int Id, short LineStyle)> ReadLineStyles(IEnumerable<Pen> pens)
	{
		return [.. pens.Select(pen => (Id: pen.PenId, LineStyle: (short)pen.LineStyle)).OrderBy(entry => entry.Id)];
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
