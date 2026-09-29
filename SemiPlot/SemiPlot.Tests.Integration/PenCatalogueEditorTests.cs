using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

// Every write runs as semiplot through the resolved editor, so a passing write also proves its grant; the
// catalogue is seeded as postgres, because semiplot may not insert a pen.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class PenCatalogueEditorTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	private const int ChamberPressureId = 101;

	private const int UncommissionedId = 105;

	private const string VacuumGroup = "Vacuum";

	private const string HeatersGroup = "Heaters";

	// A stored NULL colour and a mask PenValueFormat refuses, both of which the chart's read normalises away.
	private const string UncommissionedTagCommand =
		"""
		INSERT INTO public.semiplot_tags (id, name, format, color, line_style)
		VALUES (105, 'Uncommissioned', '%0.0', NULL, 1);
		""";

	private const string CountHeaterMembershipsCommand =
		"SELECT count(*) FROM public.semiplot_pen_groups WHERE pen_id = ANY(@ids);";

	private static readonly int[] _heaterIds = [102, 103, 104];

	[Fact]
	public async Task ReadReturnsEveryPenAndGroupAsStored()
	{
		using var services = Build();

		var catalogue = await ReadAsync(Editor(services));

		catalogue.Pens.Select(pen => pen.Id).Should().Equal(101, 102, 103, 104, 105);
		catalogue.Pens[0].Should().Be(new StoredPen(
			ChamberPressureId,
			"Chamber pressure",
			"Pa",
			"0.0",
			"#112233",
			PenLineStyle.Interpolated,
			true,
			0,
			100));
		catalogue.Groups.Select(group => group.Name).Should().Equal(HeatersGroup, VacuumGroup);
		Group(catalogue, HeatersGroup).MemberPenIds.Should().Equal(_heaterIds);
		Group(catalogue, VacuumGroup).MemberPenIds.Should().Equal(ChamberPressureId);
	}

	[Fact]
	public async Task ReadReturnsANullColourAsNullAndAnUnusableMaskAsItIsStored()
	{
		using var services = Build();

		var catalogue = await ReadAsync(Editor(services));

		var uncommissioned = catalogue.Pens.Should().ContainSingle(pen => pen.Id == UncommissionedId).Which;

		uncommissioned.Color.Should().BeNull();
		uncommissioned.Format.Should().Be("%0.0");
		uncommissioned.LineStyle.Should().Be(PenLineStyle.Stepped);
	}

	[Theory]
	[InlineData(nameof(PenSettingChange.Name))]
	[InlineData(nameof(PenSettingChange.Unit))]
	[InlineData(nameof(PenSettingChange.Format))]
	[InlineData(nameof(PenSettingChange.Color))]
	[InlineData(nameof(PenSettingChange.LineStyle))]
	[InlineData(nameof(PenSettingChange.EnabledOnStart))]
	[InlineData(nameof(PenSettingChange.Scale))]
	public async Task EachChangeWritesItsOwnColumnAndNothingElse(string arm)
	{
		using var services = Build();

		var editor = Editor(services);
		var before = await PenAsync(editor, ChamberPressureId);
		var (change, expected) = Arm(arm, before);

		var result = await editor.ChangeAsync(before, change);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		(await PenAsync(editor, ChamberPressureId)).Should().Be(expected);
	}

	[Fact]
	public async Task TwoInstancesWritingTwoColumnsOfOnePenBothSurvive()
	{
		using var first = Build();
		using var second = Build();

		var pen = await PenAsync(Editor(first), ChamberPressureId);

		var unit = await Editor(first).ChangeAsync(pen, new PenSettingChange.Unit("mbar"));
		var name = await Editor(second).ChangeAsync(pen, new PenSettingChange.Name("Load lock pressure"));

		unit.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(unit));
		name.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(name));

		var stored = await PenAsync(Editor(first), ChamberPressureId);

		stored.Unit.Should().Be("mbar");
		stored.Name.Should().Be("Load lock pressure");
	}

	[Fact]
	public async Task ANullUnitOrMaskIsStoredAsNull()
	{
		using var services = Build();

		var editor = Editor(services);
		var pen = await PenAsync(editor, ChamberPressureId);

		(await editor.ChangeAsync(pen, new PenSettingChange.Unit(null))).IsSuccess.Should().BeTrue();
		(await editor.ChangeAsync(pen, new PenSettingChange.Format(null))).IsSuccess.Should().BeTrue();

		var stored = await PenAsync(editor, ChamberPressureId);

		stored.Unit.Should().BeNull();
		stored.Format.Should().BeNull();
	}

	// semiplot_tags_scale_paired refuses a half-set pair between two statements, so a pen with no scale
	// taking both bounds, and giving both back, proves each pair is one statement.
	[Fact]
	public async Task TheScalePairWritesBothBoundsInOneStatement()
	{
		using var services = Build();

		var editor = Editor(services);
		var pen = await PenAsync(editor, UncommissionedId);

		var set = await editor.ChangeAsync(pen, new PenSettingChange.Scale(-5, 5));

		set.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(set));
		(await PenAsync(editor, UncommissionedId)).Should().Be(pen with { ScaleMin = -5, ScaleMax = 5 });

		var cleared = await editor.ChangeAsync(pen, new PenSettingChange.Scale(null, null));

		cleared.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(cleared));
		(await PenAsync(editor, UncommissionedId)).Should().Be(pen);
	}

	[Theory]
	[InlineData(10.0, null)]
	[InlineData(null, 10.0)]
	[InlineData(50.0, 5.0)]
	public async Task AScalePairTheServerRefusesIsARejectedValueNamingThePen(double? min, double? max)
	{
		using var services = Build();

		var editor = Editor(services);
		var pen = await PenAsync(editor, ChamberPressureId);

		var result = await editor.ChangeAsync(pen, new PenSettingChange.Scale(min, max));

		FailureOf(result).Should().Be((ArchiveFault.ValueRejected, "Chamber pressure"));
		(await PenAsync(editor, ChamberPressureId)).Should().Be(pen);
	}

	[Fact]
	public async Task AColourTheServerRefusesIsARejectedValueNamingThePen()
	{
		using var services = Build();

		var editor = Editor(services);
		var pen = await PenAsync(editor, ChamberPressureId);

		var result = await editor.ChangeAsync(pen, new PenSettingChange.Color("red"));

		FailureOf(result).Should().Be((ArchiveFault.ValueRejected, "Chamber pressure"));
	}

	[Fact]
	public async Task CreatingAGroupReturnsTheIdTheCatalogueCarries()
	{
		using var services = Build();

		var editor = Editor(services);

		var created = await editor.CreateGroupAsync("Watchlist");

		created.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(created));

		var group = Group(await ReadAsync(editor), "Watchlist");

		group.Id.Should().Be(created.Value);
		group.MemberPenIds.Should().BeEmpty();
	}

	[Fact]
	public async Task CreatingAGroupUnderATakenNameIsNameTakenNamingTheNameAskedFor()
	{
		using var services = Build();

		var result = await Editor(services).CreateGroupAsync(VacuumGroup);

		FailureOf(result).Should().Be((ArchiveFault.NameTaken, VacuumGroup));
	}

	[Fact]
	public async Task RenamingAGroupWritesThatGroupOnly()
	{
		using var services = Build();

		var editor = Editor(services);
		var before = await ReadAsync(editor);

		var result = await editor.RenameGroupAsync(Group(before, VacuumGroup), "Pumps");

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		var after = await ReadAsync(editor);

		Group(after, "Pumps").Should().BeEquivalentTo(Group(before, VacuumGroup) with { Name = "Pumps" });
		Group(after, HeatersGroup).Should().BeEquivalentTo(Group(before, HeatersGroup));
	}

	[Fact]
	public async Task RenamingAGroupToATakenNameIsNameTakenNamingTheNewName()
	{
		using var services = Build();

		var editor = Editor(services);
		var vacuum = Group(await ReadAsync(editor), VacuumGroup);

		var result = await editor.RenameGroupAsync(vacuum, HeatersGroup);

		FailureOf(result).Should().Be((ArchiveFault.NameTaken, HeatersGroup));
	}

	[Fact]
	public async Task ChangingAPenThatIsGoneIsRowGoneNamingThePen()
	{
		using var services = Build();

		var editor = Editor(services);
		var pen = await PenAsync(editor, ChamberPressureId);

		await AdminAsync($"DELETE FROM public.semiplot_tags WHERE id = {ChamberPressureId};");

		var result = await editor.ChangeAsync(pen, new PenSettingChange.Unit("mbar"));

		FailureOf(result).Should().Be((ArchiveFault.RowGone, "Chamber pressure"));
	}

	[Fact]
	public async Task RenamingAGroupThatIsGoneIsRowGoneNamingTheGroup()
	{
		using var services = Build();

		var editor = Editor(services);
		var vacuum = Group(await ReadAsync(editor), VacuumGroup);

		await AdminAsync($"DELETE FROM public.semiplot_groups WHERE name = '{VacuumGroup}';");

		var result = await editor.RenameGroupAsync(vacuum, "Pumps");

		FailureOf(result).Should().Be((ArchiveFault.RowGone, VacuumGroup));
	}

	[Fact]
	public async Task DeletingAGroupThatIsGoneIsRowGoneNamingTheGroup()
	{
		using var services = Build();

		var editor = Editor(services);
		var vacuum = Group(await ReadAsync(editor), VacuumGroup);

		await AdminAsync($"DELETE FROM public.semiplot_groups WHERE name = '{VacuumGroup}';");

		var result = await editor.DeleteGroupAsync(vacuum);

		FailureOf(result).Should().Be((ArchiveFault.RowGone, VacuumGroup));
	}

	[Fact]
	public async Task AMembershipOfAGroupThatIsGoneIsRowGoneNamingTheGroup()
	{
		using var services = Build();

		var editor = Editor(services);
		var catalogue = await ReadAsync(editor);
		var vacuum = Group(catalogue, VacuumGroup);

		await AdminAsync($"DELETE FROM public.semiplot_groups WHERE name = '{VacuumGroup}';");

		var result = await editor.SetMembershipAsync(Pen(catalogue, UncommissionedId), vacuum, true);

		FailureOf(result).Should().Be((ArchiveFault.RowGone, VacuumGroup));
	}

	[Fact]
	public async Task AMembershipOfAPenThatIsGoneIsRowGoneNamingThePen()
	{
		using var services = Build();

		var editor = Editor(services);
		var catalogue = await ReadAsync(editor);
		var uncommissioned = Pen(catalogue, UncommissionedId);

		await AdminAsync($"DELETE FROM public.semiplot_tags WHERE id = {UncommissionedId};");

		var result = await editor.SetMembershipAsync(uncommissioned, Group(catalogue, VacuumGroup), true);

		FailureOf(result).Should().Be((ArchiveFault.RowGone, uncommissioned.Name));
	}

	[Fact]
	public async Task SettingAMembershipIsIdempotentBothWays()
	{
		using var services = Build();

		var editor = Editor(services);
		var catalogue = await ReadAsync(editor);
		var pen = Pen(catalogue, UncommissionedId);
		var vacuum = Group(catalogue, VacuumGroup);

		(await editor.SetMembershipAsync(pen, vacuum, true)).IsSuccess.Should().BeTrue();
		(await editor.SetMembershipAsync(pen, vacuum, true)).IsSuccess.Should().BeTrue();

		Group(await ReadAsync(editor), VacuumGroup).MemberPenIds.Should().Equal(ChamberPressureId, UncommissionedId);

		(await editor.SetMembershipAsync(pen, vacuum, false)).IsSuccess.Should().BeTrue();
		(await editor.SetMembershipAsync(pen, vacuum, false)).IsSuccess.Should().BeTrue();

		Group(await ReadAsync(editor), VacuumGroup).MemberPenIds.Should().Equal(ChamberPressureId);
	}

	// The grant is column-level UPDATE on the settings and no INSERT or DELETE: statements the editor never
	// issues, sent over its own data source, must be refused by the server rather than by this code.
	[Theory]
	[InlineData("INSERT INTO semiplot_tags (id, name) VALUES (4242, '4242');")]
	[InlineData("DELETE FROM semiplot_tags WHERE id = 101;")]
	[InlineData("UPDATE semiplot_tags SET id = 4242 WHERE id = 101;")]
	public async Task TheRoleCannotManageScadaKeys(string statement)
	{
		using var services = Build();

		await using var command = services.GetRequiredService<NpgsqlDataSource>().CreateCommand(statement);

		var thrown = await Record.ExceptionAsync(
			() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

		var refused = thrown.Should().BeOfType<PostgresException>().Which;
		var error = services.GetRequiredService<ArchiveExceptionMapper>().MapWrite(refused, "Chamber pressure");

		error.Should().BeOfType<ArchiveError>().Which.Kind.Should().Be(ArchiveFault.AccessDenied);
		(await ReadAsync(Editor(services))).Pens.Select(pen => pen.Id).Should().Equal(101, 102, 103, 104, 105);
	}

	[Fact]
	public async Task DeletingAGroupRemovesItsMembershipsAndKeepsItsPens()
	{
		using var services = Build();

		var editor = Editor(services);
		var heaters = Group(await ReadAsync(editor), HeatersGroup);

		heaters.MemberPenIds.Should().Equal(_heaterIds);

		var result = await editor.DeleteGroupAsync(heaters);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		(await CountHeaterMembershipsAsync()).Should().Be(0);

		var pens = await services.GetRequiredService<IDataProvider>().QueryPensAsync();

		pens.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(pens));
		pens.Value.Where(pen => _heaterIds.Contains(pen.PenId)).Should().HaveCount(_heaterIds.Length)
			.And.OnlyContain(pen => pen.Groups.Count == 0);
	}

	protected override async ValueTask SeedAsync()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		await new TagCatalogWriter(Database.AdminConnectionString).WriteAsync(
			[
				new SyntheticPen(ChamberPressureId, "Chamber pressure", [VacuumGroup], "#112233", 0, 100, "Pa", "0.0"),
				new SyntheticPen(_heaterIds[0], "Heater 1", [HeatersGroup], "#223344", 20, 850, "degC"),
				new SyntheticPen(_heaterIds[1], "Heater 2", [HeatersGroup], "#334455", 20, 850, "degC"),
				new SyntheticPen(_heaterIds[2], "Heater 3", [HeatersGroup], "#445566", 20, 850, "degC")
			],
			cancellationToken);

		await AdminAsync(UncommissionedTagCommand);
	}

	private static (PenSettingChange Change, StoredPen Expected) Arm(string arm, StoredPen pen)
	{
		return arm switch
		{
			nameof(PenSettingChange.Name)
				=> (new PenSettingChange.Name("Load lock pressure"), pen with { Name = "Load lock pressure" }),
			nameof(PenSettingChange.Unit) => (new PenSettingChange.Unit("mbar"), pen with { Unit = "mbar" }),
			nameof(PenSettingChange.Format) => (new PenSettingChange.Format("0.00"), pen with { Format = "0.00" }),
			nameof(PenSettingChange.Color) => (new PenSettingChange.Color("#ABCDEF"), pen with { Color = "#ABCDEF" }),
			nameof(PenSettingChange.LineStyle) => (
				new PenSettingChange.LineStyle(PenLineStyle.Stepped),
				pen with { LineStyle = PenLineStyle.Stepped }),
			nameof(PenSettingChange.EnabledOnStart)
				=> (new PenSettingChange.EnabledOnStart(false), pen with { EnabledOnStart = false }),
			nameof(PenSettingChange.Scale)
				=> (new PenSettingChange.Scale(-1, 1), pen with { ScaleMin = -1, ScaleMax = 1 }),
			_ => throw new ArgumentOutOfRangeException(nameof(arm), arm, "Unknown change arm.")
		};
	}

	private static (ArchiveFault Kind, string Detail) FailureOf(ResultBase result)
	{
		result.IsFailed.Should().BeTrue();

		var error = result.Errors.OfType<ArchiveError>().Should().ContainSingle().Which;

		return (error.Kind, error.Detail);
	}

	private static StoredPen Pen(PenCatalogue catalogue, int penId)
	{
		return catalogue.Pens.Should().ContainSingle(pen => pen.Id == penId).Which;
	}

	private static StoredGroup Group(PenCatalogue catalogue, string name)
	{
		return catalogue.Groups.Should().ContainSingle(group => group.Name == name).Which;
	}

	private static IPenCatalogueEditor Editor(IServiceProvider services)
	{
		return services.GetRequiredService<IPenCatalogueEditor>();
	}

	private static async Task<PenCatalogue> ReadAsync(IPenCatalogueEditor editor)
	{
		var catalogue = await editor.ReadAsync();

		catalogue.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(catalogue));

		return catalogue.Value;
	}

	private static async Task<StoredPen> PenAsync(IPenCatalogueEditor editor, int penId)
	{
		return Pen(await ReadAsync(editor), penId);
	}

	private ServiceProvider Build()
	{
		return ArchiveProviderFactory.Build(Database.PlotConnectionString);
	}

	private Task AdminAsync(string statement)
	{
		return ArchiveDatabase.ExecuteAsync(
			Database.AdminConnectionString,
			statement,
			TestContext.Current.CancellationToken);
	}

	private Task<long> CountHeaterMembershipsAsync()
	{
		return ArchiveDatabase.CountAsync(
			Database.AdminConnectionString,
			CountHeaterMembershipsCommand,
			_heaterIds,
			TestContext.Current.CancellationToken);
	}
}
