using System.Globalization;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Integration;

// Every statement runs as semiplot through the resolved store, so a passing write also proves its grant.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class MarkerStoreTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	private const string DropMarkersCommand = "DROP TABLE public.semiplot_markers;";

	private const string InsertRecipeStartCommand = """
	                                                INSERT INTO public.semiplot_markers (t, label, kind, locked)
	                                                VALUES ('2026-03-02 08:15:00+00', 'Etch.csv', 1, true);
	                                                """;

	private const string DropKindCheckCommand = """
	                                            DO $$
	                                            DECLARE check_name text;
	                                            BEGIN
	                                                FOR check_name IN
	                                                    SELECT conname FROM pg_constraint
	                                                    WHERE conrelid = 'public.semiplot_markers'::regclass
	                                                      AND contype = 'c'
	                                                      AND pg_get_constraintdef(oid) LIKE '%kind%'
	                                                LOOP
	                                                    EXECUTE format(
	                                                        'ALTER TABLE public.semiplot_markers DROP CONSTRAINT %I',
	                                                        check_name);
	                                                END LOOP;
	                                            END $$;
	                                            """;

	private const string InsertKindSevenCommand = """
	                                              INSERT INTO public.semiplot_markers (t, label, kind, locked)
	                                              VALUES ('2026-03-02 08:15:00+00', 'Etch.csv', 7, true);
	                                              """;

	private const int MaxLabelLength = 80;

	private static readonly DateTime _etchStart = new(2026, 3, 2, 8, 15, 0, DateTimeKind.Utc);

	// Europe/Berlin leaves summer time on 2026-10-25 at 01:00 UTC: both moments read 02:30 on a local clock.
	private static readonly DateTime _repeatedHourSummer = new(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc);
	private static readonly DateTime _repeatedHourWinter = new(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc);

	[Fact]
	public async Task AnAddReturnsTheIdTheReadCarries()
	{
		using var services = Build();

		var store = Store(services);

		var id = await AddAsync(store, _etchStart, "Etch start");

		(await ReadAsync(store)).Should().Equal(new StoredMarker(id, _etchStart, "Etch start"));
	}

	[Fact]
	public async Task AReadReturnsTheMarkersInTimeOrder()
	{
		using var services = Build();

		var store = Store(services);

		var late = await AddAsync(store, _etchStart.AddHours(2), "Purge");
		var early = await AddAsync(store, _etchStart, null);
		var middle = await AddAsync(store, _etchStart.AddHours(1), "Etch end");

		(await ReadAsync(store)).Select(marker => marker.Id).Should().Equal(early, middle, late);
	}

	[Fact]
	public async Task AMoveWritesThatMarkerOnly()
	{
		using var services = Build();

		var store = Store(services);
		var moved = await AddMarkerAsync(store, _etchStart, "Etch start");
		var kept = await AddMarkerAsync(store, _etchStart.AddHours(1), "Etch end");
		var target = _etchStart.AddMinutes(10);

		var result = await store.MoveAsync(moved, target);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		(await ReadAsync(store)).Should().Equal(moved with { TimeUtc = target }, kept);
	}

	[Theory]
	[InlineData("Etch end")]
	[InlineData(null)]
	public async Task ARenameWritesThatMarkerOnly(string? label)
	{
		using var services = Build();

		var store = Store(services);
		var renamed = await AddMarkerAsync(store, _etchStart, "Etch start");
		var kept = await AddMarkerAsync(store, _etchStart.AddHours(1), "Purge");

		var result = await store.RenameAsync(renamed, label);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		(await ReadAsync(store)).Should().Equal(renamed with { Label = label }, kept);
	}

	[Fact]
	public async Task ARowTheScadaInsertedReadsBackWithItsKindAndLock()
	{
		await AdminAsync(InsertRecipeStartCommand);

		using var services = Build();

		var marker = (await ReadAsync(Store(services))).Should().ContainSingle().Which;

		marker.Should().Be(new StoredMarker(marker.Id, _etchStart, "Etch.csv", MarkerKind.RecipeStart, IsLocked: true));
	}

	[Fact]
	public async Task AnAddedMarkerReadsAsAnUnlockedUserMarker()
	{
		using var services = Build();

		var store = Store(services);

		await AddAsync(store, _etchStart, "Etch start");

		var marker = (await ReadAsync(store)).Should().ContainSingle().Which;

		marker.Kind.Should().Be(MarkerKind.User);
		marker.IsLocked.Should().BeFalse();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ALockWritesThatMarkerOnly(bool isLocked)
	{
		await AdminAsync(InsertRecipeStartCommand);

		using var services = Build();

		var store = Store(services);
		var recipe = (await ReadAsync(store)).Should().ContainSingle().Which;
		var written = await AddMarkerAsync(store, _etchStart.AddHours(1), "Etch end");
		var kept = await AddMarkerAsync(store, _etchStart.AddHours(2), "Purge") with { IsLocked = !isLocked };
		await AdminAsync(string.Create(
			CultureInfo.InvariantCulture,
			$"UPDATE public.semiplot_markers SET locked = {kept.IsLocked} WHERE id = {kept.Id};"));

		var first = await store.SetLockedAsync(written, isLocked);
		var second = await store.SetLockedAsync(recipe, isLocked);

		first.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(first));
		second.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(second));
		(await ReadAsync(store)).Should().Equal(
			recipe with { IsLocked = isLocked },
			written with { IsLocked = isLocked },
			kept);
	}

	[Fact]
	public async Task AKindOutsideTheEnumIsShapeUnexpectedNamingTheValue()
	{
		await AdminAsync(DropKindCheckCommand);
		await AdminAsync(InsertKindSevenCommand);

		using var services = Build();

		var result = await Store(services).ReadAsync(TestContext.Current.CancellationToken);

		var (kind, detail) = FailureOf(result);
		kind.Should().Be(ArchiveFault.ShapeUnexpected);
		detail.Should().Contain("7");
	}

	[Theory]
	[InlineData("Move")]
	[InlineData("Rename")]
	[InlineData("Lock")]
	public async Task AWriteToADeletedMarkerIsMarkerGoneNamingIt(string write)
	{
		using var services = Build();

		var store = Store(services);
		var marker = await AddMarkerAsync(store, _etchStart, "Etch start");

		(await store.DeleteAsync(marker)).IsSuccess.Should().BeTrue();

		var result = write switch
		{
			"Move" => await store.MoveAsync(marker, _etchStart.AddMinutes(10)),
			"Rename" => await store.RenameAsync(marker, "Etch end"),
			"Lock" => await store.SetLockedAsync(marker, true),
			_ => throw new ArgumentOutOfRangeException(nameof(write), write, "Unknown write.")
		};

		FailureOf(result).Should().Be((ArchiveFault.MarkerGone, "Etch start"));
	}

	[Fact]
	public async Task ASecondDeleteSucceeds()
	{
		using var services = Build();

		var store = Store(services);
		var marker = await AddMarkerAsync(store, _etchStart, null);

		var first = await store.DeleteAsync(marker);
		var second = await store.DeleteAsync(marker);

		first.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(first));
		second.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(second));
		(await ReadAsync(store)).Should().BeEmpty();
	}

	[Fact]
	public async Task ALabelOfEightyCharactersIsStored()
	{
		using var services = Build();

		var store = Store(services);
		var label = new string('x', MaxLabelLength);

		var id = await AddAsync(store, _etchStart, label);

		(await ReadAsync(store)).Should().Equal(new StoredMarker(id, _etchStart, label));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(MaxLabelLength + 1)]
	public async Task AnAddWithALabelTheServerRefusesIsARejectedValueNamingTheMarker(int length)
	{
		using var services = Build();

		var store = Store(services);
		var label = new string('x', length);

		var result = await store.AddAsync(_etchStart, label);

		FailureOf(result).Should().Be((ArchiveFault.ValueRejected, RefusedSubject(label)));
		(await ReadAsync(store)).Should().BeEmpty();
	}

	[Theory]
	[InlineData(0)]
	[InlineData(MaxLabelLength + 1)]
	public async Task ARenameToALabelTheServerRefusesIsARejectedValueNamingTheMarker(int length)
	{
		using var services = Build();

		var store = Store(services);
		var marker = await AddMarkerAsync(store, _etchStart, null);
		var label = new string('x', length);

		var result = await store.RenameAsync(marker, label);

		FailureOf(result).Should().Be((ArchiveFault.ValueRejected, RefusedSubject(label)));
		(await ReadAsync(store)).Should().Equal(marker);
	}

	[Fact]
	public async Task AMomentWrittenAsUtcReadsBackEqualThroughTheRepeatedHour()
	{
		using var services = Build();

		var store = Store(services);

		var summer = await AddAsync(store, _repeatedHourSummer, "Summer time");
		var winter = await AddAsync(store, _repeatedHourWinter, "Winter time");

		var markers = await ReadAsync(store);

		markers.Should().Equal(
			new StoredMarker(summer, _repeatedHourSummer, "Summer time"),
			new StoredMarker(winter, _repeatedHourWinter, "Winter time"));
		markers.Should().OnlyContain(marker => marker.TimeUtc.Kind == DateTimeKind.Utc);
	}

	[Theory]
	[InlineData("Read")]
	[InlineData("Add")]
	[InlineData("Move")]
	[InlineData("Rename")]
	[InlineData("Lock")]
	[InlineData("Delete")]
	public async Task AStatementAgainstADroppedTableIsTableMissingNamingTheMarkers(string statement)
	{
		await AdminAsync(DropMarkersCommand);

		using var services = Build();

		var store = Store(services);
		var marker = new StoredMarker(1, _etchStart, "Etch start");

		ResultBase result = statement switch
		{
			"Read" => await store.ReadAsync(TestContext.Current.CancellationToken),
			"Add" => await store.AddAsync(_etchStart, "Etch start"),
			"Move" => await store.MoveAsync(marker, _etchStart.AddMinutes(10)),
			"Rename" => await store.RenameAsync(marker, "Etch end"),
			"Lock" => await store.SetLockedAsync(marker, true),
			"Delete" => await store.DeleteAsync(marker),
			_ => throw new ArgumentOutOfRangeException(nameof(statement), statement, "Unknown statement.")
		};

		FailureOf(result).Should().Be((ArchiveFault.TableMissing, ArchiveStatements.MarkersRelation));
	}

	private static string RefusedSubject(string label)
	{
		return label.Length == 0
			? _etchStart.ToLocalTime().ToString(PostgresMarkerStore.SubjectTimeFormat, CultureInfo.CurrentCulture)
			: label;
	}

	private static (ArchiveFault Kind, string Detail) FailureOf(ResultBase result)
	{
		result.IsFailed.Should().BeTrue();

		var error = result.Errors.OfType<ArchiveError>().Should().ContainSingle().Which;

		return (error.Kind, error.Detail);
	}

	private static IMarkerStore Store(IServiceProvider services)
	{
		return services.GetRequiredService<IMarkerStore>();
	}

	private static async Task<long> AddAsync(IMarkerStore store, DateTime timeUtc, string? label)
	{
		var added = await store.AddAsync(timeUtc, label);

		added.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(added));

		return added.Value;
	}

	private static async Task<StoredMarker> AddMarkerAsync(IMarkerStore store, DateTime timeUtc, string? label)
	{
		return new StoredMarker(await AddAsync(store, timeUtc, label), timeUtc, label);
	}

	private static async Task<IReadOnlyList<StoredMarker>> ReadAsync(IMarkerStore store)
	{
		var markers = await store.ReadAsync(TestContext.Current.CancellationToken);

		markers.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(markers));

		return markers.Value;
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
}
