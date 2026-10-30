using System.Globalization;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

// The live catalogue compares two reads of the real statement; every read and every editor write runs as semiplot.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class LiveCatalogueTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	private const int ChamberPressureId = 101;

	private const int HeaterId = 102;

	private const int UncommissionedId = 105;

	private const int HandEditedId = 106;

	private const string VacuumGroup = "Vacuum";

	private const string FallbackColorArgument = "FallbackColor";

	private const string StoredMaskArgument = "StoredMask";

	private const string StoredLineStyleArgument = "StoredValue";

	private static readonly string _uncommissionedTagCommand =
		$"""
		INSERT INTO public.semiplot_tags (id, name, color, line_style)
		VALUES ({UncommissionedId}, 'Uncommissioned', NULL, 0);
		""";

	// A mask the character rule refuses and a line style no build defines, as a hand edit would leave them.
	private static readonly string _handEditedTagCommand =
		$"""
		INSERT INTO public.semiplot_tags (id, name, format, color, line_style)
		VALUES ({HandEditedId}, 'Hand edited', '%0.0', '#123456', 99);
		""";

	private static readonly string _handEditedAgainCommand =
		$"UPDATE public.semiplot_tags SET format = '%0.00', line_style = 98 WHERE id = {HandEditedId};";

	[Fact]
	public async Task TwoReadsWithNoWriteBetween_Match()
	{
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var provider = services.GetRequiredService<IDataProvider>();

		var before = await ReadCatalogueAsync(provider);
		var after = await ReadCatalogueAsync(provider);

		before.Matches(after).Should().BeTrue();
		after.Pens.Select(pen => pen.PenId).Should().BeEquivalentTo([ChamberPressureId, HeaterId, UncommissionedId]);
	}

	[Fact]
	public async Task AColourAndAMembershipWrittenThroughTheEditor_ChangeOnePenAndOneGroup()
	{
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var provider = services.GetRequiredService<IDataProvider>();
		var editor = services.GetRequiredService<IPenCatalogueEditor>();
		var before = await ReadCatalogueAsync(provider);

		var colourWritten = await editor.ChangeAsync(
			await StoredPenAsync(editor, HeaterId),
			new PenSettingChange.Color("#ABCDEF"));

		colourWritten.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(colourWritten));

		var catalogue = await editor.ReadAsync();

		catalogue.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(catalogue));

		var membershipWritten = await editor.SetMembershipAsync(
			catalogue.Value.Pens.Should().ContainSingle(pen => pen.Id == HeaterId).Which,
			catalogue.Value.Groups.Should().ContainSingle(group => group.Name == VacuumGroup).Which,
			true);

		membershipWritten.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(membershipWritten));

		var after = await ReadCatalogueAsync(provider);

		before.Matches(after).Should().BeFalse();
		var pens = PenListDelta.Between(before.Pens, after.Pens);
		var revised = pens.Revised.Should().ContainSingle().Which;

		revised.PenId.Should().Be(HeaterId);
		revised.Color.Should().Be("#ABCDEF");
		before.Pens.Should().ContainSingle(pen => pen.PenId == HeaterId).Which.Groups.Should().NotContain(VacuumGroup);
		revised.Groups.Should().Contain(VacuumGroup);
		pens.Added.Should().BeEmpty();
		pens.RemovedPenIds.Should().BeEmpty();
		ChangedGroups(before, after).Should().ContainSingle().Which.Name.Should().Be(VacuumGroup);
		GroupIds(after).Should().Equal(GroupIds(before));
	}

	[Fact]
	public async Task AColourWrittenThroughTheEditor_ChangesOnePenAndNoGroup()
	{
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var provider = services.GetRequiredService<IDataProvider>();
		var editor = services.GetRequiredService<IPenCatalogueEditor>();
		var before = await ReadCatalogueAsync(provider);

		var colourWritten = await editor.ChangeAsync(
			await StoredPenAsync(editor, HeaterId),
			new PenSettingChange.Color("#ABCDEF"));

		colourWritten.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(colourWritten));

		var after = await ReadCatalogueAsync(provider);

		before.Matches(after).Should().BeFalse();
		var pens = PenListDelta.Between(before.Pens, after.Pens);
		var revised = pens.Revised.Should().ContainSingle().Which;

		revised.PenId.Should().Be(HeaterId);
		revised.Color.Should().Be("#ABCDEF");
		revised.Groups.Should().Equal(before.Pens.Single(pen => pen.PenId == HeaterId).Groups);
		pens.Added.Should().BeEmpty();
		pens.RemovedPenIds.Should().BeEmpty();
		ChangedGroups(before, after).Should().BeEmpty();
		GroupIds(after).Should().Equal(GroupIds(before));
	}

	[Fact]
	public async Task TwoReadsOfAPenStoredWithNoColour_LogOneWarningNotTwo()
	{
		using var logs = new FakeLoggerProvider();
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString, logs);

		var provider = services.GetRequiredService<IDataProvider>();

		await ReadCatalogueAsync(provider);
		await ReadCatalogueAsync(provider);

		LevelsOf(logs, UncommissionedId, FallbackColorArgument).Should().Equal(LogLevel.Warning, LogLevel.Debug);
	}

	[Fact]
	public async Task AnUnusableMaskAndLineStyle_WarnOncePerStoredValue()
	{
		using var logs = new FakeLoggerProvider();
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString, logs);

		var provider = services.GetRequiredService<IDataProvider>();

		await ArchiveDatabase.ExecuteAsync(
			Database.AdminConnectionString,
			_handEditedTagCommand,
			TestContext.Current.CancellationToken);
		await ReadCatalogueAsync(provider);
		await ReadCatalogueAsync(provider);
		await ArchiveDatabase.ExecuteAsync(
			Database.AdminConnectionString,
			_handEditedAgainCommand,
			TestContext.Current.CancellationToken);
		await ReadCatalogueAsync(provider);

		LevelsOf(logs, HandEditedId, StoredMaskArgument).Should().Equal(
			LogLevel.Warning, LogLevel.Debug, LogLevel.Warning);
		LevelsOf(logs, HandEditedId, StoredLineStyleArgument).Should().Equal(
			LogLevel.Warning, LogLevel.Debug, LogLevel.Warning);
	}

	protected override async ValueTask SeedAsync()
	{
		await new TagCatalogWriter(Database.AdminConnectionString).WriteAsync(
			[
				new SyntheticPen(ChamberPressureId, "Chamber pressure", [VacuumGroup], "#112233", 0, 100, "Pa", "0.0"),
				new SyntheticPen(HeaterId, "Heater", ["Heaters"], "#223344", 20, 850, "degC")
			],
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			Database.AdminConnectionString,
			_uncommissionedTagCommand,
			TestContext.Current.CancellationToken);
	}

	private static async Task<CatalogueRead> ReadCatalogueAsync(IDataProvider provider)
	{
		var read = await provider.QueryCatalogueAsync();

		read.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(read));

		return read.Value;
	}

	/// <summary>
	/// The groups of <paramref name="after"/> whose name or members differ from the group of that id before.
	/// </summary>
	private static IReadOnlyList<StoredGroup> ChangedGroups(CatalogueRead before, CatalogueRead after)
	{
		var beforeById = before.Stored.Groups.ToDictionary(group => group.Id);

		return
		[
			.. after.Stored.Groups.Where(group =>
				beforeById.TryGetValue(group.Id, out var previous)
				&& (previous.Name != group.Name || !previous.MemberPenIds.SequenceEqual(group.MemberPenIds)))
		];
	}

	private static IEnumerable<int> GroupIds(CatalogueRead read)
	{
		return read.Stored.Groups.Select(group => group.Id).Order();
	}

	private static async Task<StoredPen> StoredPenAsync(IPenCatalogueEditor editor, int penId)
	{
		var catalogue = await editor.ReadAsync();

		catalogue.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(catalogue));

		return catalogue.Value.Pens.Should().ContainSingle(pen => pen.Id == penId).Which;
	}

	/// <summary>The levels of the entries about one pen that carry the named argument.</summary>
	private static IReadOnlyList<LogLevel> LevelsOf(FakeLoggerProvider logs, int penId, string argument)
	{
		var penIdText = penId.ToString(CultureInfo.InvariantCulture);

		return
		[
			.. logs.Collector.GetSnapshot()
				.Where(entry => entry.GetStructuredStateValue("PenId") == penIdText)
				.Where(entry => entry.StructuredState?.Any(pair => pair.Key == argument) == true)
				.Select(entry => entry.Level)
		];
	}
}
