using AwesomeAssertions;

using Npgsql;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

// The bench catalogue as the writer leaves it. The provisioned clone rather than the seeded template:
// this class writes its own rows (docs/architecture/bench.md).
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class TagCatalogWriterTests(PostgresContainerFixture postgresContainerFixture)
{
	private const string GroupNamesCommand = "SELECT name FROM public.semiplot_groups ORDER BY name;";

	private const string MembershipsCommand =
		"""
		SELECT membership.pen_id, grp.name
		FROM public.semiplot_pen_groups membership
		JOIN public.semiplot_groups grp ON grp.id = membership.group_id
		ORDER BY membership.pen_id, grp.name;
		""";

	private const string StoredScalesCommand =
		"SELECT id, scale_min, scale_max FROM public.semiplot_tags ORDER BY id;";

	[Fact]
	public async Task TheGroupTableHoldsEveryGroupTheSliceNames()
	{
		await using var database = await WrittenSliceAsync();

		var written = await GroupNamesAsync(database);

		written.Should().Equal(ExpectedGroupNames());
	}

	[Fact]
	public async Task EveryMembershipOfTheSliceGetsARowAndTheTwoGroupPenGetsTwo()
	{
		await using var database = await WrittenSliceAsync();

		var written = await MembershipsAsync(database);

		written.Should().Equal(ExpectedMemberships());
		written.Count(membership => membership.PenId == SyntheticPenCatalog.TwoGroupPenId).Should().Be(2);
		written.Should().NotContain(membership => membership.PenId == SyntheticPenCatalog.UncommissionedPenId);
	}

	[Fact]
	public async Task TheStoredScalePairIsTheSyntheticPensOwnBounds()
	{
		await using var database = await WrittenSliceAsync();

		var written = await StoredScalesAsync(database);

		written.Should().Equal(
		[
			.. Slice().Select(pen => (pen.PenId, pen.ScaleMin, pen.ScaleMax)).OrderBy(scale => scale.PenId)
		]);
		written.Should().Contain(scale => scale.PenId == SyntheticPenCatalog.UncommissionedPenId
										  && scale.Min == null
										  && scale.Max == null);
	}

	// A template rebuild runs the writer over a catalogue it already wrote; the memberships are
	// replaced, so a rerun over the same slice duplicates none of them.
	[Fact]
	public async Task RewritingTheCatalogueLeavesTheMembershipsUnchanged()
	{
		await using var database = await WrittenSliceAsync();

		var first = await MembershipsAsync(database);

		await new TagCatalogWriter(database.AdminConnectionString)
			.WriteAsync(Slice(), TestContext.Current.CancellationToken);

		(await MembershipsAsync(database)).Should().Equal(first);
	}

	// The other half of replacing rather than adding: a membership the catalogue stopped stating goes,
	// and the group it emptied goes with it rather than staying behind as a header with no rows.
	[Fact]
	public async Task RewritingATrimmedCatalogueDropsTheMembershipsAndGroupsItNoLongerStates()
	{
		await using var database = await WrittenSliceAsync();

		var trimmed = TrimmedSlice(out var droppedGroup);

		await new TagCatalogWriter(database.AdminConnectionString)
			.WriteAsync(trimmed, TestContext.Current.CancellationToken);

		var memberships = await MembershipsAsync(database);

		memberships.Count(membership => membership.PenId == SyntheticPenCatalog.TwoGroupPenId).Should().Be(1);
		memberships.Should().NotContain(membership => membership.Group == droppedGroup);
		(await GroupNamesAsync(database)).Should().NotContain(droppedGroup);
	}

	// The two-group pen loses its second group, which nothing else in the slice carries.
	private static IReadOnlyList<SyntheticPen> TrimmedSlice(out string droppedGroup)
	{
		var slice = Slice();
		var twoGroupPen = slice.Single(pen => pen.PenId == SyntheticPenCatalog.TwoGroupPenId);

		var removed = twoGroupPen.Groups[^1];
		droppedGroup = removed;

		return
		[
			.. slice.Select(pen => pen.PenId == SyntheticPenCatalog.TwoGroupPenId
				? pen with { Groups = [.. pen.Groups.Where(group => group != removed)] }
				: pen)
		];
	}

	private static IReadOnlyList<SyntheticPen> Slice()
	{
		return RawLayerGenerator.SelectPens(ArchiveTemplate.Slice.PenCount);
	}

	private static IReadOnlyList<string> ExpectedGroupNames()
	{
		return
		[
			.. Slice()
				.SelectMany(pen => pen.Groups)
				.Distinct(StringComparer.Ordinal)
				.OrderBy(name => name, StringComparer.Ordinal)
		];
	}

	private static IReadOnlyList<(int PenId, string Group)> ExpectedMemberships()
	{
		return
		[
			.. Slice()
				.SelectMany(pen => pen.Groups.Select(group => (pen.PenId, Group: group)))
				.OrderBy(membership => membership.PenId)
				.ThenBy(membership => membership.Group, StringComparer.Ordinal)
		];
	}

	private async Task<ArchiveDatabase> WrittenSliceAsync()
	{
		var database = await postgresContainerFixture.CloneProvisionedAsync(TestContext.Current.CancellationToken);

		await new TagCatalogWriter(database.AdminConnectionString)
			.WriteAsync(Slice(), TestContext.Current.CancellationToken);

		return database;
	}

	private static async Task<IReadOnlyList<string>> GroupNamesAsync(ArchiveDatabase database)
	{
		return await ReadAsync(database, GroupNamesCommand, reader => reader.GetString(0));
	}

	private static async Task<IReadOnlyList<(int PenId, string Group)>> MembershipsAsync(ArchiveDatabase database)
	{
		return await ReadAsync(
			database,
			MembershipsCommand,
			reader => (reader.GetInt32(0), reader.GetString(1)));
	}

	private static async Task<IReadOnlyList<(int PenId, double? Min, double? Max)>> StoredScalesAsync(
		ArchiveDatabase database)
	{
		return await ReadAsync(
			database,
			StoredScalesCommand,
			reader => (
				reader.GetInt32(0),
				reader.IsDBNull(1) ? (double?)null : reader.GetDouble(1),
				reader.IsDBNull(2) ? (double?)null : reader.GetDouble(2)));
	}

	private static async Task<IReadOnlyList<TRow>> ReadAsync<TRow>(
		ArchiveDatabase database,
		string commandText,
		Func<NpgsqlDataReader, TRow> readRow)
	{
		var rows = new List<TRow>();

		await using var connection = new NpgsqlConnection(database.AdminConnectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		await using var command = new NpgsqlCommand(commandText, connection);
		await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

		while (await reader.ReadAsync(TestContext.Current.CancellationToken))
		{
			rows.Add(readRow(reader));
		}

		return rows;
	}
}
