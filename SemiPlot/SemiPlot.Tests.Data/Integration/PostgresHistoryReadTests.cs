using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The windowed history read against the bench. Every expectation is generated from the seeder rather than
// read back out of the archive, so an assertion covers the statement, the time conversion and the fold
// instead of comparing the query to itself.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class PostgresHistoryReadTests(
	PostgresContainerFixture postgresContainerFixture,
	SeededArchive seededArchive)
	: IClassFixture<SeededArchive>
{
	// Above the row count of every window this class reads, so MinMaxDecimator passes each row through one
	// column of its own and the envelopes compare against raw rows rather than against a second decimator
	// run. AssertMatchesSeededRows checks it per pen, so a denser bench says so instead of quietly turning
	// the comparison into a tautology.
	private const int TargetColumnCount = 4096;

	// A strict subset of the seeded pens, so a read that ignored the pen list would fail.
	private const int RequestedPenCount = 3;

	// The length of every window here that is not built around a break. Two minutes fits well inside an
	// archiving run — BreakPlan.MinimumRun keeps every run at least five minutes long — and spans some
	// 1 200 polls at the seeder's 100 ms interval, so the window is mostly row absence.
	private static readonly TimeSpan _quietWindowLength = TimeSpan.FromMinutes(2);

	// Archiving of at least this length surrounds every break, so a margin of one MinimumRun on each side
	// of the first break stays inside the archive and inside the runs that bound it.
	private static readonly TimeSpan _breakMargin = BreakPlan.MinimumRun;

	// How far into the first archiving run the steady window opens, and how far past the archive's end the
	// seed-only window opens. Both are well inside the statement's look-back floor of one partition width,
	// so a seed row exists in each case, and one minute plus the two-minute window still closes before
	// BreakPlan.MinimumRun puts the first break marker in reach.
	private static readonly TimeSpan _windowOffset = TimeSpan.FromMinutes(1);

	// The floor of the bound ArchiveStatements.SparseHistoryWindow puts on its backwards seek: one
	// partition width, widened to the requested window when that is wider. SeedLookBackFor computes it the
	// way the statement does, and TheExpectedSeedLookBackIsTheStatementsOwn pins the pairing, so widening
	// the SQL bound without widening this one fails here rather than leaving the expectation stale.
	private static readonly TimeSpan _seedLookBackFloor = TimeSpan.FromDays(1);

	// The clause that carries that bound. Held as a literal so the pin reads as the statement does.
	private const string SeedLookBackClause = "prior.t >= @from - greatest(@to - @from, interval '1 day')";

	// Wider than that floor, so the bound the statement applies over this window is the window itself.
	private static readonly TimeSpan _wideWindowLength = TimeSpan.FromDays(3);

	private static readonly ArchiveTimeConverter _timeConverter = new(ArchiveProviderFactory.SourceTimeZone);

	// Lazy rather than a plain initialiser: regenerating the seeder's whole day of raw rows costs real time
	// and memory, and a run with no container runtime skips every test here before touching any of it.
	private static readonly Lazy<BreakPlan> _breakPlan = new(() => BreakPlan.Create(ArchiveTemplate.Slice));

	private static readonly Lazy<IReadOnlyList<ArchiveRow>> _seededRawRows = new(GenerateSeededRawRows);

	private static readonly Lazy<IReadOnlyList<long>> _seededPenIds = new(SelectSeededPenIds);

	[Fact]
	public async Task AWindowInsideTheFirstRunReadsTheSeedersOwnRows()
	{
		postgresContainerFixture.RequireAvailable();

		var window = QuietWindow();

		var result = await ReadHistoryAsync(seededArchive.Database.ReaderConnectionString, window);

		AssertMatchesSeededRows(result, window);

		// The window opens on the instant every run starts, so every seeded pen has rows in it.
		Assert.Equal(ArchiveTemplate.Slice.PenCount, result.Value.Count);
	}

	// The pen list is not decoration: a read that ignored it would answer with every seeded pen and pass
	// every other test in this class, all of which ask for all eight.
	[Fact]
	public async Task OnlyTheRequestedPensComeBack()
	{
		postgresContainerFixture.RequireAvailable();

		var window = QuietWindow();
		// SelectPens walks the catalogue's groups round-robin, so its first three are a strict subset in no
		// particular order; the statement orders by id, so the expectation is sorted to match.
		var requested = _seededPenIds.Value.Take(RequestedPenCount).Order().ToArray();

		Assert.True(requested.Length < _seededPenIds.Value.Count, "The subset must be a strict one.");

		var result = await ReadHistoryAsync(
			seededArchive.Database.ReaderConnectionString,
			window,
			requested,
			AggregationLayer.Raw);

		Assert.True(result.IsSuccess, ArchiveReadSupport.Describe(result));
		Assert.Equal(requested, result.Value.Select(envelope => envelope.PenId));
	}

	// Likewise the layer: a read that bound layer 0 whatever it was asked for would return the raw rows
	// here, and the minute layer holds at most four of them per pen per minute plus the break markers.
	[Fact]
	public async Task TheMinuteLayerReturnsFewerColumnsThanRawOverTheSameWindow()
	{
		postgresContainerFixture.RequireAvailable();

		var window = QuietWindow();

		var raw = await ReadHistoryAsync(seededArchive.Database.ReaderConnectionString, window);
		var minute = await ReadHistoryAsync(
			seededArchive.Database.ReaderConnectionString,
			window,
			_seededPenIds.Value,
			AggregationLayer.Minute);

		Assert.True(raw.IsSuccess, ArchiveReadSupport.Describe(raw));
		Assert.True(minute.IsSuccess, ArchiveReadSupport.Describe(minute));

		// Not merely non-empty: the layer has to hold the same pens, or "fewer" would be satisfied by a
		// read that found the wrong partition of the primary key.
		Assert.Equal(
			raw.Value.Select(envelope => envelope.PenId),
			minute.Value.Select(envelope => envelope.PenId));

		Assert.All(
			minute.Value.Zip(raw.Value),
			pair => Assert.True(
				pair.First.Timestamps.Count < pair.Second.Timestamps.Count,
				$"Pen {pair.First.PenId} returns {pair.First.Timestamps.Count} columns from the minute "
					+ $"layer and {pair.Second.Timestamps.Count} from raw, so the layer parameter is not "
					+ "reaching the statement."));
	}

	[Fact]
	public async Task AWindowBeforeTheArchiveStartsIsASuccessfulEmptyList()
	{
		postgresContainerFixture.RequireAvailable();

		var window = new LocalWindow(
			ArchiveTemplate.Slice.Start - TimeSpan.FromHours(1),
			ArchiveTemplate.Slice.Start);

		var result = await ReadHistoryAsync(seededArchive.Database.ReaderConnectionString, window);

		// The seeder writes its first row at Start and the statement's upper bound is exclusive, so the
		// window really does end before the archive begins.
		Assert.Empty(SeededRowsIn(window));

		Assert.True(result.IsSuccess, ArchiveReadSupport.Describe(result));
		Assert.Empty(result.Value);
	}

	// A break writes no rows at all, and the fold turns the q = 32 row bounding it into one NaN anchor a
	// tick later. Counting the anchors rather than finding one is the whole instrument: a read that
	// anchored on every row absence, and one that anchored on the q = 16 resumption as well, both leave a
	// NaN in this window and only the count separates them from the right answer. This is the only place
	// the count runs against a real PostgreSQL rather than against rows handed to the fold directly.
	[Fact]
	public async Task AWindowStraddlingTheFirstBreakCarriesExactlyOneGapColumn()
	{
		postgresContainerFixture.RequireAvailable();

		var stopped = _breakPlan.Value.Breaks[0];
		var window = new LocalWindow(stopped.Start - _breakMargin, stopped.End + _breakMargin);

		var result = await ReadHistoryAsync(seededArchive.Database.ReaderConnectionString, window);

		Assert.True(result.IsSuccess, ArchiveReadSupport.Describe(result));

		var expected = SeededRowsIn(window);

		Assert.Equal(expected.Keys, result.Value.Select(envelope => envelope.PenId));
		Assert.Equal(ArchiveTemplate.Slice.PenCount, result.Value.Count);

		foreach (var envelope in result.Value)
		{
			var rows = expected[envelope.PenId];

			// One column per row plus the anchor: this is what says the rows pass through the decimator
			// one column each, so the index arithmetic below reads real neighbours, and it is the counted
			// form of "exactly one column was added".
			Assert.Equal(rows.Count + 1, envelope.Timestamps.Count);

			var anchor = Assert.Single(GapColumnIndices(envelope));

			Assert.True(
				double.IsNaN(envelope.Min[anchor]) && double.IsNaN(envelope.Max[anchor]),
				$"Pen {envelope.PenId} carries a gap column at {anchor} in Center alone, so the break is "
					+ "not a break in every series the chart draws.");

			var marker = Assert.Single(rows, row => row.Quality == ArchiveRow.LastBeforeBreakQuality);
			var resumption = Assert.Single(rows, row => row.Quality == ArchiveRow.FirstAfterBreakQuality);

			// The q = 32 row's own value survives as a real column immediately before the anchor, which is
			// what a read replacing the marker's value with a null would lose.
			Assert.Equal(_timeConverter.ToUtc(marker.Timestamp), envelope.Timestamps[anchor - 1]);
			Assert.Equal(marker.Value, envelope.Center[anchor - 1]);
			Assert.Equal(envelope.Timestamps[anchor - 1].AddTicks(1), envelope.Timestamps[anchor]);

			// The line resumes on the q = 16 row, and Assert.Single above says nothing anchors after it.
			Assert.Equal(_timeConverter.ToUtc(resumption.Timestamp), envelope.Timestamps[anchor + 1]);
			Assert.Equal(resumption.Value, envelope.Center[anchor + 1]);
		}
	}

	// The counterpart, and the one that says an anchor is a marker's doing rather than an absence's. The
	// archive polls every 100 ms and writes only on change, so a two-minute window inside an archiving run
	// is mostly absence — around 1 200 polls against a few dozen rows. A read treating absence as a break
	// passes the straddling test and shreds this one.
	[Fact]
	public async Task AWindowInsideASteadyStretchCarriesNoGapColumn()
	{
		postgresContainerFixture.RequireAvailable();

		var run = _breakPlan.Value.Runs[0];
		var window = new LocalWindow(run.Start + _windowOffset, run.Start + _windowOffset + _quietWindowLength);

		Assert.True(
			window.To < run.End,
			$"The window has to close before the run's q = 32 marker at {run.End:O}, not at {window.To:O}.");

		var result = await ReadHistoryAsync(seededArchive.Database.ReaderConnectionString, window);

		AssertMatchesSeededRows(result, window);

		Assert.Equal(ArchiveTemplate.Slice.PenCount, result.Value.Count);

		var anchors = result.Value.Sum(envelope => GapColumnIndices(envelope).Count);

		Assert.True(anchors == 0, $"A steady stretch produced {anchors} gap columns across the eight pens.");
	}

	// Evidence 5. The window opens after every pen's last sample, so the window branch of the statement
	// returns nothing and only the seed branch answers. Before the seed branch this read was a successful
	// empty list, and the consumer side dropped every pen from the chart — correct for a pen with no data,
	// wrong for a pen with data the window simply does not reach.
	[Fact]
	public async Task AWindowOpeningAfterEveryPensLastSampleStillReturnsThePens()
	{
		postgresContainerFixture.RequireAvailable();

		var opensAt = ArchiveTemplate.Slice.End + _windowOffset;
		var window = new LocalWindow(opensAt, opensAt + _quietWindowLength);

		// The seeder's last row sits before Slice.End, so the window really does open after every pen has
		// stopped writing, and one minute past the end is well inside the statement's look-back floor.
		Assert.DoesNotContain(_seededRawRows.Value, row => row.Timestamp >= window.From);

		var result = await ReadHistoryAsync(seededArchive.Database.ReaderConnectionString, window);

		AssertMatchesSeededRows(result, window);

		Assert.Equal(ArchiveTemplate.Slice.PenCount, result.Value.Count);

		// One column per pen, the seed row itself. The final archiving run is followed by no break, so its
		// last row carries no marker and nothing anchors after it.
		Assert.All(result.Value, envelope => Assert.Single(envelope.Timestamps));
	}

	// The look-back scales with the window, and this is the read that needs it: a pen silent for longer
	// than one partition width — a recipe setpoint written once at process start is the case — seeds a
	// window wide enough to reach back to it instead of being dropped from the chart. Against a bound
	// fixed at the floor this window finds nothing on either branch and every pen vanishes.
	[Fact]
	public async Task AWindowWiderThanTheLookBackFloorSeeksBackAsFarAsItAsks()
	{
		postgresContainerFixture.RequireAvailable();

		var opensAt = ArchiveTemplate.Slice.End + _seedLookBackFloor;
		var window = new LocalWindow(opensAt, opensAt + _wideWindowLength);

		// Every pen fell silent more than the floor ago, so a look-back fixed at the floor returns no row.
		Assert.DoesNotContain(_seededRawRows.Value, row => row.Timestamp >= window.From - _seedLookBackFloor);

		var result = await ReadHistoryAsync(seededArchive.Database.ReaderConnectionString, window);

		AssertMatchesSeededRows(result, window);

		Assert.Equal(ArchiveTemplate.Slice.PenCount, result.Value.Count);

		// One column per pen, its seed and nothing else: the window itself holds no row at all.
		Assert.All(result.Value, envelope => Assert.Single(envelope.Timestamps));
	}

	// The one failure path the read owns: trends absent under a present catalogue, which is the state a
	// client meets between semibase create and the SCADA's first write.
	[Fact]
	public async Task AProvisionedButUnseededDatabaseFailsNamingTrends()
	{
		postgresContainerFixture.RequireAvailable();

		await using var database = await postgresContainerFixture.CreateEmptyDatabaseAsync(
			TestContext.Current.CancellationToken);

		var provisioned = await SemibaseProvisioner.CreateAsync(
			postgresContainerFixture.Server,
			database.Name,
			TestContext.Current.CancellationToken);

		Assert.True(provisioned.IsSuccess, string.Join("; ", provisioned.Errors.Select(error => error.Message)));

		var window = QuietWindow();

		var result = await ReadHistoryAsync(database.ReaderConnectionString, window);

		Assert.True(result.IsFailed);

		var error = Assert.Single(result.Errors.OfType<ArchiveNotInitialisedError>());

		Assert.Equal(ArchiveObject.Table, error.MissingObject);
		Assert.Equal("trends", error.Table);
		Assert.Equal(database.Name, error.Database);
	}

	// SeedBefore mirrors a bound that lives in SQL, and nothing but this test links the two. It opens no
	// connection, so it runs wherever the rest of the class skips.
	[Fact]
	public void TheExpectedSeedLookBackIsTheStatementsOwn()
	{
		Assert.Contains(SeedLookBackClause, ArchiveStatements.SparseHistoryWindow, StringComparison.Ordinal);

		Assert.Equal(_seedLookBackFloor, SeedLookBackFor(QuietWindow()));

		var wideWindow = new LocalWindow(
			ArchiveTemplate.Slice.Start,
			ArchiveTemplate.Slice.Start + _seedLookBackFloor + _quietWindowLength);

		Assert.Equal(wideWindow.To - wideWindow.From, SeedLookBackFor(wideWindow));
	}

	// The archive's first two minutes: inside the first archiving run, before any break.
	private static LocalWindow QuietWindow()
	{
		return new LocalWindow(ArchiveTemplate.Slice.Start, ArchiveTemplate.Slice.Start + _quietWindowLength);
	}

	private static Task<Result<IReadOnlyList<PenHistoryEnvelope>>> ReadHistoryAsync(
		string connectionString,
		LocalWindow window)
	{
		return ReadHistoryAsync(connectionString, window, _seededPenIds.Value, AggregationLayer.Raw);
	}

	private static async Task<Result<IReadOnlyList<PenHistoryEnvelope>>> ReadHistoryAsync(
		string connectionString,
		LocalWindow window,
		IReadOnlyList<long> penIds,
		AggregationLayer layer)
	{
		await using var services = ArchiveProviderFactory.Build(connectionString);

		// The bounds cross the boundary in UTC, so they go in as the instants the archive's own naive
		// values stand for; the provider converts them back on the way to the statement. Every window here
		// sits in January, where the source zone holds one offset, so that round trip is exact.
		return await services.GetRequiredService<IDataProvider>().QueryHistoryAsync(
			penIds,
			_timeConverter.ToUtc(window.From),
			_timeConverter.ToUtc(window.To),
			layer,
			TargetColumnCount);
	}

	private static void AssertMatchesSeededRows(Result<IReadOnlyList<PenHistoryEnvelope>> result, LocalWindow window)
	{
		Assert.True(result.IsSuccess, ArchiveReadSupport.Describe(result));

		var expected = SeededRowsIn(window);

		Assert.NotEmpty(expected);

		// The statement orders by id, and the fold keeps that order, so the envelopes arrive on ascending
		// pen identifiers — which is the order the expectation is built in.
		Assert.Equal(expected.Keys, result.Value.Select(envelope => envelope.PenId));

		foreach (var envelope in result.Value)
		{
			var columns = ExpectedColumns(expected[envelope.PenId]);

			Assert.True(
				columns.Count <= TargetColumnCount,
				$"Pen {envelope.PenId} carries {columns.Count} columns, over the "
					+ $"{TargetColumnCount}-column target.");

			Assert.Equal(columns.Select(column => column.Timestamp), envelope.Timestamps);
			Assert.Equal(columns.Select(column => column.Value), envelope.Min);
			Assert.Equal(columns.Select(column => column.Value), envelope.Max);
			Assert.Equal(columns.Select(column => column.Value), envelope.Center);
		}
	}

	// Below the target every row becomes one column whose min, max and centre are its own value, and a
	// q = 32 row is followed by the fold's NaN anchor one tick after its converted timestamp.
	private static IReadOnlyList<(DateTime Timestamp, double Value)> ExpectedColumns(IReadOnlyList<ArchiveRow> rows)
	{
		var columns = new List<(DateTime Timestamp, double Value)>(rows.Count);

		foreach (var row in rows)
		{
			var timestamp = _timeConverter.ToUtc(row.Timestamp);
			columns.Add((timestamp, row.Value));

			if (row.Quality == ArchiveRow.LastBeforeBreakQuality)
			{
				columns.Add((timestamp.AddTicks(1), double.NaN));
			}
		}

		return columns;
	}

	// Every column the fold's break anchor produces. MinMaxDecimator writes NaN into all three series of a
	// gap column, so one series carries the count and the straddling test checks the other two agree.
	private static IReadOnlyList<int> GapColumnIndices(PenHistoryEnvelope envelope)
	{
		return Enumerable.Range(0, envelope.Timestamps.Count)
			.Where(index => double.IsNaN(envelope.Center[index]))
			.ToArray();
	}

	// Each pen's rows inside the window, led by the row the statement's seed branch finds before it. A pen
	// carrying a seed and no window row is present here with the seed alone, which is the read's own
	// answer: the window opened after that pen's last sample and the pen still draws. A pen with neither
	// gets no entry and no envelope.
	private static SortedDictionary<long, IReadOnlyList<ArchiveRow>> SeededRowsIn(LocalWindow window)
	{
		var rowsByPen = new SortedDictionary<long, IReadOnlyList<ArchiveRow>>();

		foreach (var penId in _seededPenIds.Value)
		{
			var rows = SeedBefore(penId, window)
				.Concat(_seededRawRows.Value
					.Where(row => (long)row.Id == penId
						&& row.Timestamp >= window.From
						&& row.Timestamp < window.To)
					.OrderBy(row => row.Timestamp))
				.ToArray();

			if (rows.Length > 0)
			{
				rowsByPen.Add(penId, rows);
			}
		}

		return rowsByPen;
	}

	// The look-back the statement seeks back over: the requested window, or one partition width when the
	// window is narrower than that.
	private static TimeSpan SeedLookBackFor(LocalWindow window)
	{
		var windowLength = window.To - window.From;

		return windowLength > _seedLookBackFloor ? windowLength : _seedLookBackFloor;
	}

	// The row the statement's seed branch returns: this pen's last row strictly before the window, no
	// further back than the look-back the statement carries.
	private static IEnumerable<ArchiveRow> SeedBefore(long penId, LocalWindow window)
	{
		var seed = _seededRawRows.Value
			.Where(row => (long)row.Id == penId
				&& row.Timestamp < window.From
				&& row.Timestamp >= window.From - SeedLookBackFor(window))
			.OrderByDescending(row => row.Timestamp)
			.FirstOrDefault();

		return seed is null ? [] : [seed];
	}

	private static IReadOnlyList<ArchiveRow> GenerateSeededRawRows()
	{
		return RawLayerGenerator.Generate(ArchiveTemplate.Slice)
			.Where(row => row.Layer == ArchiveRow.RawLayer)
			.ToArray();
	}

	private static IReadOnlyList<long> SelectSeededPenIds()
	{
		return RawLayerGenerator.SelectPens(ArchiveTemplate.Slice.PenCount)
			.Select(pen => pen.PenId)
			.ToArray();
	}

	// The archive's naive local wall clock, the vocabulary the seeder writes in.
	private readonly record struct LocalWindow(DateTime From, DateTime To);
}
