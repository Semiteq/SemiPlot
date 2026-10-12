using AwesomeAssertions;

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
		"SELECT id, scale_min_on_start, scale_max_on_start FROM public.semiplot_tags ORDER BY id;";

	private const string LogScaleFlagsCommand =
		"SELECT id, log_scale_on_start FROM public.semiplot_tags ORDER BY id;";

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
			.. Slice()
				.Select(pen => (pen.PenId, pen.ScaleMinOnStart, pen.ScaleMaxOnStart))
				.OrderBy(scale => scale.PenId)
		]);
		written.Should().Contain(scale => scale.PenId == SyntheticPenCatalog.UncommissionedPenId
										  && scale.Min == null
										  && scale.Max == null);
	}

	[Fact]
	public async Task TheStoredLogScaleFlagIsTheSyntheticPensOwn()
	{
		await using var database = await WrittenSliceAsync();

		var written = await ArchiveDatabase.ReadAsync(
			database.AdminConnectionString,
			LogScaleFlagsCommand,
			reader => (PenId: reader.GetInt32(0), LogScaleOnStart: reader.GetBoolean(1)),
			cancellationToken: TestContext.Current.CancellationToken);

		written.Should().Equal(
		[
			.. Slice()
				.Select(pen => (pen.PenId, pen.LogScaleOnStart))
				.OrderBy(flag => flag.PenId)
		]);
		written.Should().ContainSingle(flag => flag.LogScaleOnStart)
			.Which.PenId.Should().Be(SyntheticPenCatalog.LogScalePenId);
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

	// The upsert is what makes a template rebuild idempotent.
	[Fact]
	public async Task WritingTheTagCatalogueTwiceUpdatesTheRowsInPlace()
	{
		await using var database = await WrittenSliceAsync();

		var renamed = Slice().Select(pen => pen with { Name = pen.Name + " rewritten" }).ToArray();

		var second = await new TagCatalogWriter(database.AdminConnectionString)
			.WriteAsync(renamed, TestContext.Current.CancellationToken);

		second.Should().Be(renamed.Length);

		(await ArchiveDatabase.ReadAsync(
			database.AdminConnectionString,
			"SELECT id, name FROM public.semiplot_tags ORDER BY id;",
			reader => (reader.GetInt32(0), reader.GetString(1)),
			cancellationToken: TestContext.Current.CancellationToken)).Should().Equal(
			[.. renamed.Select(pen => (pen.PenId, pen.Name)).OrderBy(tag => tag.PenId)]);
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

	private Task<ArchiveDatabase> WrittenSliceAsync()
	{
		return postgresContainerFixture.CloneAsync(
			CloneSource.Provisioned,
			clone => new TagCatalogWriter(clone.AdminConnectionString)
				.WriteAsync(Slice(), TestContext.Current.CancellationToken),
			TestContext.Current.CancellationToken);
	}

	private static async Task<IReadOnlyList<string>> GroupNamesAsync(ArchiveDatabase database)
	{
		return await ArchiveDatabase.ReadAsync(
			database.AdminConnectionString,
			GroupNamesCommand,
			reader => reader.GetString(0),
			cancellationToken: TestContext.Current.CancellationToken);
	}

	private static async Task<IReadOnlyList<(int PenId, string Group)>> MembershipsAsync(ArchiveDatabase database)
	{
		return await ArchiveDatabase.ReadAsync(
			database.AdminConnectionString,
			MembershipsCommand,
			reader => (reader.GetInt32(0), reader.GetString(1)),
			cancellationToken: TestContext.Current.CancellationToken);
	}

	private static async Task<IReadOnlyList<(int PenId, double? Min, double? Max)>> StoredScalesAsync(
		ArchiveDatabase database)
	{
		return await ArchiveDatabase.ReadAsync(
			database.AdminConnectionString,
			StoredScalesCommand,
			reader => (
				reader.GetInt32(0),
				reader.IsDBNull(1) ? (double?)null : reader.GetDouble(1),
				reader.IsDBNull(2) ? (double?)null : reader.GetDouble(2)),
			cancellationToken: TestContext.Current.CancellationToken);
	}
}
