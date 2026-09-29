using System.Globalization;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
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
	public async Task TwoReadsWithNoWriteBetween_GiveAnEmptyDelta()
	{
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var provider = services.GetRequiredService<IDataProvider>();

		var delta = PenListDelta.Between(await ReadPensAsync(provider), await ReadPensAsync(provider));

		delta.IsEmpty.Should().BeTrue();
		delta.Current.Select(pen => pen.PenId).Should().BeEquivalentTo([ChamberPressureId, HeaterId, UncommissionedId]);
	}

	[Fact]
	public async Task AColourWrittenThroughTheEditor_GivesOneRevisionCarryingIt()
	{
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var provider = services.GetRequiredService<IDataProvider>();
		var editor = services.GetRequiredService<IPenCatalogueEditor>();
		var before = await ReadPensAsync(provider);

		var written = await editor.ChangeAsync(
			await StoredPenAsync(editor, HeaterId),
			new PenSettingChange.Color("#ABCDEF"));

		written.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(written));

		var delta = PenListDelta.Between(before, await ReadPensAsync(provider));

		var revision = delta.Revised.Should().ContainSingle().Which;

		revision.Current.PenId.Should().Be(HeaterId);
		revision.Current.Color.Should().Be("#ABCDEF");
		revision.ScaleChanged.Should().BeFalse();
		delta.ChangesPenSet.Should().BeFalse();
	}

	[Fact]
	public async Task AMembershipWrittenThroughTheEditor_GivesARevisionWhoseGroupsDiffer()
	{
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var provider = services.GetRequiredService<IDataProvider>();
		var editor = services.GetRequiredService<IPenCatalogueEditor>();
		var before = await ReadPensAsync(provider);

		var catalogue = await editor.ReadAsync();

		catalogue.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(catalogue));

		var written = await editor.SetMembershipAsync(
			catalogue.Value.Pens.Should().ContainSingle(pen => pen.Id == HeaterId).Which,
			catalogue.Value.Groups.Should().ContainSingle(group => group.Name == VacuumGroup).Which,
			true);

		written.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(written));

		var delta = PenListDelta.Between(before, await ReadPensAsync(provider));

		var revision = delta.Revised.Should().ContainSingle().Which;

		revision.Current.PenId.Should().Be(HeaterId);
		revision.Previous.Groups.Should().NotContain(VacuumGroup);
		revision.Current.Groups.Should().Contain(VacuumGroup);
		delta.ChangesPenSet.Should().BeFalse();
	}

	[Fact]
	public async Task TwoReadsOfAPenStoredWithNoColour_LogOneWarningNotTwo()
	{
		using var logs = new FakeLoggerProvider();
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString, logs);

		var provider = services.GetRequiredService<IDataProvider>();

		await ReadPensAsync(provider);
		await ReadPensAsync(provider);

		LevelsOf(logs, typeof(PostgresDataProvider).FullName!).Should().Equal(LogLevel.Warning, LogLevel.Debug);
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
		await ReadPensAsync(provider);
		await ReadPensAsync(provider);
		await ArchiveDatabase.ExecuteAsync(
			Database.AdminConnectionString,
			_handEditedAgainCommand,
			TestContext.Current.CancellationToken);
		await ReadPensAsync(provider);

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

	private static async Task<IReadOnlyList<Pen>> ReadPensAsync(IDataProvider provider)
	{
		var pens = await provider.QueryPensAsync();

		pens.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(pens));

		return pens.Value;
	}

	private static async Task<StoredPen> StoredPenAsync(IPenCatalogueEditor editor, int penId)
	{
		var catalogue = await editor.ReadAsync();

		catalogue.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(catalogue));

		return catalogue.Value.Pens.Should().ContainSingle(pen => pen.Id == penId).Which;
	}

	private static IReadOnlyList<LogLevel> LevelsOf(FakeLoggerProvider logs, string category)
	{
		return
		[
			.. logs.Collector.GetSnapshot()
				.Where(entry => entry.Category == category)
				.Select(entry => entry.Level)
		];
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
