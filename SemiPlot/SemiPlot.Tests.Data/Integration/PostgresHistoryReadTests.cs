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

	// Opens on the archive's first instant, where every pen carries its run-start row, and closes well
	// inside the first archiving run — BreakPlan.MinimumRun keeps that run at least five minutes long.
	private static readonly TimeSpan _quietWindowLength = TimeSpan.FromMinutes(2);

	// Archiving of at least this length surrounds every break, so a margin of one MinimumRun on each side
	// of the first break stays inside the archive and inside the runs that bound it.
	private static readonly TimeSpan _breakMargin = BreakPlan.MinimumRun;

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

	// A break writes no rows at all, so the envelope steps from the last row before the stop to the first
	// one after it. AssertMatchesSeededRows pins the whole series against the seeder's own rows, which is
	// what says there is no interior sample — a separate "nothing inside the break" assertion would only
	// re-state a property of the seeder.
	[Fact]
	public async Task AWindowStraddlingTheFirstBreakCarriesNoColumnInsideIt()
	{
		postgresContainerFixture.RequireAvailable();

		var stopped = _breakPlan.Value.Breaks[0];
		var window = new LocalWindow(stopped.Start - _breakMargin, stopped.End + _breakMargin);

		var result = await ReadHistoryAsync(seededArchive.Database.ReaderConnectionString, window);

		AssertMatchesSeededRows(result, window);
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

		Assert.Equal("trends", error.Table);
		Assert.Equal(database.Name, error.Database);
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
			var rows = expected[envelope.PenId];

			Assert.True(
				rows.Count <= TargetColumnCount,
				$"Pen {envelope.PenId} carries {rows.Count} rows, over the {TargetColumnCount}-column target.");

			// Below the target every row becomes one column whose min, max and centre are its own value.
			Assert.Equal(rows.Select(row => _timeConverter.ToUtc(row.Timestamp)), envelope.Timestamps);
			Assert.Equal(rows.Select(row => row.Value), envelope.Min);
			Assert.Equal(rows.Select(row => row.Value), envelope.Max);
			Assert.Equal(rows.Select(row => row.Value), envelope.Center);
		}
	}

	private static SortedDictionary<long, IReadOnlyList<ArchiveRow>> SeededRowsIn(LocalWindow window)
	{
		var rowsByPen = new SortedDictionary<long, IReadOnlyList<ArchiveRow>>();

		var selected = _seededRawRows.Value
			.Where(row => row.Timestamp >= window.From && row.Timestamp < window.To)
			.GroupBy(row => (long)row.Id);

		foreach (var pen in selected)
		{
			rowsByPen.Add(pen.Key, pen.OrderBy(row => row.Timestamp).ToArray());
		}

		return rowsByPen;
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
