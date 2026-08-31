using Npgsql;

using NpgsqlTypes;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// Each assertion is scoped to one layer and to one period's own bounds, because the two statements write
// into different periods: the closed flush into the period a pair of instants leaves, the opening row into
// the period it lands in. A count over the whole table would confuse the two.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class CoarseFlushTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	private const int PenCount = 3;

	private const string ReadRowsCommand = """
		SELECT id, t, v, q
		FROM public.trends
		WHERE l = @layer AND t >= @fromInclusive AND t < @toExclusive
		ORDER BY id, t;
		""";

	private const string CountRowCommand =
		"SELECT count(*) FROM public.trends WHERE l = @layer AND id = @id AND t = @t;";

	private static readonly string _insertNullRowCommand = $"""
		INSERT INTO public.trends (id, l, t, v, q)
		VALUES (@id, {ArchiveRow.RawLayer}, @t, NULL, {ArchiveRow.OrdinaryQuality});
		""";

	// The pens the follow loop itself writes, so the opening-row statement probes the same identifiers this
	// archive carries.
	private static readonly IReadOnlyList<int> _penIds =
		RawLayerGenerator.SelectPens(PenCount).Select(pen => pen.PenId).ToArray();

	// One row per pen at each coarse layer: what the first call inside a period writes and what no later
	// call inside that period repeats.
	private static readonly long _openingRowsPerPeriod = _penIds.Count * LayerThinner.CoarseLayers.Count;

	private static readonly DateTime _day = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified);

	private static readonly DateTime _previousDay = _day.AddDays(-1);

	// One sample every ten seconds gives a minute six rows per pen — enough for a first, a last, an
	// interior minimum and an interior maximum to be four distinct rows.
	private static readonly TimeSpan _sampleInterval = TimeSpan.FromSeconds(10);

	// Inside the last minute of the day, so one marker row sits in a closing minute, hour and day at once.
	private static readonly DateTime _markerInstant = _day.AddHours(23).AddMinutes(59).AddSeconds(30);

	// The archive spans the last minute of the previous day and the last hour and a minute of this one:
	// the first gives the day a period before the flushed one that carries rows, and the second gives an
	// hour boundary that is not also a day boundary.
	private static readonly DateTime _rawStart = _previousDay.AddHours(23).AddMinutes(59);

	private static readonly DateTime _rawEndExclusive = _day.AddDays(1);

	private static readonly IReadOnlyList<ArchiveRow> _rawRows = BuildRawRows();

	protected override async ValueTask SeedAsync()
	{
		await Writer().WriteAsync(_rawRows, _rawStart, _rawEndExclusive);
	}

	[Fact]
	public async Task TheClosedMinuteCarriesExactlyTheRowsTheThinnerSelects()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;
		var minute = _day.AddHours(23).AddMinutes(10);
		var expected = ExpectedThin(LayerThinner.MinuteLayer, minute);

		Assert.NotEmpty(expected);

		var flushed = await CoarseFlush.FlushAsync(
			Options(), minute.AddSeconds(30), minute.AddSeconds(65), cancellationToken);
		Assert.Equal(expected, await ReadPeriodAsync(LayerThinner.MinuteLayer, minute, cancellationToken));
	}

	[Fact]
	public async Task TheSecondFlushOfOnePeriodInsertsNothingAndLeavesItAsItWas()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;
		var minute = _day.AddHours(23).AddMinutes(10);
		var previousTick = minute.AddSeconds(30);
		var now = minute.AddSeconds(65);

		var first = await CoarseFlush.FlushAsync(Options(), previousTick, now, cancellationToken);

		var afterFirst = await ReadPeriodAsync(LayerThinner.MinuteLayer, minute, cancellationToken);

		Assert.NotEmpty(afterFirst);

		var second = await CoarseFlush.FlushAsync(Options(), previousTick, now, cancellationToken);
		Assert.Equal(0L, second);
		Assert.Equal(afterFirst, await ReadPeriodAsync(LayerThinner.MinuteLayer, minute, cancellationToken));
	}

	[Fact]
	public async Task AMarkerRowReachesEveryCoarseLayer()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;
		var previousTick = _markerInstant.AddSeconds(15);
		var now = _day.AddDays(1).AddSeconds(5);

		var flushed = await CoarseFlush.FlushAsync(Options(), previousTick, now, cancellationToken);

		foreach (var layer in LayerThinner.CoarseLayers)
		{
			var periodStart = LayerThinner.PeriodStart(previousTick, layer);
			var rows = await ReadPeriodAsync(layer, periodStart, cancellationToken);

			Assert.Contains(
				rows,
				row => row.Id == _penIds[2]
					&& row.Timestamp == _markerInstant
					&& row.Quality == ArchiveRow.LastBeforeBreakQuality);

			Assert.Equal(ExpectedThin(layer, periodStart), rows);
		}
	}

	// The preceding period of every layer carries raw rows, so a gate that fired where no period closed
	// would be visible in all three.
	[Fact]
	public async Task APairInsideOneMinuteClosesNoPeriodAtAnyLayer()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;
		var minute = _day.AddHours(23).AddMinutes(10);
		var now = minute.AddSeconds(50);

		var flushed = await CoarseFlush.FlushAsync(
			Options(), minute.AddSeconds(20), now, cancellationToken);

		Assert.Empty(
			await ReadPeriodAsync(LayerThinner.MinuteLayer, minute.AddMinutes(-1), cancellationToken));

		Assert.Empty(
			await ReadPeriodAsync(LayerThinner.HourLayer, _day.AddHours(22), cancellationToken));

		Assert.Empty(await ReadPeriodAsync(LayerThinner.DayLayer, _previousDay, cancellationToken));

		await AssertOnlyTheOpeningRowsAsync(now, cancellationToken);
	}

	[Fact]
	public async Task APairCrossingAnHourClosesTheMinuteAndTheHourAndNotTheDay()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;
		var hour = _day.AddHours(22);
		var minute = hour.AddMinutes(59);
		var now = hour.AddHours(1).AddSeconds(20);

		var flushed = await CoarseFlush.FlushAsync(
			Options(), minute.AddSeconds(40), now, cancellationToken);

		Assert.Equal(
			ExpectedThin(LayerThinner.MinuteLayer, minute),
			await ReadPeriodAsync(LayerThinner.MinuteLayer, minute, cancellationToken));

		Assert.Equal(
			ExpectedThin(LayerThinner.HourLayer, hour),
			await ReadPeriodAsync(LayerThinner.HourLayer, hour, cancellationToken));

		Assert.Empty(await ReadPeriodAsync(LayerThinner.DayLayer, _previousDay, cancellationToken));

		await AssertOnlyTheOpeningRowsAsync(now, cancellationToken);
	}

	// The seeder thins over the period its fill ends part-way through, so the archive already carries a
	// coarse row for it before the demo writer starts. A COPY has no conflict handling and dies on it.
	[Fact]
	public async Task AFlushOverAPeriodTheSeederAlreadyThinnedAddsNoDuplicate()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;
		var minute = _day.AddHours(23).AddMinutes(10);
		var expected = ExpectedThin(LayerThinner.MinuteLayer, minute);
		var alreadyThinned = expected.GroupBy(row => row.Id).Select(pen => pen.First()).ToArray();

		var written = await Writer().WriteAsync(
			alreadyThinned,
			minute,
			minute.AddMinutes(1),
			allowExistingRows: true,
			cancellationToken);

		var flushed = await CoarseFlush.FlushAsync(
			Options(), minute.AddSeconds(30), minute.AddSeconds(65), cancellationToken);
		Assert.Equal(expected, await ReadPeriodAsync(LayerThinner.MinuteLayer, minute, cancellationToken));
	}

	// The day layer is the case worth reading: the period opens at midnight, its first raw row lands at 22:59.
	[Fact]
	public async Task ACallInsideAPeriodOpensEveryLayerWithItsFirstRawRow()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;
		var minute = _day.AddHours(23).AddMinutes(10);

		var flushed = await CoarseFlush.FlushAsync(
			Options(), minute.AddSeconds(10), minute.AddSeconds(40), cancellationToken);
		Assert.Equal(_openingRowsPerPeriod, flushed);

		await AssertOnlyTheOpeningRowsAsync(minute.AddSeconds(40), cancellationToken);
	}

	[Fact]
	public async Task RepeatedCallsInsideOnePeriodDoNotDensifyTheCoarseLayers()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;
		var minute = _day.AddHours(23).AddMinutes(10);

		for (var call = 0; call < 10; call++)
		{
			var flushed = await CoarseFlush.FlushAsync(
				Options(), minute.AddSeconds(call), minute.AddSeconds(call + 1), cancellationToken);
			Assert.Equal(call == 0 ? _openingRowsPerPeriod : 0L, flushed);
		}

		await AssertOnlyTheOpeningRowsAsync(minute.AddSeconds(10), cancellationToken);
	}

	[Fact]
	public async Task ACallSpanningSeveralPeriodsClosesEveryOneOfThem()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;
		var firstMinute = _day.AddHours(23).AddMinutes(10);
		var now = firstMinute.AddMinutes(4).AddSeconds(20);

		var flushed = await CoarseFlush.FlushAsync(
			Options(), firstMinute.AddSeconds(30), now, cancellationToken);

		for (var offset = 0; offset < 4; offset++)
		{
			var minute = firstMinute.AddMinutes(offset);
			var expected = ExpectedThin(LayerThinner.MinuteLayer, minute);

			Assert.NotEmpty(expected);
			Assert.Equal(expected, await ReadPeriodAsync(LayerThinner.MinuteLayer, minute, cancellationToken));
		}

		// The period before the span the call covers is not this call's to close.
		Assert.Empty(
			await ReadPeriodAsync(LayerThinner.MinuteLayer, firstMinute.AddMinutes(-1), cancellationToken));

		await AssertOnlyTheOpeningRowsAsync(now, cancellationToken);
	}

	// ArchiveWriter cannot write a NULL, so it goes in over the admin connection.
	[Fact]
	public async Task ANullValuedRawRowIsNotSelectedAsAPeriodsMaximum()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;
		var minute = _day.AddHours(23).AddMinutes(10);

		// Between two sampled instants and inside the minute, so the row is neither its first nor its last
		// and only the value orderings can select it.
		var nullInstant = minute.AddSeconds(25);

		await InsertNullValuedRawRowAsync(_penIds[0], nullInstant, cancellationToken);

		var flushed = await CoarseFlush.FlushAsync(
			Options(), minute.AddSeconds(30), minute.AddSeconds(65), cancellationToken);

		Assert.Equal(
			0L,
			await CountCoarseRowsAtAsync(LayerThinner.MinuteLayer, _penIds[0], nullInstant, cancellationToken));

		Assert.Equal(
			ExpectedThin(LayerThinner.MinuteLayer, minute),
			await ReadPeriodAsync(LayerThinner.MinuteLayer, minute, cancellationToken));
	}

	[Fact]
	public async Task ClosingAPeriodOpenedEarlierLeavesTheRowsTheThinnerSelectsAndNoDuplicate()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;
		var minute = _day.AddHours(23).AddMinutes(10);

		var opened = await CoarseFlush.FlushAsync(
			Options(), minute.AddSeconds(10), minute.AddSeconds(40), cancellationToken);

		Assert.Equal(
			ExpectedOpening(LayerThinner.MinuteLayer, minute),
			await ReadPeriodAsync(LayerThinner.MinuteLayer, minute, cancellationToken));

		var closed = await CoarseFlush.FlushAsync(
			Options(), minute.AddSeconds(50), minute.AddSeconds(65), cancellationToken);

		Assert.Equal(
			ExpectedThin(LayerThinner.MinuteLayer, minute),
			await ReadPeriodAsync(LayerThinner.MinuteLayer, minute, cancellationToken));
	}

	[Fact]
	public async Task APeriodWithNoRawRowsYetWritesNothingAndReportsSuccess()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;
		var afterTheArchive = _rawEndExclusive.AddSeconds(30);

		var flushed = await CoarseFlush.FlushAsync(
			Options(), _rawEndExclusive.AddSeconds(10), afterTheArchive, cancellationToken);
		Assert.Equal(0L, flushed);

		foreach (var layer in LayerThinner.CoarseLayers)
		{
			Assert.Empty(await ReadPeriodAsync(
				layer, LayerThinner.PeriodStart(afterTheArchive, layer), cancellationToken));
		}
	}

	// Every coarse layer's open period holds one row per pen and that row is the period's first raw row.
	private async Task AssertOnlyTheOpeningRowsAsync(DateTime nowLocal, CancellationToken cancellationToken)
	{
		foreach (var layer in LayerThinner.CoarseLayers)
		{
			var periodStart = LayerThinner.PeriodStart(nowLocal, layer);

			Assert.Equal(
				ExpectedOpening(layer, periodStart),
				await ReadPeriodAsync(layer, periodStart, cancellationToken));
		}
	}

	// The follow options the demo writer runs with, minus the cadence, which no flush reads.
	private FollowOptions Options()
	{
		return new(
			Database.WriterConnectionString,
			TimeSpan.FromSeconds(1),
			PenCount,
			SeederOptions.DefaultSeed,
			SeederOptions.DefaultChangeSeconds);
	}

	// What LayerThinner selects for one period, in the order the read returns.
	private static IReadOnlyList<ArchiveRow> ExpectedThin(short layer, DateTime periodStart)
	{
		var periodEndExclusive = PeriodEndExclusive(periodStart, layer);

		var period = _rawRows
			.Where(row => row.Timestamp >= periodStart && row.Timestamp < periodEndExclusive)
			.ToArray();

		return LayerThinner.Thin(period, layer)
			.OrderBy(row => row.Id)
			.ThenBy(row => row.Timestamp)
			.ToArray();
	}

	// The first raw row at or after the period start, per pen, stamped at the coarse layer — the opening
	// statement's output, computed from the archive's rows rather than from the statement.
	private static IReadOnlyList<ArchiveRow> ExpectedOpening(short layer, DateTime periodStart)
	{
		return _rawRows
			.Where(row => row.Timestamp >= periodStart)
			.GroupBy(row => row.Id)
			.Select(pen => pen.OrderBy(row => row.Timestamp).First() with { Layer = layer })
			.OrderBy(row => row.Id)
			.ThenBy(row => row.Timestamp)
			.ToArray();
	}

	// Stated here rather than derived from the production code, so the bounds an assertion reads are
	// independent of the bounds the statement binds.
	private static DateTime PeriodEndExclusive(DateTime periodStart, short layer)
	{
		return layer switch
		{
			LayerThinner.MinuteLayer => periodStart.AddMinutes(1),
			LayerThinner.HourLayer => periodStart.AddHours(1),
			_ => periodStart.AddDays(1)
		};
	}

	// ArchiveWriter's COPY binds a plain double, so a NULL value can only be written straight over the
	// admin connection.
	private async Task InsertNullValuedRawRowAsync(int penId, DateTime at, CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(Database.AdminConnectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(_insertNullRowCommand, connection);

		command.Parameters.AddWithValue("id", NpgsqlDbType.Integer, penId);
		command.Parameters.AddWithValue("t", NpgsqlDbType.Timestamp, at);

		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	// Counted rather than read back as an ArchiveRow, whose Value is a plain double: the question is
	// whether the row reached the coarse layer at all.
	private async Task<long> CountCoarseRowsAtAsync(
		short layer,
		int penId,
		DateTime at,
		CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(Database.AdminConnectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(CountRowCommand, connection);

		command.Parameters.AddWithValue("layer", NpgsqlDbType.Smallint, layer);
		command.Parameters.AddWithValue("id", NpgsqlDbType.Integer, penId);
		command.Parameters.AddWithValue("t", NpgsqlDbType.Timestamp, at);

		return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
	}

	private async Task<IReadOnlyList<ArchiveRow>> ReadPeriodAsync(
		short layer,
		DateTime periodStart,
		CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(Database.AdminConnectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(ReadRowsCommand, connection);

		command.Parameters.AddWithValue("layer", NpgsqlDbType.Smallint, layer);
		command.Parameters.AddWithValue("fromInclusive", NpgsqlDbType.Timestamp, periodStart);
		command.Parameters.AddWithValue(
			"toExclusive", NpgsqlDbType.Timestamp, PeriodEndExclusive(periodStart, layer));

		await using var reader = await command.ExecuteReaderAsync(cancellationToken);

		var rows = new List<ArchiveRow>();

		while (await reader.ReadAsync(cancellationToken))
		{
			rows.Add(new ArchiveRow(
				reader.GetInt32(0),
				layer,
				reader.GetDateTime(1),
				reader.GetDouble(2),
				reader.GetInt32(3)));
		}

		return rows;
	}

	private static IReadOnlyList<ArchiveRow> BuildRawRows()
	{
		var rows = new List<ArchiveRow>();

		AppendSlice(rows, _rawStart, _day);
		AppendSlice(rows, _day.AddHours(22).AddMinutes(59), _rawEndExclusive);

		return rows;
	}

	private static void AppendSlice(List<ArchiveRow> rows, DateTime from, DateTime toExclusive)
	{
		for (var index = 0; from + _sampleInterval * index < toExclusive; index++)
		{
			var at = from + _sampleInterval * index;

			for (var penIndex = 0; penIndex < _penIds.Count; penIndex++)
			{
				rows.Add(new ArchiveRow(
					_penIds[penIndex],
					ArchiveRow.RawLayer,
					at,
					ValueFor(penIndex, index),
					QualityFor(penIndex, at)));
			}
		}
	}

	// One pen walks over a wide range, one holds a single value so that every row of a period ties on the
	// minimum and the maximum at once, and one walks over a range of its own and carries the marker.
	private static double ValueFor(int penIndex, int index)
	{
		return penIndex switch
		{
			0 => index * 37 % 101 + 0.5,
			1 => 42.0,
			_ => index * 53 % 89 + 0.25
		};
	}

	private static int QualityFor(int penIndex, DateTime at)
	{
		return penIndex == 2 && at == _markerInstant
			? ArchiveRow.LastBeforeBreakQuality
			: ArchiveRow.OrdinaryQuality;
	}
}
