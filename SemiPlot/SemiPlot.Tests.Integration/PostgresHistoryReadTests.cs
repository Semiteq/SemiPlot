using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tools.ArchiveSeeder;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Integration;

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
	// The floor under every column target here: above the row count of every window this class reads, so a
	// coarse read passes each row through one column of its own.
	private const int TargetColumnCount = 4096;

	// A Raw read reduces by time, so a row keeps a column of its own only while the bucket is no wider than
	// this: RawLayerGenerator writes an anchor one PollInterval before every change, and the row-for-row
	// comparisons below would otherwise compare buckets. ColumnTargetFor sizes the target to hold it.
	private static readonly TimeSpan _tightestRowSpacing = RawLayerGenerator.PollInterval;

	// Denser than the canvas: over the window below the archive holds some 360 changes per pen, so the
	// server reduces rather than passes through.
	private const int BucketedColumnTarget = 64;

	// Wide enough that the target above reduces, narrow enough to sit inside one archiving run.
	private static readonly TimeSpan _denseWindowLength = TimeSpan.FromMinutes(30);

	// One minute of run on each side of that window, so it opens and closes clear of the markers.
	private static readonly TimeSpan _denseWindowMargin = TimeSpan.FromMinutes(1);

	// A strict subset of the seeded pens, so a read that ignored the pen list would fail.
	private const int RequestedPenCount = 3;

	// Inside one archiving run: BreakPlan.MinimumRun is five minutes.
	private static readonly TimeSpan _quietWindowLength = TimeSpan.FromMinutes(2);

	// Archiving of at least this length surrounds every break, so a margin of one MinimumRun on each side
	// of the first break stays inside the archive and inside the runs that bound it.
	private static readonly TimeSpan _breakMargin = BreakPlan.MinimumRun;

	// How far into the first archiving run the steady window opens, and how far past the archive's end the
	// seed-only window opens. Both are well inside the statement's look-back floor of one partition width.
	private static readonly TimeSpan _windowOffset = TimeSpan.FromMinutes(1);

	// The floor of the bound ArchiveStatements.SparseHistoryWindow puts on its backwards seek: one
	// partition width, widened to the requested window when that is wider.
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

	private static readonly Lazy<IReadOnlyList<int>> _seededPenIds = new(SelectSeededPenIds);

	// The fresh tail's own archive, written by this class into a clone of the provisioned source. One
	// calendar day, so the write creates the single partition tp2026m01d01; winter under the source zone,
	// so every conversion out is an unambiguous fixed offset.
	private static readonly DateTime _tailDay = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

	private static readonly DateTime _tailWindowFrom = _tailDay.AddHours(10);

	private static readonly DateTime _tailWindowTo = _tailWindowFrom.AddMinutes(5);

	// The raw layer's cadence in that archive. Ten seconds is well under Minute's 15 s point spacing, so
	// the raw layer really does carry rows the coarse layer cannot.
	private static readonly TimeSpan _rawStep = TimeSpan.FromSeconds(10);

	// The newest raw row, ten seconds before the window closes: what a Minute-layer window has to reach
	// once the tail fills it.
	private static readonly DateTime _newestRawTimestamp = _tailWindowTo - _rawStep;

	// Its coarse rows end exactly at the clamped tail start, so it clears the bound and takes the tail.
	private const int FreshPenId = 1;

	// Its coarse rows stop three minutes before that bound, so it takes no tail row at all.
	private const int LaggingPenId = 2;

	// Its coarse layer already carries the newest raw row's own timestamp, so every tail row it is offered
	// is one the fold has to drop.
	private const int ReachingPenId = 3;

	private static readonly DateTime _freshSeam = _tailWindowTo.AddMinutes(-1);

	private static readonly DateTime _laggingSeam = _tailWindowFrom.AddMinutes(1);

	private static readonly DateTime[] _freshCoarse =
	[
		_tailWindowFrom,
		_tailWindowFrom.AddMinutes(1),
		_tailWindowFrom.AddMinutes(2),
		_tailWindowFrom.AddMinutes(3),
		_freshSeam
	];

	private static readonly DateTime[] _laggingCoarse = [_tailWindowFrom, _laggingSeam];

	private static readonly DateTime[] _reachingCoarse = [.. _freshCoarse, _newestRawTimestamp];

	// The archive's own code for the layer the coarse rows below belong to.
	private const short MinuteLayer = (short)AggregationLayer.Minute;

	[Fact]
	public async Task AWindowInsideTheFirstRunReadsTheSeedersOwnRows()
	{
		var window = QuietWindow();

		var result = await ReadHistoryAsync(seededArchive.Database.ReaderConnectionString, window);

		AssertMatchesSeededRows(result, window);

		// The window opens on the instant every run starts, so every seeded pen has rows in it.
		result.Value.Count.Should().Be(ArchiveTemplate.Slice.PenCount);
	}

	[Fact]
	public async Task OnlyTheRequestedPensComeBack()
	{
		var window = QuietWindow();
		// SelectPens walks the catalogue's groups round-robin, so its first three are a strict subset in no
		// particular order; the statement orders by id, so the expectation is sorted to match.
		var requested = _seededPenIds.Value.Take(RequestedPenCount).Order().ToArray();

		(requested.Length < _seededPenIds.Value.Count).Should().BeTrue("The subset must be a strict one.");

		var result = await ReadHistoryAsync(
			seededArchive.Database.ReaderConnectionString,
			window,
			requested,
			AggregationLayer.Raw);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Select(envelope => envelope.PenId).Should().Equal(requested);
	}

	// The minute layer holds at most four rows per pen per minute plus the break markers.
	[Fact]
	public async Task TheMinuteLayerReturnsFewerColumnsThanRawOverTheSameWindow()
	{
		var window = QuietWindow();

		var raw = await ReadHistoryAsync(seededArchive.Database.ReaderConnectionString, window);
		var minute = await ReadHistoryAsync(
			seededArchive.Database.ReaderConnectionString,
			window,
			_seededPenIds.Value,
			AggregationLayer.Minute);

		raw.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(raw));
		minute.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(minute));

		// Not merely non-empty: the layer has to hold the same pens, or "fewer" would be satisfied by a
		// read that found the wrong partition of the primary key.
		minute.Value.Select(envelope => envelope.PenId).Should().Equal(raw.Value.Select(envelope => envelope.PenId));

		minute.Value.Zip(raw.Value).Should().AllSatisfy(
			pair => (pair.First.Timestamps.Count < pair.Second.Timestamps.Count).Should().BeTrue(
				$"Pen {pair.First.PenId} returns {pair.First.Timestamps.Count} columns from the minute "
					+ $"layer and {pair.Second.Timestamps.Count} from raw, so the layer parameter is not "
					+ "reaching the statement."));
	}

	[Fact]
	public async Task AWindowBeforeTheArchiveStartsIsASuccessfulEmptyList()
	{
		var window = new LocalWindow(
			ArchiveTemplate.Slice.Start - TimeSpan.FromHours(1),
			ArchiveTemplate.Slice.Start);

		var result = await ReadHistoryAsync(seededArchive.Database.ReaderConnectionString, window);

		// The seeder writes its first row at Start and the statement's upper bound is exclusive, so the
		// window really does end before the archive begins.
		SeededRowsIn(window).Should().BeEmpty();

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Should().BeEmpty();
	}

	// A break writes no rows at all, and the fold turns the q = 32 row bounding it into one NaN anchor a
	// tick later. Counting the anchors rather than finding one is the whole instrument.
	[Fact]
	public async Task AWindowStraddlingTheFirstBreakCarriesExactlyOneGapColumn()
	{
		var stopped = _breakPlan.Value.Breaks[0];
		var window = new LocalWindow(stopped.Start - _breakMargin, stopped.End + _breakMargin);

		var result = await ReadHistoryAsync(seededArchive.Database.ReaderConnectionString, window);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		var expected = SeededRowsIn(window);

		result.Value.Select(envelope => envelope.PenId).Should().Equal(expected.Keys);
		result.Value.Count.Should().Be(ArchiveTemplate.Slice.PenCount);

		foreach (var envelope in result.Value)
		{
			var rows = expected[envelope.PenId];

			// One column per row plus the anchor.
			envelope.Timestamps.Count.Should().Be(rows.Count + 1);

			var anchor = GapColumnIndices(envelope).Should().ContainSingle().Which;

			(double.IsNaN(envelope.Min[anchor]) && double.IsNaN(envelope.Max[anchor])).Should().BeTrue(
				$"Pen {envelope.PenId} carries a gap column at {anchor} in Center alone, so the break is "
					+ "not a break in every series the chart draws.");

			var marker = rows.Should().ContainSingle(row => row.Quality == ArchiveRow.LastBeforeBreakQuality).Which;
			var resumption = rows.Should().ContainSingle(row => row.Quality == ArchiveRow.FirstAfterBreakQuality).Which;

			// The q = 32 row's own value survives as a real column immediately before the anchor, which is
			// what a read replacing the marker's value with a null would lose.
			envelope.Timestamps[anchor - 1].Should().Be(_timeConverter.ToUtc(marker.Timestamp));
			envelope.Center[anchor - 1].Should().Be(marker.Value);
			envelope.Timestamps[anchor].Should().Be(envelope.Timestamps[anchor - 1].AddTicks(1));

			// The line resumes on the q = 16 row, and ContainSingle above says nothing anchors after it.
			envelope.Timestamps[anchor + 1].Should().Be(_timeConverter.ToUtc(resumption.Timestamp));
			envelope.Center[anchor + 1].Should().Be(resumption.Value);
		}
	}

	// The counterpart, and the one that says an anchor is a marker's doing rather than an absence's. The
	// archive polls every 100 ms and writes only on change, so a two-minute window inside an archiving run
	// is mostly absence.
	[Fact]
	public async Task AWindowInsideASteadyStretchCarriesNoGapColumn()
	{
		var run = _breakPlan.Value.Runs[0];
		var window = new LocalWindow(run.Start + _windowOffset, run.Start + _windowOffset + _quietWindowLength);

		(window.To < run.End).Should().BeTrue(
			$"The window has to close before the run's q = 32 marker at {run.End:O}, not at {window.To:O}.");

		var result = await ReadHistoryAsync(seededArchive.Database.ReaderConnectionString, window);

		AssertMatchesSeededRows(result, window);

		result.Value.Count.Should().Be(ArchiveTemplate.Slice.PenCount);

		var anchors = result.Value.Sum(envelope => GapColumnIndices(envelope).Count);

		(anchors == 0).Should().BeTrue($"A steady stretch produced {anchors} gap columns across the eight pens.");
	}

	[Fact]
	public async Task ARawWindowDenserThanTheCanvasComesBackBucketed()
	{
		var window = DenseWindow();
		var expected = SeededRowsIn(window);

		expected.Should().AllSatisfy(pen => (pen.Value.Count > BucketedColumnTarget).Should().BeTrue(
			$"Pen {pen.Key} holds {pen.Value.Count} rows in the window, which a target of "
				+ $"{BucketedColumnTarget} columns would pass through instead of reducing."));

		var result = await ReadHistoryAsync(
			seededArchive.Database.ReaderConnectionString,
			window,
			_seededPenIds.Value,
			AggregationLayer.Raw,
			BucketedColumnTarget);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Select(envelope => envelope.PenId).Should().Equal(expected.Keys);

		foreach (var envelope in result.Value)
		{
			var rows = expected[envelope.PenId];
			var markers = rows.Count(row => row.Quality == ArchiveRow.LastBeforeBreakQuality);

			// One column per bucket, one gap anchor per marker, and the seed's own column.
			(envelope.Timestamps.Count <= BucketedColumnTarget + markers + 1).Should().BeTrue(
				$"Pen {envelope.PenId} comes back with {envelope.Timestamps.Count} columns over "
					+ $"{rows.Count} rows, so the window was not reduced to the {BucketedColumnTarget}-column "
					+ "target the request carried.");

			envelope.Timestamps.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();

			AssertEachColumnBracketsItsCenter(envelope);

			// Which sample stands for a bucket: the newest one's timestamp and value, and the extremes of
			// the whole bucket. min(t) in place of max(t) passes every other fact in this class.
			AssertMatchesExpectedBuckets(envelope, rows, window, BucketedColumnTarget);
		}

		await ReportServerTimeAsync(window, BucketedColumnTarget);
	}

	// The widest Raw read the prefetch margin can ask for: a query covers three visible windows, so the
	// ceiling is the whole seeded archive at three times the canvas maximum.
	[Fact]
	public async Task TheWidestRawWindowThePrefetchMarginAsksForComesBackBucketed()
	{
		var window = new LocalWindow(ArchiveTemplate.Slice.Start, ArchiveTemplate.Slice.End);
		var target = HistoryColumnTarget.MaxColumns * HistoryPrefetch.MarginColumnFactor;
		var expected = SeededRowsIn(window);

		var result = await ReadHistoryAsync(
			seededArchive.Database.ReaderConnectionString,
			window,
			_seededPenIds.Value,
			AggregationLayer.Raw,
			target);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Select(envelope => envelope.PenId).Should().Equal(expected.Keys);

		foreach (var envelope in result.Value)
		{
			var rows = expected[envelope.PenId];
			var markers = rows.Count(row => row.Quality == ArchiveRow.LastBeforeBreakQuality);

			(envelope.Timestamps.Count <= target + markers + 1).Should().BeTrue(
				$"Pen {envelope.PenId} comes back with {envelope.Timestamps.Count} columns over "
					+ $"{rows.Count} rows, so the archive's widest window was not reduced to the "
					+ $"{target}-column target the request carried.");
		}

		await ReportServerTimeAsync(window, target);
	}

	// The counterpart, and the premise every row-for-row fact in this class rests on.
	[Fact]
	public async Task ARawWindowSparserThanTheCanvasComesBackRowForRow()
	{
		var window = QuietWindow();

		ColumnTargetFor(window).Should().Be(TargetColumnCount);

		(BucketFor(window) <= _tightestRowSpacing).Should().BeTrue(
			$"A bucket of {BucketFor(window)} merges the anchor and change rows the seeder writes "
				+ $"{_tightestRowSpacing} apart, so the comparison below would compare buckets, not rows.");

		var result = await ReadHistoryAsync(seededArchive.Database.ReaderConnectionString, window);

		AssertMatchesSeededRows(result, window);
	}

	// The anti-straddle rule, at a target whose bucket spans the whole break: without the segment in the
	// GROUP BY the marker and the rows after the break fold into one column and the marker's own sample is
	// lost, which no row-for-row fact in this class would notice.
	[Fact]
	public async Task ABucketWideEnoughToSpanABreakStillSplitsAtTheMarker()
	{
		var stopped = _breakPlan.Value.Breaks[0];
		var window = new LocalWindow(stopped.Start - _breakMargin, stopped.End + _breakMargin);
		var target = StraddlingColumnTargetFor(window, stopped.End - stopped.Start);

		(BucketFor(window, target) > stopped.End - stopped.Start).Should().BeTrue(
			$"A bucket of {BucketFor(window, target)} is narrower than the {stopped.End - stopped.Start} "
				+ "break, so date_bin alone would keep the marker apart from what follows it.");

		var result = await ReadHistoryAsync(
			seededArchive.Database.ReaderConnectionString,
			window,
			_seededPenIds.Value,
			AggregationLayer.Raw,
			target);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		var expected = SeededRowsIn(window);

		result.Value.Select(envelope => envelope.PenId).Should().Equal(expected.Keys);

		foreach (var envelope in result.Value)
		{
			var rows = expected[envelope.PenId];
			var marker = rows.Should().ContainSingle(row => row.Quality == ArchiveRow.LastBeforeBreakQuality).Which;
			var anchor = GapColumnIndices(envelope).Should().ContainSingle().Which;

			envelope.Timestamps[anchor - 1].Should().Be(_timeConverter.ToUtc(marker.Timestamp));
			envelope.Center[anchor - 1].Should().Be(marker.Value);
			envelope.Timestamps[anchor].Should().Be(envelope.Timestamps[anchor - 1].AddTicks(1));

			AssertMatchesExpectedBuckets(envelope, rows, window, target);
		}
	}

	// Only the seed branch answers here.
	[Fact]
	public async Task AWindowOpeningAfterEveryPensLastSampleStillReturnsThePens()
	{
		var opensAt = ArchiveTemplate.Slice.End + _windowOffset;
		var window = new LocalWindow(opensAt, opensAt + _quietWindowLength);

		// The seeder's last row sits before Slice.End, so the window really does open after every pen has
		// stopped writing, and one minute past the end is well inside the statement's look-back floor.
		_seededRawRows.Value.Should().NotContain(row => row.Timestamp >= window.From);

		var result = await ReadHistoryAsync(seededArchive.Database.ReaderConnectionString, window);

		AssertMatchesSeededRows(result, window);

		result.Value.Count.Should().Be(ArchiveTemplate.Slice.PenCount);

		// One column per pen, the seed row itself. The final archiving run is followed by no break, so its
		// last row carries no marker and nothing anchors after it.
		result.Value.Should().AllSatisfy(envelope => envelope.Timestamps.Should().ContainSingle());
	}

	// The look-back scales with the window, and this is the read that needs it: a pen silent for longer
	// than one partition width — a recipe setpoint written once at process start is the case — seeds a
	// window wide enough to reach back to it instead of being dropped from the chart.
	[Fact]
	public async Task AWindowWiderThanTheLookBackFloorSeeksBackAsFarAsItAsks()
	{
		var opensAt = ArchiveTemplate.Slice.End + _seedLookBackFloor;
		var window = new LocalWindow(opensAt, opensAt + _wideWindowLength);

		// Every pen fell silent more than the floor ago, so a look-back fixed at the floor returns no row.
		_seededRawRows.Value.Should().NotContain(row => row.Timestamp >= window.From - _seedLookBackFloor);

		var result = await ReadHistoryAsync(seededArchive.Database.ReaderConnectionString, window);

		AssertMatchesSeededRows(result, window);

		result.Value.Count.Should().Be(ArchiveTemplate.Slice.PenCount);

		// One column per pen, its seed and nothing else: the window itself holds no row at all.
		result.Value.Should().AllSatisfy(envelope => envelope.Timestamps.Should().ContainSingle());
	}

	// The one failure path the read owns: trends absent under a present catalogue. Provisioning creates
	// both, so the state is forced by dropping the table from a clone of the provisioned source.
	[Fact]
	public async Task ADroppedTrendsTableFailsNamingTrends()
	{
		await using var database = await postgresContainerFixture.CloneProvisionedAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.WriterConnectionString,
			ArchiveReadSupport.DropTrendsCommand,
			TestContext.Current.CancellationToken);

		var window = QuietWindow();

		var result = await ReadHistoryAsync(database.ReaderConnectionString, window);

		result.IsFailed.Should().BeTrue();

		var error = result.Errors.OfType<ArchiveError>().Should().ContainSingle().Which;

		error.Kind.Should().Be(ArchiveFault.TableMissing);
		error.Detail.Should().Be("trends");
		error.Database.Should().Be(database.Name);
	}

	// Minute's point spacing is 15 s and its period four of those, so over the five-minute window below the
	// clamp lands the tail start exactly one minute before the window closes.
	[Fact]
	public async Task AMinuteWindowPastTheCoarseLayersNewestRowReachesTheRawLayersNewest()
	{
		await using var database = await WriteTailArchiveAsync(TestContext.Current.CancellationToken);

		var result = await ReadTailWindowAsync(database, AggregationLayer.Minute);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		var envelope = result.Value.Should().ContainSingle(candidate => candidate.PenId == FreshPenId).Which;

		// The coarse rows, then every raw row after the pen's own seam. Without the tail the series would
		// stop at 10:04:00 and the operator would read a value fifty seconds old as the current one.
		envelope.Timestamps.Should().Equal(
			ExpectedUtc(_freshCoarse.Concat(RawTimestamps().Where(timestamp => timestamp > _freshSeam))));

		envelope.Timestamps[^1].Should().Be(_timeConverter.ToUtc(_newestRawTimestamp));
	}

	[Fact]
	public async Task TheSameWindowAtTheRawLayerIsUnchangedByTheTail()
	{
		await using var database = await WriteTailArchiveAsync(TestContext.Current.CancellationToken);

		var result = await ReadTailWindowAsync(database, AggregationLayer.Raw);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		result.Value.Select(e => e.PenId).Should().Equal(FreshPenId, LaggingPenId, ReachingPenId);

		// Identical across all three pens, including the one whose coarse layer lags: at Raw the coarse
		// layer takes no part in the answer at all.
		result.Value.Should().AllSatisfy(envelope => envelope.Timestamps.Should().Equal(ExpectedUtc(RawTimestamps())));
	}

	// The tail rows overlap the coarse rows in time, and one of this pen's raw rows carries the very
	// timestamp its coarse layer already reached. The fold's ascending check is what drops it.
	[Fact]
	public async Task APenWhoseCoarseRowsReachTheWindowEndGainsNoDuplicate()
	{
		await using var database = await WriteTailArchiveAsync(TestContext.Current.CancellationToken);

		var result = await ReadTailWindowAsync(database, AggregationLayer.Minute);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		var envelope = result.Value.Should().ContainSingle(candidate => candidate.PenId == ReachingPenId).Which;

		envelope.Timestamps.Should().Equal(ExpectedUtc(_reachingCoarse));
		envelope.Timestamps.Should().Equal(envelope.Timestamps.Distinct());
	}

	// The lagging pen keeps its short right edge and gains no interpolated span.
	[Fact]
	public async Task APenWhoseCoarseRowsStopBeforeTheTailStartGainsNoRowAndNoInterpolatedSpan()
	{
		await using var database = await WriteTailArchiveAsync(TestContext.Current.CancellationToken);

		var result = await ReadTailWindowAsync(database, AggregationLayer.Minute);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		var envelope = result.Value.Should().ContainSingle(candidate => candidate.PenId == LaggingPenId).Which;

		envelope.Timestamps.Should().Equal(ExpectedUtc(_laggingCoarse));
		envelope.Timestamps[^1].Should().Be(_timeConverter.ToUtc(_laggingSeam));

		// Nothing lands after the seam, so there is no segment spanning the hole — and nothing anchors a
		// gap either, which is what would have made such a segment readable had one been drawn.
		GapColumnIndices(envelope).Should().BeEmpty();
	}

	// SeedBefore mirrors a bound that lives in SQL, and nothing but this test links the two. It opens no
	// connection, so it runs wherever the rest of the class skips.
	[Fact]
	public void TheExpectedSeedLookBackIsTheStatementsOwn()
	{
		ArchiveStatements.SparseHistoryWindow.Should().Contain(SeedLookBackClause);

		SeedLookBackFor(QuietWindow()).Should().Be(_seedLookBackFloor);

		var wideWindow = new LocalWindow(
			ArchiveTemplate.Slice.Start,
			ArchiveTemplate.Slice.Start + _seedLookBackFloor + _quietWindowLength);

		SeedLookBackFor(wideWindow).Should().Be(wideWindow.To - wideWindow.From);
	}

	// The archive's first two minutes: inside the first archiving run, before any break.
	private static LocalWindow QuietWindow()
	{
		return new LocalWindow(ArchiveTemplate.Slice.Start, ArchiveTemplate.Slice.Start + _quietWindowLength);
	}

	// Half an hour inside the longest archiving run, so the only reduction the read shows is the bucketing.
	private static LocalWindow DenseWindow()
	{
		var run = _breakPlan.Value.Runs.MaxBy(candidate => candidate.End - candidate.Start);
		var window = new LocalWindow(
			run.Start + _denseWindowMargin,
			run.Start + _denseWindowMargin + _denseWindowLength);

		(window.To + _denseWindowMargin < run.End).Should().BeTrue(
			$"The longest archiving run, {run.Start:O} to {run.End:O}, is too short to hold the window.");

		return window;
	}

	private static void AssertEachColumnBracketsItsCenter(PenHistoryEnvelope envelope)
	{
		for (var column = 0; column < envelope.Timestamps.Count; column++)
		{
			(envelope.Min[column] <= envelope.Center[column] && envelope.Center[column] <= envelope.Max[column])
				.Should().BeTrue(
					$"Pen {envelope.PenId} column {column} carries a center of {envelope.Center[column]} "
						+ $"outside [{envelope.Min[column]}, {envelope.Max[column]}], so the band does not "
						+ "hold the line it is drawn around.");
		}
	}

	// Recorded rather than asserted: a threshold on the server's own time would flake on a shared runner. The
	// whole plan is printed, not the timing lines alone, because the rows read and sorted ahead of the window
	// aggregate are the other half of what the read costs.
	private async Task ReportServerTimeAsync(LocalWindow window, int columnTarget)
	{
		await using var connection = new NpgsqlConnection(seededArchive.Database.ReaderConnectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		await using var command = new NpgsqlCommand(
			"EXPLAIN (ANALYZE, TIMING OFF) " + ArchiveStatements.BucketedRawWindow,
			connection);

		PostgresDataProvider.BindBucketedWindow(
			command,
			_timeConverter,
			[.. _seededPenIds.Value],
			_timeConverter.ToUtc(window.From),
			_timeConverter.ToUtc(window.To),
			columnTarget);

		await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{window.From:O} to {window.To:O}, {_seededPenIds.Value.Count} pens, {columnTarget} columns:");

		while (await reader.ReadAsync(TestContext.Current.CancellationToken))
		{
			TestContext.Current.TestOutputHelper?.WriteLine(reader.GetString(0).Trim());
		}
	}

	private static Task<Result<IReadOnlyList<PenHistoryEnvelope>>> ReadHistoryAsync(
		string connectionString,
		LocalWindow window)
	{
		return ReadHistoryAsync(connectionString, window, _seededPenIds.Value, AggregationLayer.Raw);
	}

	private static Task<Result<IReadOnlyList<PenHistoryEnvelope>>> ReadHistoryAsync(
		string connectionString,
		LocalWindow window,
		IReadOnlyList<int> penIds,
		AggregationLayer layer)
	{
		return ReadHistoryAsync(connectionString, window, penIds, layer, ColumnTargetFor(window));
	}

	private static async Task<Result<IReadOnlyList<PenHistoryEnvelope>>> ReadHistoryAsync(
		string connectionString,
		LocalWindow window,
		IReadOnlyList<int> penIds,
		AggregationLayer layer,
		int targetColumnCount)
	{
		await using var services = ArchiveProviderFactory.Build(connectionString);

		// January under the source zone: one offset, so the round trip is exact.
		return await services.GetRequiredService<IDataProvider>().QueryHistoryAsync(
			penIds,
			_timeConverter.ToUtc(window.From),
			_timeConverter.ToUtc(window.To),
			layer,
			targetColumnCount);
	}

	// The target that keeps a Raw bucket at or under the archive's tightest row spacing, never below the
	// floor a coarse read needs.
	private static int ColumnTargetFor(LocalWindow window)
	{
		return Math.Max(TargetColumnCount, (int)Math.Ceiling((window.To - window.From) / _tightestRowSpacing));
	}

	private static TimeSpan BucketFor(LocalWindow window)
	{
		return BucketFor(window, ColumnTargetFor(window));
	}

	private static TimeSpan BucketFor(LocalWindow window, int targetColumnCount)
	{
		return PostgresDataProvider.BucketFor(window.From, window.To, targetColumnCount);
	}

	// The coarsest target whose bucket still spans twice the break, so the marker and the rows after it
	// share a date_bin slot and only the segment separates them.
	private static int StraddlingColumnTargetFor(LocalWindow window, TimeSpan breakLength)
	{
		return Math.Max(1, (int)((window.To - window.From) / (breakLength + breakLength)));
	}

	private static void AssertMatchesExpectedBuckets(
		PenHistoryEnvelope envelope,
		IReadOnlyList<ArchiveRow> rows,
		LocalWindow window,
		int targetColumnCount)
	{
		var columns = ExpectedBuckets(rows, window, targetColumnCount);

		envelope.Timestamps.Should().Equal(columns.Select(column => column.Timestamp));
		envelope.Center.Should().Equal(columns.Select(column => column.Center));
		envelope.Min.Should().Equal(columns.Select(column => column.Min));
		envelope.Max.Should().Equal(columns.Select(column => column.Max));
	}

	private static IReadOnlyList<ExpectedBucket> ExpectedBuckets(
		IReadOnlyList<ArchiveRow> rows,
		LocalWindow window,
		int targetColumnCount)
	{
		var bucket = BucketFor(window, targetColumnCount);
		var columns = new List<ExpectedBucket>();
		var group = new List<ArchiveRow>();
		var groupKey = (Segment: -1, Bin: -1L);
		var segment = 0;

		foreach (var row in rows)
		{
			if (row.Timestamp < window.From)
			{
				AppendBucket(columns, [row]);

				continue;
			}

			var key = (segment, (row.Timestamp - window.From).Ticks / bucket.Ticks);

			if (group.Count > 0 && key != groupKey)
			{
				AppendBucket(columns, group);
				group = [];
			}

			groupKey = key;
			group.Add(row);

			if (row.Quality == ArchiveRow.LastBeforeBreakQuality)
			{
				segment++;
			}
		}

		if (group.Count > 0)
		{
			AppendBucket(columns, group);
		}

		return columns;
	}

	private static void AppendBucket(List<ExpectedBucket> columns, IReadOnlyList<ArchiveRow> group)
	{
		var timestamp = _timeConverter.ToUtc(group[^1].Timestamp);

		columns.Add(new ExpectedBucket(
			timestamp,
			group[^1].Value,
			group.Min(row => row.Value),
			group.Max(row => row.Value)));

		if (group.Any(row => row.Quality == ArchiveRow.LastBeforeBreakQuality))
		{
			columns.Add(new ExpectedBucket(timestamp.AddTicks(1), double.NaN, double.NaN, double.NaN));
		}
	}

	private static void AssertMatchesSeededRows(Result<IReadOnlyList<PenHistoryEnvelope>> result, LocalWindow window)
	{
		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		var expected = SeededRowsIn(window);

		expected.Should().NotBeEmpty();

		// The statement orders by id, and the fold keeps that order, so the envelopes arrive on ascending
		// pen identifiers — which is the order the expectation is built in.
		result.Value.Select(envelope => envelope.PenId).Should().Equal(expected.Keys);

		var target = ColumnTargetFor(window);

		foreach (var envelope in result.Value)
		{
			var columns = ExpectedColumns(expected[envelope.PenId]);

			(columns.Count <= target).Should().BeTrue(
				$"Pen {envelope.PenId} carries {columns.Count} columns, over the {target}-column target.");

			envelope.Timestamps.Should().Equal(columns.Select(column => column.Timestamp));
			envelope.Min.Should().Equal(columns.Select(column => column.Value));
			envelope.Max.Should().Equal(columns.Select(column => column.Value));
			envelope.Center.Should().Equal(columns.Select(column => column.Value));
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
		return [.. Enumerable.Range(0, envelope.Timestamps.Count).Where(index => double.IsNaN(envelope.Center[index]))];
	}

	// Each pen's rows inside the window, led by the row the statement's seed branch finds before it. A pen
	// carrying a seed and no window row is present here with the seed alone.
	private static SortedDictionary<int, IReadOnlyList<ArchiveRow>> SeededRowsIn(LocalWindow window)
	{
		var rowsByPen = new SortedDictionary<int, IReadOnlyList<ArchiveRow>>();

		foreach (var penId in _seededPenIds.Value)
		{
			var rows = SeedBefore(penId, window)
				.Concat(_seededRawRows.Value
					.Where(row => row.Id == penId
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
	private static IEnumerable<ArchiveRow> SeedBefore(int penId, LocalWindow window)
	{
		var seed = _seededRawRows.Value
			.Where(row => row.Id == penId
				&& row.Timestamp < window.From
				&& row.Timestamp >= window.From - SeedLookBackFor(window))
			.OrderByDescending(row => row.Timestamp)
			.FirstOrDefault();

		return seed is null ? [] : [seed];
	}

	// A clone of the provisioned source carrying nothing but the rows below. It is dropped by the caller's
	// `await using`, so a failed write disposes here rather than leaking a database.
	private async Task<ArchiveDatabase> WriteTailArchiveAsync(CancellationToken cancellationToken)
	{
		var database = await postgresContainerFixture.CloneProvisionedAsync(cancellationToken);

		try
		{
			await new ArchiveWriter(database.WriterConnectionString)
				.WriteAsync(
					TailArchiveRows(),
					_tailDay,
					_tailDay.AddDays(1),
					cancellationToken: cancellationToken);

		}
		catch
		{
			await database.DisposeAsync();

			throw;
		}

		return database;
	}

	private static Task<Result<IReadOnlyList<PenHistoryEnvelope>>> ReadTailWindowAsync(
		ArchiveDatabase database,
		AggregationLayer layer)
	{
		return ReadHistoryAsync(
			database.ReaderConnectionString,
			new LocalWindow(_tailWindowFrom, _tailWindowTo),
			[FreshPenId, LaggingPenId, ReachingPenId],
			layer);
	}

	// The raw layer for all three pens plus a coarse layer that ends at a different instant for each.
	private static IReadOnlyList<ArchiveRow> TailArchiveRows()
	{
		var rows = new List<ArchiveRow>();

		foreach (var penId in new[] { FreshPenId, LaggingPenId, ReachingPenId })
		{
			rows.AddRange(RawTimestamps().Select(timestamp => new ArchiveRow(
				penId,
				ArchiveRow.RawLayer,
				timestamp,
				penId,
				ArchiveRow.OrdinaryQuality)));
		}

		rows.AddRange(CoarseRows(FreshPenId, _freshCoarse));
		rows.AddRange(CoarseRows(LaggingPenId, _laggingCoarse));
		rows.AddRange(CoarseRows(ReachingPenId, _reachingCoarse));

		return rows;
	}

	private static IEnumerable<ArchiveRow> CoarseRows(int penId, IEnumerable<DateTime> timestamps)
	{
		return timestamps.Select(timestamp => new ArchiveRow(
			penId,
			MinuteLayer,
			timestamp,
			penId,
			ArchiveRow.OrdinaryQuality));
	}

	// Every raw timestamp inside the tail window. The first sits on the window's own start, so the
	// statement's seed branch finds nothing before it and the raw read is the window alone.
	private static IReadOnlyList<DateTime> RawTimestamps()
	{
		var timestamps = new List<DateTime>();

		for (var timestamp = _tailWindowFrom; timestamp < _tailWindowTo; timestamp += _rawStep)
		{
			timestamps.Add(timestamp);
		}

		return timestamps;
	}

	private static IReadOnlyList<DateTime> ExpectedUtc(IEnumerable<DateTime> archiveLocal)
	{
		return [.. archiveLocal.Select(_timeConverter.ToUtc)];
	}

	private static IReadOnlyList<ArchiveRow> GenerateSeededRawRows()
	{
		return [.. RawLayerGenerator.Generate(ArchiveTemplate.Slice).Where(row => row.Layer == ArchiveRow.RawLayer)];
	}

	private static IReadOnlyList<int> SelectSeededPenIds()
	{
		return [.. RawLayerGenerator.SelectPens(ArchiveTemplate.Slice.PenCount).Select(pen => pen.PenId)];
	}

	// The archive's naive local wall clock, the vocabulary the seeder writes in.
	private readonly record struct LocalWindow(DateTime From, DateTime To);

	// One column the bucketed statement is expected to return, in the chart's own UTC vocabulary.
	private readonly record struct ExpectedBucket(DateTime Timestamp, double Center, double Min, double Max);
}
