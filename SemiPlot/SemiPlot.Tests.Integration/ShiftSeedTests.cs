using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using SemiPlot.Core.Data;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

// A shifts seed through the fill converge runs, into a provisioned clone: rows as scada_writer, then the
// recipe and operator markers as the same role, read back through the viewer's own marker store.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class ShiftSeedTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	private const string WeekendRowCountCommand =
		"SELECT count(*) FROM public.trends WHERE extract(isodow FROM t) IN (6, 7);";

	private const string RowCountCommand = "SELECT count(*) FROM public.trends;";

	private const string RawMarkerRowsCommand = """
		SELECT id, t, q FROM public.trends WHERE l = 0 AND q <> 0 ORDER BY id, t;
		""";

	private const string RawRowsCommand = "SELECT id, t FROM public.trends WHERE l = 0 ORDER BY id, t;";

	private const string LayerCountsCommand = "SELECT l, count(*) FROM public.trends GROUP BY l ORDER BY l;";

	// Fourteen days to a Friday midnight: two weekends, and a last session that stretches to midnight only.
	private static readonly SeederOptions _shape = new(
		string.Empty,
		new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified),
		Days: 14,
		PenCount: 7,
		SeederOptions.DefaultSeed,
		ChangeSeconds: 30.0,
		BreakCount: 0,
		AdminConnectionString: null,
		ArchiveSchedule.Shifts);

	private SeederOptions Options => _shape with { ConnectionString = Database.WriterConnectionString };

	private SeedFillResult Fill
	{
		get => field ?? throw new InvalidOperationException("The fill was read before the seed ran.");
		set;
	}

	protected override async ValueTask SeedAsync()
	{
		Fill = await SeedFiller.FillAsync(Options, TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task TheSeedHoldsNoSaturdayOrSundayRow()
	{
		(await ScalarAsync(RowCountCommand)).Should().BePositive();
		(await ScalarAsync(WeekendRowCountCommand)).Should().Be(0);
	}

	[Fact]
	public async Task EveryInnerSessionEdgeCarriesItsBreakMarker()
	{
		var offPeriods = BreakPlan.Create(Options).Breaks;
		var markerRows = await ReadAsync(RawMarkerRowsCommand, reader => (
			Id: reader.GetInt32(0), Time: reader.GetDateTime(1), Quality: reader.GetInt32(2)));
		var rawRows = await ReadAsync(RawRowsCommand, reader => (Id: reader.GetInt32(0), Time: reader.GetDateTime(1)));
		var lastRowsBeforeOff = rawRows
			.GroupBy(row => row.Id)
			.ToDictionary(
				pen => pen.Key,
				pen => offPeriods.Select(off => pen.Last(row => row.Time < off.Start).Time).ToArray());

		offPeriods.Count.Should().BeGreaterThanOrEqualTo(5);
		lastRowsBeforeOff.Should().HaveCount(Options.PenCount);

		foreach (var (pen, lastBeforeOff) in lastRowsBeforeOff)
		{
			var rows = markerRows.Where(row => row.Id == pen).ToArray();

			rows.Where(row => row.Quality == ArchiveRow.FirstAfterBreakQuality).Select(row => row.Time)
				.Should().Equal(offPeriods.Select(off => off.End));
			rows.Where(row => row.Quality == ArchiveRow.LastBeforeBreakQuality).Select(row => row.Time)
				.Should().Equal(lastBeforeOff);
		}
	}

	// Expected from the drawn runs, not from the writer's plan: each run that opens in the span and closes
	// before its end, at the instant its local wall-clock time names.
	[Fact]
	public async Task EveryRecipeRunHasOneLockedStartAndOneClosingMarkerAtItsInstant()
	{
		var schedule = new ShiftSchedule(Options.Seed, TimeSpan.FromSeconds(Options.ChangeSeconds).Ticks);
		var spanStart = BreakPlan.Create(Options).Runs[0].Start;
		var runs = Enumerable.Range(0, Options.Days + 1)
			.SelectMany(offset => schedule.RecipeRuns(DateOnly.FromDateTime(Options.Start).AddDays(offset)))
			.Where(run => run.Start >= spanStart && run.End < Options.End)
			.ToArray();
		var expected = runs.SelectMany(run => new[]
		{
			(Time: Instant(run.Start), Kind: MarkerKind.RecipeStart),
			(Time: Instant(run.End), Kind: run.IsInterrupted ? MarkerKind.RecipeInterrupted : MarkerKind.RecipeEnd),
		});

		var markers = (await ReadMarkersAsync()).Where(marker => marker.Kind != MarkerKind.User).ToArray();

		runs.Should().NotBeEmpty();
		Fill.RecipeMarkersWritten.Should().Be(2 * runs.Length);
		markers.Select(marker => (marker.TimeUtc, marker.Kind)).Should().Equal(expected);
		markers.Should().OnlyContain(marker => marker.IsLocked && marker.TimeUtc.Kind == DateTimeKind.Utc);
		markers.Chunk(2).Should().AllSatisfy(pair => pair[1].Label.Should().Be(pair[0].Label).And.NotBeNullOrEmpty());
		markers.Select(marker => marker.Kind).Should().Contain(MarkerKind.RecipeInterrupted);
	}

	[Fact]
	public async Task TheOperatorMarkersReadBackUnlocked_AtTheirPlannedInstants()
	{
		var planned = ShiftMarkerWriter.OperatorPlan(Options);

		var markers = (await ReadMarkersAsync()).Where(marker => marker.Kind == MarkerKind.User).ToArray();

		planned.Should().NotBeEmpty();
		Fill.OperatorMarkersWritten.Should().Be(planned.Count);
		markers.Select(marker => (marker.TimeUtc, marker.Label)).Should()
			.Equal(planned.Select(marker => (Instant(marker.Time), marker.Label)));
		markers.Should().OnlyContain(marker => !marker.IsLocked && marker.TimeUtc.Kind == DateTimeKind.Utc);
	}

	[Fact]
	public async Task AMarkersWallClock_IsBoundThroughTheWritersZone_NotTheHosts()
	{
		const string Label = "Berlin.csv";
		var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
		var writer = new ShiftMarkerWriter(Database.WriterConnectionString, berlin);
		var summerMorning = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Unspecified);

		await writer.WriteAsync(
			[new SeededMarker(summerMorning, Label, MarkerKind.RecipeStart, IsLocked: true)],
			TestContext.Current.CancellationToken);

		var marker = (await ReadMarkersAsync()).Should().ContainSingle(stored => stored.Label == Label).Subject;
		marker.TimeUtc.Should().Be(
			new DateTime(2026, 7, 1, 6, 0, 0, DateTimeKind.Utc),
			"08:00 of a Berlin summer day is 06:00 UTC on every host");
	}

	[Fact]
	public async Task TheReportedLayerCountsAreTheArchives()
	{
		var counts = await ReadAsync(
			LayerCountsCommand,
			reader => (Layer: reader.GetInt16(0), Count: reader.GetInt64(1)));

		Fill.RowsPerLayer.Select(entry => (Layer: entry.Key, Count: entry.Value)).Should().Equal(counts);
		Fill.RowsPerLayer.Values.Sum().Should().Be(Fill.RowsWritten);
		counts.Select(entry => entry.Layer).Should()
			.Equal(ArchiveRow.RawLayer, LayerThinner.MinuteLayer, LayerThinner.HourLayer);
	}

	private static DateTime Instant(DateTime wallClock)
	{
		return TimeZoneInfo.ConvertTimeToUtc(wallClock, TimeZoneInfo.Local);
	}

	private async Task<IReadOnlyList<StoredMarker>> ReadMarkersAsync()
	{
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var markers = await services.GetRequiredService<IMarkerStore>()
			.ReadAsync(TestContext.Current.CancellationToken);

		markers.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(markers));

		return markers.Value;
	}

	private Task<long> ScalarAsync(string statement)
	{
		return ArchiveDatabase.ScalarAsync<long>(
			Database.AdminConnectionString, statement, cancellationToken: TestContext.Current.CancellationToken);
	}

	private Task<IReadOnlyList<TRow>> ReadAsync<TRow>(string statement, Func<NpgsqlDataReader, TRow> readRow)
	{
		return ArchiveDatabase.ReadAsync(
			Database.AdminConnectionString,
			statement,
			readRow,
			cancellationToken: TestContext.Current.CancellationToken);
	}
}
