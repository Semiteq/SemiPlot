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
// semiplot, the role production uses.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class PostgresCatalogReadTests(
	PostgresContainerFixture postgresContainerFixture,
	SeededArchive seededArchive)
	: IClassFixture<SeededArchive>
{
	private const int ReversedMembershipTagId = 9996;

	// Memberships written in reverse name order, so the array the statement aggregates reads in name
	// order only because of its own ORDER BY.
	private const string ReversedMembershipCommand =
		"""
		INSERT INTO public.semiplot_tags (id, name, color, line_style)
		VALUES (9996, 'Reversed memberships', '#123456', 0);
		INSERT INTO public.semiplot_groups (name) VALUES ('Zulu'), ('Alpha');
		INSERT INTO public.semiplot_pen_groups (pen_id, group_id)
		SELECT 9996, id FROM public.semiplot_groups WHERE name = 'Zulu';
		INSERT INTO public.semiplot_pen_groups (pen_id, group_id)
		SELECT 9996, id FROM public.semiplot_groups WHERE name = 'Alpha';
		""";

	private const int ScalingMaskTagId = 9997;

	private const int ShapingMaskTagId = 9998;

	// The per-cent specifier scales the reading by 100, so it is the mistake a try over ToString
	// cannot catch; the pen beside it proves the rule accepts a mask that only shapes the number.
	private const string StoredMaskTagsCommand =
		"""
		INSERT INTO public.semiplot_tags (id, name, format, color, line_style)
		VALUES (9997, 'Scaling mask', '%0.0', '#123456', 0),
		       (9998, 'Shaping mask', '0.##0', '#123456', 0);
		""";

	private const int NullColumnTagId = 9999;

	// Every nullable column of SemiBase's semiplot_tags at once, and no membership: the state a pen
	// sits in between the day its number is known and the day it is commissioned.
	private const string UncommissionedTagCommand =
		"""
		INSERT INTO public.semiplot_tags (id, name, unit, format, color, line_style)
		VALUES (9999, 'Uncommissioned', NULL, NULL, NULL, 0);
		""";

	// Spelled out rather than read off the provider, so the test pins the delivered colour itself.
	private const string UncommissionedPenColor = "#808080";

	private const string StoredLineStylesCommand =
		"SELECT id, line_style FROM public.semiplot_tags ORDER BY id;";

	// The two values the column may hold, written out rather than cast from PenLineStyle.
	private const short InterpolatedOrdinal = 0;

	private const short SteppedOrdinal = 1;

	// BeEquivalentTo rather than Equal: a record compares IReadOnlyList<string> by reference, so whole
	// pens would compare unequal for a reason unrelated to the data.
	[Fact]
	public async Task SeededCatalogueReadsEveryPenOrderedByName()
	{
		var result = await ReadCatalogueAsync(seededArchive.Database.PlotConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Should().BeEquivalentTo(ExpectedPens(), options => options.WithStrictOrdering());
	}

	// The wholesale comparison above reads ExpectedPens(), which the seeder built from too: a catalogue
	// state dropped from SyntheticPenCatalog changes both sides and passes. The three cases below name
	// the states instead, so losing one fails here.
	[Fact]
	public async Task SeededCatalogueCarriesTheCommissionedFieldsOfEachPen()
	{
		var result = await ReadCatalogueAsync(seededArchive.Database.PlotConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		var commissioned = Single(result.Value, SyntheticPenCatalog.TwoGroupPenId);

		commissioned.Unit.Should().Be("degC");
		commissioned.Format.Should().Be("0.0");
		commissioned.ScaleMin.Should().Be(20.0);
		commissioned.ScaleMax.Should().Be(850.0);
		commissioned.EnabledOnStart.Should().BeTrue();

		Single(result.Value, SyntheticPenCatalog.HiddenOnStartPenId).EnabledOnStart.Should().BeFalse();
	}

	// One row, not two: the memberships aggregate into the array the record carries.
	[Fact]
	public async Task APenInTwoGroupsIsOneRowCarryingBothNames()
	{
		var result = await ReadCatalogueAsync(seededArchive.Database.PlotConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		Single(result.Value, SyntheticPenCatalog.TwoGroupPenId)
			.Groups.Should().Equal("Heaters", "Watchlist");
	}

	[Fact]
	public async Task AGroupListReadsInNameOrderWhateverOrderTheMembershipsWereWritten()
	{
		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			ReversedMembershipCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadCatalogueAsync(database.PlotConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		Single(result.Value, ReversedMembershipTagId).Groups.Should().Equal("Alpha", "Zulu");
	}

	// The outer join is what keeps this pen in the answer at all.
	[Fact]
	public async Task APenInNoGroupReadsAsAnEmptyGroupList()
	{
		var result = await ReadCatalogueAsync(seededArchive.Database.PlotConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		var uncommissioned = Single(result.Value, SyntheticPenCatalog.UncommissionedPenId);

		uncommissioned.Groups.Should().BeEmpty();
		uncommissioned.ScaleMin.Should().BeNull();
		uncommissioned.ScaleMax.Should().BeNull();
	}

	[Fact]
	public async Task AStoredMaskIsKeptWhenItShapesTheReadingAndDroppedWhenItScalesIt()
	{
		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			StoredMaskTagsCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadCatalogueAsync(database.PlotConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		Single(result.Value, ShapingMaskTagId).Format.Should().Be("0.##0");
		Single(result.Value, ScalingMaskTagId).Format.Should().BeNull();
	}

	[Fact]
	public async Task SeededCatalogueLineStylesReadBackAsTheStoredOrdinals()
	{
		var stored = await StoredLineStylesAsync(seededArchive.Database.PlotConnectionString);
		var result = await ReadCatalogueAsync(seededArchive.Database.PlotConnectionString);

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
	public async Task AnUncommissionedPenReadsWithTheFallbackColourAndNoStoredFields()
	{
		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			UncommissionedTagCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadCatalogueAsync(database.PlotConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		var expected = ExpectedPens();

		result.Value.Count.Should().Be(expected.Count + 1);

		var uncommissioned = Single(result.Value, NullColumnTagId);

		uncommissioned.Groups.Should().BeEmpty();
		uncommissioned.Color.Should().Be(UncommissionedPenColor);
		uncommissioned.Unit.Should().BeNull();
		uncommissioned.Format.Should().BeNull();
		result.Value.Should().BeEquivalentTo([.. expected, ExpectedUncommissionedPen()]);
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

		var result = await ReadCatalogueAsync(database.PlotConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Should().BeEmpty();
	}

	// The statement reads three tables, so naming semiplot_tags alone would send the operator to a
	// table that is still there. Asserted against the three names rather than the constant the
	// provider passed, which would compare the answer to itself.
	[Theory]
	[InlineData(ArchiveReadSupport.DropCatalogCommand)]
	[InlineData(ArchiveReadSupport.DropPenGroupsCommand)]
	[InlineData(ArchiveReadSupport.DropGroupsCommand)]
	public async Task ADroppedCatalogueTableFailsNamingEveryRelationTheReadTouches(string dropCommand)
	{
		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			dropCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadCatalogueAsync(database.PlotConnectionString);

		result.IsFailed.Should().BeTrue();

		var error = result.Errors.OfType<ArchiveError>().Should().ContainSingle().Which;

		error.Kind.Should().Be(ArchiveFault.TableMissing);
		error.Detail.Should().ContainAll("semiplot_tags", "semiplot_groups", "semiplot_pen_groups");
		error.Database.Should().Be(database.Name);
	}

	private static async Task<Result<IReadOnlyList<Pen>>> ReadCatalogueAsync(string connectionString)
	{
		await using var services = ArchiveProviderFactory.Build(connectionString);

		return await services.GetRequiredService<IDataProvider>().QueryPensAsync();
	}

	private static Pen Single(IEnumerable<Pen> pens, int penId)
	{
		return pens.Should().ContainSingle(pen => pen.PenId == penId).Which;
	}

	// Ordinal rather than the database collation: every seeded name differs inside its first word and
	// is ASCII, where the two orderings agree.
	private static IReadOnlyList<Pen> ExpectedPens()
	{
		return [.. RawLayerGenerator.SelectPens(ArchiveTemplate.Slice.PenCount)
			.Select(ToPen)
			.OrderBy(pen => pen.Name, StringComparer.Ordinal)];
	}

	private static Pen ToPen(SyntheticPen pen)
	{
		return new Pen(
			pen.PenId,
			pen.Name,
			pen.Groups,
			pen.Color,
			pen.Unit,
			pen.Format,
			pen.EnabledOnStart,
			pen.ScaleMin,
			pen.ScaleMax,
			pen.LineStyle);
	}

	// Spelled out rather than taken from the answer: the row inserted above carries every nullable
	// column empty, so the record the provider must build is fully decided by the statement.
	private static Pen ExpectedUncommissionedPen()
	{
		return new Pen(NullColumnTagId, "Uncommissioned", [], UncommissionedPenColor);
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
