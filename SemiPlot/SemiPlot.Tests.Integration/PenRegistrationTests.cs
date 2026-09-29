using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

// Registration runs as semiplot through the resolved editor, so every call also proves the EXECUTE grant.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class PenRegistrationTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	private const int RegisteredPenId = 7;

	private const string RegisterStatement = "SELECT semiplot_register_new_pens();";

	private const string DropFunctionCommand = "DROP FUNCTION public.semiplot_register_new_pens();";

	private const string CountPensCommand = "SELECT count(*) FROM public.semiplot_tags WHERE id = ANY(@ids);";

	private static readonly int[] _unregisteredPenIds = [4242, 4243, 4244];

	private static readonly DateTime _day = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

	// SemiBase's palette, spelled out rather than read off the function, so the test pins the delivered one.
	private static readonly string[] _registrationColors =
	[
		"#4E79A7", "#F28E2B", "#E15759", "#76B7B2", "#59A14F", "#EDC948",
		"#B07AA1", "#FF9DA7", "#9C755F", "#17BECF", "#D62728", "#9467BD"
	];

	// Far inside the role's 60 s idle_in_transaction_session_timeout, which would end the held call.
	private static readonly TimeSpan _holdTime = TimeSpan.FromMilliseconds(500);

	private static readonly TimeSpan _releaseBound = TimeSpan.FromSeconds(30);

	[Fact]
	public async Task AKeyWithNoPenBecomesAHiddenDefaultPenNamedByItsNumber()
	{
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var editor = services.GetRequiredService<IPenCatalogueEditor>();

		var registered = await editor.RegisterNewPensAsync();

		registered.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(registered));
		registered.Value.Should().Be(_unregisteredPenIds.Length);

		var pens = await ReadPensAsync(editor);
		var added = pens.Should().ContainSingle(pen => pen.Id == _unregisteredPenIds[0]).Which;

		added.Name.Should().Be("4242");
		added.EnabledOnStart.Should().BeFalse();
		added.Color.Should().BeOneOf(_registrationColors);
		added.Unit.Should().BeNull();
		added.Format.Should().BeNull();
		added.ScaleMin.Should().BeNull();
		added.ScaleMax.Should().BeNull();
		pens.Select(pen => pen.Id).Should().Equal(_unregisteredPenIds.Prepend(RegisteredPenId));
	}

	[Fact]
	public async Task ASecondRegistrationAddsNothing()
	{
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var editor = services.GetRequiredService<IPenCatalogueEditor>();

		await editor.RegisterNewPensAsync();

		var before = await ReadPensAsync(editor);
		var second = await editor.RegisterNewPensAsync();

		second.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(second));
		second.Value.Should().Be(0);
		(await ReadPensAsync(editor)).Should().Equal(before);
	}

	[Fact]
	public async Task ARegistrationStartedDuringAnUncommittedOneWaitsAndThenAddsNothing()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var editor = services.GetRequiredService<IPenCatalogueEditor>();

		await using var held = new NpgsqlConnection(Database.PlotConnectionString);

		await held.OpenAsync(cancellationToken);

		await using var transaction = await held.BeginTransactionAsync(cancellationToken);
		await using var heldCall = new NpgsqlCommand(RegisterStatement, held, transaction);

		var heldAdded = await heldCall.ExecuteScalarAsync(cancellationToken);

		var waiting = editor.RegisterNewPensAsync();

		await Task.WhenAny(waiting, Task.Delay(_holdTime, cancellationToken));

		waiting.IsCompleted.Should().BeFalse();

		await transaction.CommitAsync(cancellationToken);

		var result = await waiting.WaitAsync(_releaseBound, cancellationToken);

		heldAdded.Should().Be(_unregisteredPenIds.Length);
		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Should().Be(0);
		(await CountPensAsync(_unregisteredPenIds, cancellationToken)).Should().Be(_unregisteredPenIds.Length);
	}

	[Fact]
	public async Task AMissingRegistrationFunctionFailsAsAMissingRelationNamingIt()
	{
		await ArchiveDatabase.ExecuteAsync(
			Database.AdminConnectionString,
			DropFunctionCommand,
			TestContext.Current.CancellationToken);

		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var result = await services.GetRequiredService<IPenCatalogueEditor>().RegisterNewPensAsync();

		result.IsFailed.Should().BeTrue();

		var error = result.Errors.OfType<ArchiveError>().Should().ContainSingle().Which;

		error.Kind.Should().Be(ArchiveFault.TableMissing);
		error.Detail.Should().Be("semiplot_register_new_pens()");
		error.Database.Should().Be(Database.Name);
	}

	// One registered pen beside the unregistered keys, so the function has a key to skip as well as keys
	// to add. The rows go in as scada_writer, the role SCADA writes with.
	protected override async ValueTask SeedAsync()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		await new TagCatalogWriter(Database.AdminConnectionString).WriteAsync(
			[new SyntheticPen(RegisteredPenId, "Chamber pressure", [], "#112233", 0, 100)],
			cancellationToken);

		ArchiveRow[] rows =
		[
			.. _unregisteredPenIds.Prepend(RegisteredPenId).Select(penId => new ArchiveRow(
				penId,
				ArchiveRow.RawLayer,
				_day.AddMinutes(1),
				1.0,
				ArchiveRow.OrdinaryQuality))
		];

		await Writer().WriteAsync(rows, _day, _day.AddDays(1), cancellationToken: cancellationToken);
	}

	private static async Task<IReadOnlyList<StoredPen>> ReadPensAsync(IPenCatalogueEditor editor)
	{
		var catalogue = await editor.ReadAsync();

		catalogue.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(catalogue));

		return catalogue.Value.Pens;
	}

	private Task<long> CountPensAsync(int[] penIds, CancellationToken cancellationToken)
	{
		return ArchiveDatabase.CountAsync(Database.AdminConnectionString, CountPensCommand, penIds, cancellationToken);
	}
}
