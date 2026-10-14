using System.Globalization;
using System.Reflection;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

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
	private const int ColumnTargetFloor = 4096;

	// Every read reduces by time, so a row keeps a column of its own only while the bucket is no wider than
	// this: RawLayerGenerator writes an anchor one PollInterval before every change, and the row-for-row
	// comparisons below would otherwise compare buckets. ColumnTargetFor sizes the target to hold it.
	private static readonly TimeSpan _tightestRowSpacing = RawLayerGenerator.PollInterval;

	// Denser than the canvas: over the window below the archive holds some 360 changes per pen, so the
	// server reduces rather than passes through.
	private const int BucketedColumnTarget = 64;

	private const int MinuteColumnTarget = 64;

	private const int HourColumnTarget = 8;

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

	// The floor of the bound ArchiveStatements.BucketedHistoryWindow puts on its backwards seek: one
	// partition width, widened to the requested window when that is wider.
	private static readonly TimeSpan _seedLookBackFloor = TimeSpan.FromDays(1);

	// Wider than that floor, so the bound the statement applies over this window is the window itself.
	private static readonly TimeSpan _wideWindowLength = TimeSpan.FromDays(3);

	// Lazy rather than a plain initialiser: regenerating the seeder's whole day of raw rows costs real time
	// and memory, and a run with no container runtime skips every test here before touching any of it.
	private static readonly Lazy<BreakPlan> _breakPlan = new(() => BreakPlan.Create(ArchiveTemplate.Slice));

	private static readonly Lazy<IReadOnlyList<ArchiveRow>> _seededRawRows = new(GenerateSeededRawRows);

	// The coarse layers ArchiveTemplate writes beside the raw rows, thinned once per run.
	private static readonly Lazy<IReadOnlyList<ArchiveRow>> _seededMinuteRows =
		new(() => LayerThinner.Thin(_seededRawRows.Value, LayerThinner.MinuteLayer));

	private static readonly Lazy<IReadOnlyList<ArchiveRow>> _seededHourRows =
		new(() => LayerThinner.Thin(_seededRawRows.Value, LayerThinner.HourLayer));

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

	// Its coarse rows end thirty seconds past the fresh seam: inside the clamp, yet more than one Minute point
	// before the window end, so a tail start taken from its own seam alone would differ from the fresh pen's.
	private const int LatePenId = 6;

	private static readonly DateTime[] _lateCoarse = [.. _freshCoarse, _freshSeam.AddSeconds(30)];

	// Its hour-layer rows end twenty minutes before the raw rows start.
	private const int HourTailPenId = 4;

	// Its minute layer matches the fresh pen's, and its raw tail carries a q = 32 marker and the q = 16 row
	// that resumes after it.
	private const int BreakTailPenId = 5;

	private static readonly DateTime _hourWindowFrom = _tailWindowFrom.AddHours(-2);

	private static readonly DateTime _hourSeam = _tailWindowFrom.AddMinutes(-20);

	private static readonly DateTime[] _hourCoarse = [_tailWindowFrom.AddMinutes(-100), _hourSeam];

	private static readonly DateTime _tailBreakMarker = _freshSeam.AddSeconds(20);

	// One raw step is missing between the two: the archive wrote nothing while the project stood.
	private static readonly DateTime _tailBreakResumption = _tailBreakMarker.AddSeconds(20);

	// Two columns over the five-minute tail window: a bucket of two and a half minutes, wider than the
	// one-minute tail the clamp allows, so the whole tail folds into one column.
	private const int WideTailColumnTarget = 2;

	// The archive's own codes for the layers the coarse rows below belong to.
	private const short MinuteLayer = (short)AggregationLayer.Minute;

	private const short HourLayer = (short)AggregationLayer.Hour;

	private const string LockTrendsCommand = "LOCK TABLE public.trends IN ACCESS EXCLUSIVE MODE;";

	private const string LockWaitOnTrendsQuery =
		"SELECT count(*) FROM pg_locks WHERE NOT granted AND relation = 'public.trends'::regclass;";

	// Far above the round trip a cancel request takes, so only a read the token never reached runs into it.
	private static readonly TimeSpan _cancelledReadDeadline = TimeSpan.FromSeconds(20);

	// The premise every row-for-row fact in this class rests on: a bucket no wider than the tightest row
	// spacing keeps one column per row. A steady stretch has no break, so no row carries a gap anchor.
	[Fact]
	public async Task AWindowInsideTheFirstRunReadsTheSeedersOwnRowsWithoutAGapColumn()
	{
		var window = QuietWindow();
		var run = _breakPlan.Value.Runs[0];

		(window.To < run.End).Should().BeTrue(
			$"The window has to close before the run's q = 32 marker at {run.End:O}, not at {window.To:O}.");

		ColumnTargetFor(window).Should().Be(ColumnTargetFloor);

		(BucketFor(window) <= _tightestRowSpacing).Should().BeTrue(
			$"A bucket of {BucketFor(window)} merges the anchor and change rows the seeder writes "
				+ $"{_tightestRowSpacing} apart, so the comparison below would compare buckets, not rows.");

		var result = await ReadHistoryAsync(seededArchive.Database.PlotConnectionString, window);

		AssertMatchesSeededRows(result, window);

		// The window opens on the instant every run starts, so every seeded pen has rows in it.
		result.Value.Count.Should().Be(ArchiveTemplate.Slice.PenCount);

		var anchors = result.Value.Sum(envelope => GapColumnIndices(envelope).Count);

		(anchors == 0).Should().BeTrue($"A steady stretch produced {anchors} gap columns across the eight pens.");
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
			seededArchive.Database.PlotConnectionString,
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

		var raw = await ReadHistoryAsync(seededArchive.Database.PlotConnectionString, window);
		var minute = await ReadHistoryAsync(
			seededArchive.Database.PlotConnectionString,
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

		var result = await ReadHistoryAsync(seededArchive.Database.PlotConnectionString, window);

		// The seeder writes its first row at Start and the statement's upper bound is exclusive, so the
		// window really does end before the archive begins.
		SeededRowsIn(window).Should().BeEmpty();

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Should().BeEmpty();
	}

	// The lock holds the statement on the server, so the cancel lands mid-statement and the server answers
	// 57014. Both layers reach the bucketed statement.
	[Theory]
	[InlineData(AggregationLayer.Raw)]
	[InlineData(AggregationLayer.Minute)]
	public async Task AReadCancelledMidStatementThrowsInsteadOfFailing(AggregationLayer layer)
	{
		var testCancellation = TestContext.Current.CancellationToken;
		await using var database = await postgresContainerFixture.CloneAsync(
			CloneSource.Provisioned, cancellationToken: testCancellation);
		await using var locker = new NpgsqlConnection(database.AdminConnectionString);
		await locker.OpenAsync(testCancellation);
		await using var transaction = await locker.BeginTransactionAsync(testCancellation);
		await using var lockTrends = new NpgsqlCommand(LockTrendsCommand, locker, transaction);
		await lockTrends.ExecuteNonQueryAsync(testCancellation);

		var window = QuietWindow();
		using var cancellation = new CancellationTokenSource();
		using var logs = new FakeLoggerProvider();
		await using var services = ArchiveProviderFactory.Build(database.PlotConnectionString, logs);

		var read = services.GetRequiredService<IDataProvider>().QueryHistoryAsync(
			_seededPenIds.Value,
			ArchiveProviderFactory.TimeConverter.ToUtc(window.From),
			ArchiveProviderFactory.TimeConverter.ToUtc(window.To),
			layer,
			ColumnTargetFor(window),
			cancellation.Token);

		try
		{
			(await AwaitALockWaitOnTrendsAsync(locker, transaction)).Should().BeTrue(
				"the read has to be waiting on the server when the token is cancelled");

			await cancellation.CancelAsync();

			var settled = () => read.WaitAsync(_cancelledReadDeadline, testCancellation);

			await settled.Should().ThrowAsync<OperationCanceledException>();

			HistoryReadEntries(logs).Should().ContainSingle()
				.Which.GetStructuredStateValue("Outcome").Should().Be("Cancelled");
		}
		finally
		{
			await transaction.RollbackAsync(testCancellation);
		}
	}

	// A break writes no rows at all, and the fold turns the q = 32 row bounding it into one NaN anchor a
	// tick later. Counting the anchors rather than finding one is the whole instrument.
	[Fact]
	public async Task AWindowStraddlingTheFirstBreakCarriesExactlyOneGapColumn()
	{
		var stopped = _breakPlan.Value.Breaks[0];
		var window = new LocalWindow(stopped.Start - _breakMargin, stopped.End + _breakMargin);

		foreach (var penId in _seededPenIds.Value)
		{
			var inWindow = _seededRawRows.Value
				.Where(row => row.Id == penId && row.Timestamp >= window.From && row.Timestamp < window.To)
				.ToArray();

			inWindow.Count(row => row.Quality == ArchiveRow.LastBeforeBreakQuality).Should().Be(1);
			inWindow.Count(row => row.Quality == ArchiveRow.FirstAfterBreakQuality).Should().Be(1);
		}

		var result = await ReadHistoryAsync(seededArchive.Database.PlotConnectionString, window);

		// Row for row, including the marker's own value, the NaN anchor one tick after it and the resumption.
		AssertMatchesSeededRows(result, window);

		result.Value.Count.Should().Be(ArchiveTemplate.Slice.PenCount);

		result.Value.Should().AllSatisfy(envelope => GapColumnIndices(envelope).Should().ContainSingle());
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
			seededArchive.Database.PlotConnectionString,
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
	}

	// The coarse layers fold in the same statement: per bucket the newest sample and the extremes of the
	// layer's rows, and one gap column per q = 32 row the thinning copied into the layer.
	[Theory]
	[InlineData(AggregationLayer.Minute, MinuteColumnTarget)]
	[InlineData(AggregationLayer.Hour, HourColumnTarget)]
	public async Task ACoarseWindowDenserThanTheCanvasComesBackBucketed(AggregationLayer layer, int target)
	{
		var window = new LocalWindow(ArchiveTemplate.Slice.Start, ArchiveTemplate.Slice.End);
		var expected = SeededRowsIn(window, layer);

		expected.Should().AllSatisfy(pen => (pen.Value.Count > target).Should().BeTrue(
			$"Pen {pen.Key} holds {pen.Value.Count} rows at {layer} in the window, which a target of "
				+ $"{target} columns would pass through instead of reducing."));

		// The clock runs a day past the window, so the window end is the tail edge, and every pen's newest
		// layer row sits within one point of it: no fresh tail runs, and the buckets are the whole answer.
		expected.Should().AllSatisfy(pen => (window.To - pen.Value[^1].Timestamp <= layer.ToPointSpacing())
			.Should().BeTrue(
				$"Pen {pen.Key}'s newest {layer} row, {pen.Value[^1].Timestamp:O}, is more than one point "
					+ $"before the window end {window.To:O}, so a fresh tail would add columns."));

		var result = await ReadHistoryAsync(
			seededArchive.Database.PlotConnectionString,
			window,
			_seededPenIds.Value,
			layer,
			target,
			ClockAt(window.To.AddDays(1)));

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Select(envelope => envelope.PenId).Should().Equal(expected.Keys);

		foreach (var envelope in result.Value)
		{
			var rows = expected[envelope.PenId];
			var markers = rows.Count(row => row.Quality == ArchiveRow.LastBeforeBreakQuality);

			// Without a marker the gap column count and the bound below would hold as zero against zero.
			markers.Should().BePositive();

			// One column per bucket, one more where a marker splits its bucket, one gap column per marker, and
			// the seed's own column.
			(envelope.Timestamps.Count <= target + (2 * markers) + 1).Should().BeTrue(
				$"Pen {envelope.PenId} comes back with {envelope.Timestamps.Count} columns over "
					+ $"{rows.Count} rows at {layer}, so the window was not reduced to the {target}-column "
					+ "target the request carried.");

			envelope.Timestamps.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();

			AssertEachColumnBracketsItsCenter(envelope);
			AssertMatchesExpectedBuckets(envelope, rows, window, target);

			GapColumnIndices(envelope).Count.Should().Be(markers);
		}
	}

	// The widest Raw read the prefetch margin can ask for: a query covers three visible windows, so the
	// ceiling is the whole seeded archive at three times the canvas maximum.
	[Fact]
	public async Task TheWidestRawWindowThePrefetchMarginAsksForComesBackBucketed()
	{
		var window = new LocalWindow(ArchiveTemplate.Slice.Start, ArchiveTemplate.Slice.End);
		var target = 3 * HistoryColumnTarget.MaxColumns;
		var expected = SeededRowsIn(window);

		var result = await ReadHistoryAsync(
			seededArchive.Database.PlotConnectionString,
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

			(envelope.Timestamps.Count <= target + (2 * markers) + 1).Should().BeTrue(
				$"Pen {envelope.PenId} comes back with {envelope.Timestamps.Count} columns over "
					+ $"{rows.Count} rows, so the archive's widest window was not reduced to the "
					+ $"{target}-column target the request carried.");
		}

		await ReportServerTimeAsync(window, target);
	}

	// The anti-straddle rule, at a target whose bucket spans the whole break: without the segment in the
	// GROUP BY the marker and the rows after the break fold into one column and the marker's own sample is
	// lost, which no row-for-row fact in this class would notice. The coarse layers carry the markers too.
	[Theory]
	[InlineData(AggregationLayer.Raw)]
	[InlineData(AggregationLayer.Minute)]
	public async Task ABucketWideEnoughToSpanABreakStillSplitsAtTheMarker(AggregationLayer layer)
	{
		var stopped = _breakPlan.Value.Breaks[0];

		// Closed on a minute boundary, so the minute layer holds the newest raw row before it and no fresh
		// tail adds a column the expectation below does not model.
		var window = new LocalWindow(
			stopped.Start - _breakMargin,
			LayerThinner.PeriodStart(stopped.End + _breakMargin, LayerThinner.MinuteLayer));
		var target = StraddlingColumnTargetFor(window, stopped.End - stopped.Start);

		(BucketFor(window, target) > stopped.End - stopped.Start).Should().BeTrue(
			$"A bucket of {BucketFor(window, target)} is narrower than the {stopped.End - stopped.Start} "
				+ "break, so date_bin alone would keep the marker apart from what follows it.");

		var result = await ReadHistoryAsync(
			seededArchive.Database.PlotConnectionString,
			window,
			_seededPenIds.Value,
			layer,
			target);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		var expected = SeededRowsIn(window, layer);

		result.Value.Select(envelope => envelope.PenId).Should().Equal(expected.Keys);

		foreach (var envelope in result.Value)
		{
			var rows = expected[envelope.PenId];
			var marker = rows.Should().ContainSingle(row => row.Quality == ArchiveRow.LastBeforeBreakQuality).Which;
			var resumption = rows.First(row => row.Timestamp > marker.Timestamp);

			// A shared date_bin slot leaves the segment as the one key that keeps the two apart.
			SlotOf(resumption.Timestamp, window, target).Should().Be(
				SlotOf(marker.Timestamp, window, target),
				$"pen {envelope.PenId}'s marker and its resumption must share a bucket at {layer}");

			GapColumnIndices(envelope).Should().ContainSingle();

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

		var result = await ReadHistoryAsync(seededArchive.Database.PlotConnectionString, window);

		AssertMatchesSeededRows(result, window);

		result.Value.Count.Should().Be(ArchiveTemplate.Slice.PenCount);

		// One column per pen, the seed row itself. The final archiving run is followed by no break, so its
		// last row carries no marker and nothing anchors after it.
		result.Value.Should().AllSatisfy(envelope => envelope.Timestamps.Should().ContainSingle());
	}

	// The look-back scales with the window, and this is the read that needs it: a pen silent for longer
	// than one partition width seeds a window wide enough to reach back to it instead of being dropped
	// from the chart. A recipe setpoint written once at process start is that case.
	[Fact]
	public async Task AWindowWiderThanTheLookBackFloorSeeksBackAsFarAsItAsks()
	{
		var opensAt = ArchiveTemplate.Slice.End + _seedLookBackFloor;
		var window = new LocalWindow(opensAt, opensAt + _wideWindowLength);

		// Every pen fell silent more than the floor ago, so a look-back fixed at the floor returns no row.
		_seededRawRows.Value.Should().NotContain(row => row.Timestamp >= window.From - _seedLookBackFloor);

		var result = await ReadHistoryAsync(seededArchive.Database.PlotConnectionString, window);

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
		await using var database = await postgresContainerFixture.CloneAsync(
			CloneSource.Provisioned,
			clone => ArchiveDatabase.ExecuteAsync(
				clone.WriterConnectionString,
				ArchiveReadSupport.DropTrendsCommand,
				TestContext.Current.CancellationToken),
			TestContext.Current.CancellationToken);

		var window = QuietWindow();
		using var logs = new FakeLoggerProvider();
		await using var services = ArchiveProviderFactory.Build(database.PlotConnectionString, logs);

		var result = await services.GetRequiredService<IDataProvider>().QueryHistoryAsync(
			_seededPenIds.Value,
			ArchiveProviderFactory.TimeConverter.ToUtc(window.From),
			ArchiveProviderFactory.TimeConverter.ToUtc(window.To),
			AggregationLayer.Raw,
			ColumnTargetFor(window),
			TestContext.Current.CancellationToken);

		result.IsFailed.Should().BeTrue();

		var error = result.Errors.OfType<ArchiveError>().Should().ContainSingle().Which;

		error.Kind.Should().Be(ArchiveFault.TableMissing);
		error.Detail.Should().Be("trends");
		error.Database.Should().Be(database.Name);

		var entry = HistoryReadEntries(logs).Should().ContainSingle().Which;
		entry.GetStructuredStateValue("Outcome").Should().Be("Failed");
		entry.GetStructuredStateValue("EnvelopeCount").Should().BeNull();
	}

	// Minute's point spacing is 15 s and its period four of those, so over the five-minute window below the
	// clamp lands the tail start exactly one minute before the window closes. A clock a day later leaves the
	// window end as the only edge of the clamp.
	[Fact]
	public async Task AMinuteWindowPastTheCoarseLayersNewestRowReachesTheRawLayersNewest()
	{
		await using var database = await WriteTailArchiveAsync(TestContext.Current.CancellationToken);
		var window = new LocalWindow(_tailWindowFrom, _tailWindowTo);

		var result = await ReadMinuteTailAsync(database, window, ClockAt(_tailWindowTo.AddDays(1)));

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		var fresh = result.Value.Should().ContainSingle(candidate => candidate.PenId == FreshPenId).Which;

		// The coarse rows, then every raw row after the pen's own seam. Without the tail the series would
		// stop at 10:04:00 archive-local and the operator would read a value fifty seconds old as the current one.
		fresh.Timestamps.Should().Equal(
			ExpectedUtc(_freshCoarse.Concat(RawTimestamps().Where(timestamp => timestamp > _freshSeam))));

		fresh.Timestamps[^1].Should().Be(ArchiveProviderFactory.TimeConverter.ToUtc(_newestRawTimestamp));

		// The tail rows overlap the coarse rows in time, and one of this pen's raw rows carries the very
		// timestamp its coarse layer already reached. The fold's ascending check is what drops it.
		var reaching = result.Value.Should().ContainSingle(candidate => candidate.PenId == ReachingPenId).Which;

		reaching.Timestamps.Should().Equal(ExpectedUtc(_reachingCoarse));
		reaching.Timestamps.Should().Equal(reaching.Timestamps.Distinct());

		var lagging = result.Value.Should().ContainSingle(candidate => candidate.PenId == LaggingPenId).Which;

		lagging.Timestamps.Should().Equal(ExpectedUtc(_laggingCoarse));
		lagging.Timestamps[^1].Should().Be(ArchiveProviderFactory.TimeConverter.ToUtc(_laggingSeam));

		GapColumnIndices(lagging).Should().BeEmpty();
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

	// docs/architecture/data-integration.md#layer-ladder
	[Fact]
	public async Task TheRangeTheChartAsksForReachesTheRawLayersNewest()
	{
		await using var database = await WriteTailArchiveAsync(TestContext.Current.CancellationToken);

		var result = await ReadMinuteTailAsync(database, ExpandedTailWindow(), ClockAt(_tailWindowTo));

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		var envelope = result.Value.Should().ContainSingle(candidate => candidate.PenId == FreshPenId).Which;

		envelope.Timestamps.Should().Equal(
			ExpectedUtc(_freshCoarse.Concat(RawTimestamps().Where(timestamp => timestamp > _freshSeam))));
		envelope.Timestamps[^1].Should().Be(ArchiveProviderFactory.TimeConverter.ToUtc(_newestRawTimestamp));
	}

	// One minute behind the fresh seam: the lagging seam stays below the clamp, and the fresh seam is within one
	// Minute point spacing of the present, so no tail is read. Over the visible window the window end alone
	// would clamp at 10:04:00 archive-local and read one, so that window takes the present as its edge too.
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task AClockBehindTheFreshSeamReadsNoTailRow(bool overTheExpandedWindow)
	{
		await using var database = await WriteTailArchiveAsync(TestContext.Current.CancellationToken);
		var window = overTheExpandedWindow ? ExpandedTailWindow() : new LocalWindow(_tailWindowFrom, _tailWindowTo);

		var result = await ReadMinuteTailAsync(database, window, ClockAt(_freshSeam.AddMinutes(-1)));

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		result.Value.Select(envelope => envelope.PenId).Should().Equal(FreshPenId, LaggingPenId, ReachingPenId);
		result.Value[0].Timestamps.Should().Equal(ExpectedUtc(_freshCoarse));
		result.Value[1].Timestamps.Should().Equal(ExpectedUtc(_laggingCoarse));
		result.Value[2].Timestamps.Should().Equal(ExpectedUtc(_reachingCoarse));
	}

	[Fact]
	public async Task ATailUnderAWideBucketFoldsIntoOneColumnAtTheRawLayersNewest()
	{
		await using var database = await WriteTailArchiveAsync(TestContext.Current.CancellationToken);

		var result = await ReadHistoryAsync(
			database.PlotConnectionString,
			new LocalWindow(_tailWindowFrom, _tailWindowTo),
			[FreshPenId, LaggingPenId, ReachingPenId],
			AggregationLayer.Minute,
			WideTailColumnTarget,
			ClockAt(_tailWindowTo.AddDays(1)));

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Select(envelope => envelope.PenId).Should().Equal(FreshPenId, LaggingPenId, ReachingPenId);

		var secondBucketStart = _tailWindowFrom.AddMinutes(3);
		var (fresh, lagging, reaching) = (result.Value[0], result.Value[1], result.Value[2]);

		fresh.Timestamps.Should().Equal(ExpectedUtc([_tailWindowFrom.AddMinutes(2), _freshSeam, _newestRawTimestamp]));
		lagging.Timestamps.Should().Equal(ExpectedUtc([_laggingSeam]));
		reaching.Timestamps.Should().Equal(ExpectedUtc([_tailWindowFrom.AddMinutes(2), _newestRawTimestamp]));

		fresh.Min.Should().Equal(TailValues(FreshPenId, [_tailWindowFrom, secondBucketStart, _freshSeam]));
		fresh.Max.Should().Equal(
			TailValues(FreshPenId, [_tailWindowFrom.AddMinutes(2), _freshSeam, _newestRawTimestamp]));
		fresh.Center.Should().Equal(fresh.Max);

		lagging.Min.Should().Equal(TailValues(LaggingPenId, [_tailWindowFrom]));
		lagging.Max.Should().Equal(TailValues(LaggingPenId, [_laggingSeam]));
		lagging.Center.Should().Equal(lagging.Max);

		reaching.Min.Should().Equal(TailValues(ReachingPenId, [_tailWindowFrom, secondBucketStart]));
		reaching.Max.Should().Equal(
			TailValues(ReachingPenId, [_tailWindowFrom.AddMinutes(2), _newestRawTimestamp]));
		reaching.Center.Should().Equal(reaching.Max);
	}

	// Hour's clamp is one hour and its skip threshold fifteen minutes, so a seam twenty-five minutes before
	// the window end reads the raw rows past it.
	[Fact]
	public async Task AnHourWindowPastTheCoarseLayersNewestRowReachesTheRawLayersNewest()
	{
		await using var database = await WriteTailArchiveAsync(TestContext.Current.CancellationToken);
		var window = new LocalWindow(_hourWindowFrom, _tailWindowTo);

		var result = await ReadHistoryAsync(
			database.PlotConnectionString,
			window,
			[HourTailPenId],
			AggregationLayer.Hour,
			ColumnTargetFor(window),
			ClockAt(_tailWindowTo.AddDays(1)));

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		var envelope = result.Value.Should().ContainSingle().Which;

		envelope.Timestamps.Should().Equal(ExpectedUtc(_hourCoarse.Concat(RawTimestamps())));
		GapColumnIndices(envelope).Should().BeEmpty();
	}

	// The tail goes through the bucketed statement, so a break inside it gets the gap column a break inside
	// the coarse window gets.
	[Fact]
	public async Task AFreshTailCarryingABreakDrawsItsGapColumn()
	{
		await using var database = await WriteTailArchiveAsync(TestContext.Current.CancellationToken);
		var window = new LocalWindow(_tailWindowFrom, _tailWindowTo);

		var result = await ReadHistoryAsync(
			database.PlotConnectionString,
			window,
			[BreakTailPenId],
			AggregationLayer.Minute,
			ColumnTargetFor(window),
			ClockAt(_tailWindowTo.AddDays(1)));

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		var envelope = result.Value.Should().ContainSingle().Which;
		var markerColumn = ArchiveProviderFactory.TimeConverter.ToUtc(_tailBreakMarker);
		DateTime[] expected =
		[
			.. ExpectedUtc([.. _freshCoarse, _freshSeam + _rawStep, _tailBreakMarker]),
			markerColumn.AddTicks(1),
			.. ExpectedUtc([_tailBreakResumption, _newestRawTimestamp])
		];

		envelope.Timestamps.Should().Equal(expected);
		GapColumnIndices(envelope).Should().Equal(_freshCoarse.Length + 2);
	}

	[Fact]
	public async Task AHistoryReadLogsItsRequestOutcomeAndDuration()
	{
		await using var database = await WriteTailArchiveAsync(TestContext.Current.CancellationToken);
		using var logs = new FakeLoggerProvider();
		await using var services = ArchiveProviderFactory.Build(database.PlotConnectionString, logs);
		var provider = services.GetRequiredService<IDataProvider>();
		var window = new LocalWindow(_tailWindowFrom, _tailWindowTo);
		int[] penIds = [FreshPenId, LaggingPenId, ReachingPenId];

		foreach (var layer in new[] { AggregationLayer.Raw, AggregationLayer.Minute })
		{
			var result = await provider.QueryHistoryAsync(
				penIds,
				ArchiveProviderFactory.TimeConverter.ToUtc(window.From),
				ArchiveProviderFactory.TimeConverter.ToUtc(window.To),
				layer,
				ColumnTargetFor(window),
				TestContext.Current.CancellationToken);

			result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		}

		var entries = HistoryReadEntries(logs);

		entries.Select(entry => entry.GetStructuredStateValue("Layer")).Should().Equal("Raw", "Minute");
		entries.Should().AllSatisfy(entry =>
		{
			entry.Level.Should().Be(LogLevel.Debug);
			LoggedUtc(entry, "FromUtc").Should().Be(ArchiveProviderFactory.TimeConverter.ToUtc(window.From));
			LoggedUtc(entry, "ToUtc").Should().Be(ArchiveProviderFactory.TimeConverter.ToUtc(window.To));
			entry.GetStructuredStateValue("TargetColumnCount").Should().Be(
				ColumnTargetFor(window).ToString(CultureInfo.InvariantCulture));
			entry.GetStructuredStateValue("Outcome").Should().Be("Read");
			entry.GetStructuredStateValue("PenCount").Should().Be("3");
			entry.GetStructuredStateValue("GroupCount").Should().Be("3");
			entry.GetStructuredStateValue("EnvelopeCount").Should().Be("3");
			long.TryParse(entry.GetStructuredStateValue("ElapsedMilliseconds"), CultureInfo.InvariantCulture, out _)
				.Should().BeTrue();
		});
	}

	// Five pens split 2, 2, 1, the late pen alone in the last group. The wide target bins the tail from its
	// start, so a tail start taken per group would move the late pen's last column off the one-statement read.
	// The grouped read names the pens descending with one repeated, as no group split may depend on order.
	[Theory]
	[InlineData(AggregationLayer.Raw, false)]
	[InlineData(AggregationLayer.Raw, true)]
	[InlineData(AggregationLayer.Minute, false)]
	[InlineData(AggregationLayer.Minute, true)]
	public async Task AReadSplitIntoPenGroupsReturnsWhatOneStatementReturns(AggregationLayer layer, bool wideBucket)
	{
		await using var database = await WriteTailArchiveAsync(TestContext.Current.CancellationToken);
		var window = new LocalWindow(_tailWindowFrom, _tailWindowTo);
		var target = wideBucket ? WideTailColumnTarget : ColumnTargetFor(window);
		int[] penIds = [FreshPenId, LaggingPenId, ReachingPenId, HourTailPenId, LatePenId];
		int[] descendingWithARepeat =
			[LatePenId, HourTailPenId, ReachingPenId, ReachingPenId, LaggingPenId, FreshPenId];

		var (oneGroup, oneGroupEntry) = await ReadInPenGroupsAsync(database, window, penIds, layer, target, 1);
		var (grouped, groupedEntry) = await ReadInPenGroupsAsync(
			database,
			window,
			descendingWithARepeat,
			layer,
			target,
			PostgresDataProvider.HistoryReadConcurrency);

		oneGroupEntry.GetStructuredStateValue("GroupCount").Should().Be("1");
		groupedEntry.GetStructuredStateValue("GroupCount").Should().Be("3");
		groupedEntry.GetStructuredStateValue("PenCount").Should().Be(
			penIds.Length.ToString(CultureInfo.InvariantCulture), "the repeated pen is read once");
		oneGroup.Should().NotBeEmpty();
		oneGroup.Select(envelope => envelope.PenId).Should().BeInAscendingOrder();
		grouped.Select(envelope => envelope.PenId).Should().Equal(oneGroup.Select(envelope => envelope.PenId));

		foreach (var (groupedEnvelope, single) in grouped.Zip(oneGroup))
		{
			groupedEnvelope.Timestamps.Should().Equal(single.Timestamps, $"pen {single.PenId}'s columns at {layer}");
			groupedEnvelope.Min.Should().Equal(single.Min, $"pen {single.PenId}'s minima at {layer}");
			groupedEnvelope.Max.Should().Equal(single.Max, $"pen {single.PenId}'s maxima at {layer}");
			groupedEnvelope.Center.Should().Equal(single.Center, $"pen {single.PenId}'s centres at {layer}");
		}
	}

	// Every group's statement waits on the lock, so the cancel lands on each of them mid-statement.
	[Fact]
	public async Task AGroupedReadCancelledMidStatementReturnsEveryConnectionBeforeItThrows()
	{
		var testCancellation = TestContext.Current.CancellationToken;
		await using var database = await postgresContainerFixture.CloneAsync(
			CloneSource.Provisioned, cancellationToken: testCancellation);
		await using var locker = new NpgsqlConnection(database.AdminConnectionString);
		await locker.OpenAsync(testCancellation);
		await using var transaction = await locker.BeginTransactionAsync(testCancellation);
		await using var lockTrends = new NpgsqlCommand(LockTrendsCommand, locker, transaction);
		await lockTrends.ExecuteNonQueryAsync(testCancellation);

		var window = QuietWindow();
		using var cancellation = new CancellationTokenSource();
		await using var services = ArchiveProviderFactory.Build(database.PlotConnectionString);

		_seededPenIds.Value.Count.Should().BeGreaterThanOrEqualTo(PostgresDataProvider.HistoryReadConcurrency);

		var read = services.GetRequiredService<IDataProvider>().QueryHistoryAsync(
			_seededPenIds.Value,
			ArchiveProviderFactory.TimeConverter.ToUtc(window.From),
			ArchiveProviderFactory.TimeConverter.ToUtc(window.To),
			AggregationLayer.Raw,
			ColumnTargetFor(window),
			cancellation.Token);

		try
		{
			(await AwaitALockWaitOnTrendsAsync(locker, transaction, PostgresDataProvider.HistoryReadConcurrency))
				.Should().BeTrue("every group's statement has to be waiting on the server when the token is cancelled");

			BusyConnections(services.GetRequiredService<NpgsqlDataSource>())
				.Should().Be(PostgresDataProvider.HistoryReadConcurrency);

			await cancellation.CancelAsync();

			var settled = () => read.WaitAsync(_cancelledReadDeadline, testCancellation);

			await settled.Should().ThrowAsync<OperationCanceledException>();

			BusyConnections(services.GetRequiredService<NpgsqlDataSource>()).Should().Be(0);
		}
		finally
		{
			await transaction.RollbackAsync(testCancellation);
		}
	}

	// The pool holds one connection fewer than the groups: three statements wait on the lock while the fourth
	// group fails at the pool wait, so one group faults while the others are mid-statement.
	[Fact]
	public async Task AGroupFailingWhileTheOthersWaitFailsTheReadAfterEveryConnectionReturns()
	{
		const int PooledConnections = PostgresDataProvider.HistoryReadConcurrency - 1;
		var testCancellation = TestContext.Current.CancellationToken;
		await using var database = await postgresContainerFixture.CloneAsync(
			CloneSource.Provisioned, cancellationToken: testCancellation);
		await using var locker = new NpgsqlConnection(database.AdminConnectionString);
		await locker.OpenAsync(testCancellation);
		await using var transaction = await locker.BeginTransactionAsync(testCancellation);
		await using var lockTrends = new NpgsqlCommand(LockTrendsCommand, locker, transaction);
		await lockTrends.ExecuteNonQueryAsync(testCancellation);

		var window = QuietWindow();
		using var logs = new FakeLoggerProvider();
		await using var services = ArchiveProviderFactory.Build(
			database.PlotConnectionString, logs, maxPoolSize: PooledConnections);

		var read = services.GetRequiredService<IDataProvider>().QueryHistoryAsync(
			_seededPenIds.Value,
			ArchiveProviderFactory.TimeConverter.ToUtc(window.From),
			ArchiveProviderFactory.TimeConverter.ToUtc(window.To),
			AggregationLayer.Raw,
			ColumnTargetFor(window),
			testCancellation);

		try
		{
			(await AwaitALockWaitOnTrendsAsync(locker, transaction, PooledConnections))
				.Should().BeTrue("every pooled group's statement has to be waiting when the last group fails");

			var result = await read.WaitAsync(_cancelledReadDeadline, testCancellation);

			BusyConnections(services.GetRequiredService<NpgsqlDataSource>()).Should().Be(0);
			result.IsFailed.Should().BeTrue();
			result.Errors.OfType<ArchiveError>().Should().ContainSingle()
				.Which.Kind.Should().Be(ArchiveFault.Unreachable);
			HistoryReadEntries(logs).Should().ContainSingle()
				.Which.GetStructuredStateValue("GroupCount").Should().Be(
					PostgresDataProvider.HistoryReadConcurrency.ToString(CultureInfo.InvariantCulture));
		}
		finally
		{
			await transaction.RollbackAsync(testCancellation);
		}
	}

	// Npgsql keeps its pool counters internal: NpgsqlDataSource.Statistics is a (Total, Idle, Busy) tuple.
	private static int BusyConnections(NpgsqlDataSource dataSource)
	{
		var statistics = typeof(NpgsqlDataSource)
			.GetProperty("Statistics", BindingFlags.Instance | BindingFlags.NonPublic)
			?.GetValue(dataSource);

		return statistics is ValueTuple<int, int, int> counters
			? counters.Item3
			: throw new InvalidOperationException("NpgsqlDataSource no longer carries its internal Statistics tuple.");
	}

	private static async Task<(IReadOnlyList<PenHistoryEnvelope> Envelopes, FakeLogRecord Entry)> ReadInPenGroupsAsync(
		ArchiveDatabase database,
		LocalWindow window,
		int[] penIds,
		AggregationLayer layer,
		int targetColumnCount,
		int historyReadConcurrency)
	{
		using var logs = new FakeLoggerProvider();
		await using var services = ArchiveProviderFactory.Build(
			database.PlotConnectionString,
			logs,
			ClockAt(_tailWindowTo.AddDays(1)),
			historyReadConcurrency);

		var result = await services.GetRequiredService<IDataProvider>().QueryHistoryAsync(
			penIds,
			ArchiveProviderFactory.TimeConverter.ToUtc(window.From),
			ArchiveProviderFactory.TimeConverter.ToUtc(window.To),
			layer,
			targetColumnCount,
			TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		return (result.Value, HistoryReadEntries(logs).Should().ContainSingle().Subject);
	}

	private static async Task<bool> AwaitALockWaitOnTrendsAsync(
		NpgsqlConnection locker,
		NpgsqlTransaction transaction,
		long waitingStatementCount = 1)
	{
		await using var command = new NpgsqlCommand(LockWaitOnTrendsQuery, locker, transaction);
		var deadline = DateTime.UtcNow + _cancelledReadDeadline;

		while (DateTime.UtcNow < deadline)
		{
			if (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) is long waiting
				&& waiting >= waitingStatementCount)
			{
				return true;
			}

			await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
		}

		return false;
	}

	private static IReadOnlyList<FakeLogRecord> HistoryReadEntries(FakeLoggerProvider logs)
	{
		return
		[
			.. logs.Collector.GetSnapshot()
				.Where(entry => entry.Category == typeof(PostgresDataProvider).FullName)
				.Where(entry => entry.StructuredState?.Any(pair => pair.Key == "Outcome") == true)
		];
	}

	private static DateTime LoggedUtc(FakeLogRecord entry, string key)
	{
		return DateTime.Parse(
			entry.GetStructuredStateValue(key)!,
			CultureInfo.InvariantCulture,
			DateTimeStyles.RoundtripKind);
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

	// A gap column draws no line, so it has no centre to bracket.
	private static void AssertEachColumnBracketsItsCenter(PenHistoryEnvelope envelope)
	{
		var gapColumns = GapColumnIndices(envelope);

		for (var column = 0; column < envelope.Timestamps.Count; column++)
		{
			if (gapColumns.Contains(column))
			{
				continue;
			}

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
		await using var connection = new NpgsqlConnection(seededArchive.Database.PlotConnectionString);

		await connection.OpenAsync(TestContext.Current.CancellationToken);

		await using var command = new NpgsqlCommand(
			"EXPLAIN (ANALYZE, TIMING OFF) " + ArchiveStatements.BucketedHistoryWindow,
			connection);

		PostgresDataProvider.BindLocalBucketedWindow(
			command,
			[.. _seededPenIds.Value],
			window.From,
			window.To,
			AggregationLayer.Raw,
			BucketFor(window, columnTarget));

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
		int targetColumnCount,
		TimeProvider? clock = null)
	{
		await using var services = ArchiveProviderFactory.Build(connectionString, timeProvider: clock);

		// January under the source zone: one offset, so the round trip is exact.
		return await services.GetRequiredService<IDataProvider>().QueryHistoryAsync(
			penIds,
			ArchiveProviderFactory.TimeConverter.ToUtc(window.From),
			ArchiveProviderFactory.TimeConverter.ToUtc(window.To),
			layer,
			targetColumnCount);
	}

	// The target that keeps a Raw bucket at or under the archive's tightest row spacing, never below the
	// floor a coarse read needs.
	private static int ColumnTargetFor(LocalWindow window)
	{
		return Math.Max(ColumnTargetFloor, (int)Math.Ceiling((window.To - window.From) / _tightestRowSpacing));
	}

	private static TimeSpan BucketFor(LocalWindow window)
	{
		return BucketFor(window, ColumnTargetFor(window));
	}

	private static TimeSpan BucketFor(LocalWindow window, int targetColumnCount)
	{
		return PostgresDataProvider.BucketFor(window.From, window.To, targetColumnCount);
	}

	// The coarsest target whose bucket still spans twice the break.
	private static int StraddlingColumnTargetFor(LocalWindow window, TimeSpan breakLength)
	{
		return Math.Max(1, (int)((window.To - window.From) / (breakLength + breakLength)));
	}

	// The date_bin slot the statement puts a row in, counted from the window start.
	private static long SlotOf(DateTime archiveLocal, LocalWindow window, int targetColumnCount)
	{
		return (archiveLocal - window.From).Ticks / BucketFor(window, targetColumnCount).Ticks;
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
		var timestamp = ArchiveProviderFactory.TimeConverter.ToUtc(group[^1].Timestamp);

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
		// pen identifiers, which is the order the expectation is built in.
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
			var timestamp = ArchiveProviderFactory.TimeConverter.ToUtc(row.Timestamp);
			columns.Add((timestamp, row.Value));

			if (row.Quality == ArchiveRow.LastBeforeBreakQuality)
			{
				columns.Add((timestamp.AddTicks(1), double.NaN));
			}
		}

		return columns;
	}

	// Every column the fold's break anchor produces. BucketedRowFold writes NaN into all three series of a
	// gap column, so one series carries the count.
	private static IReadOnlyList<int> GapColumnIndices(PenHistoryEnvelope envelope)
	{
		return [.. Enumerable.Range(0, envelope.Timestamps.Count).Where(index => double.IsNaN(envelope.Center[index]))];
	}

	// Each pen's rows of one layer inside the window, led by the row the statement's seed branch finds before
	// it. A pen carrying a seed and no window row is present here with the seed alone.
	private static SortedDictionary<int, IReadOnlyList<ArchiveRow>> SeededRowsIn(
		LocalWindow window,
		AggregationLayer layer = AggregationLayer.Raw)
	{
		var layerRows = SeededRowsAt(layer);
		var rowsByPen = new SortedDictionary<int, IReadOnlyList<ArchiveRow>>();

		foreach (var penId in _seededPenIds.Value)
		{
			var rows = SeedBefore(layerRows, penId, window)
				.Concat(layerRows
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

	private static IReadOnlyList<ArchiveRow> SeededRowsAt(AggregationLayer layer)
	{
		return layer switch
		{
			AggregationLayer.Raw => _seededRawRows.Value,
			AggregationLayer.Minute => _seededMinuteRows.Value,
			AggregationLayer.Hour => _seededHourRows.Value,
			_ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "The template holds no rows read at it.")
		};
	}

	// The row the statement's seed branch returns: this pen's last row of the layer strictly before the
	// window, no further back than the look-back the statement carries.
	private static IEnumerable<ArchiveRow> SeedBefore(
		IReadOnlyList<ArchiveRow> layerRows,
		int penId,
		LocalWindow window)
	{
		var seed = layerRows
			.Where(row => row.Id == penId
				&& row.Timestamp < window.From
				&& row.Timestamp >= window.From - SeedLookBackFor(window))
			.OrderByDescending(row => row.Timestamp)
			.FirstOrDefault();

		return seed is null ? [] : [seed];
	}

	private Task<ArchiveDatabase> WriteTailArchiveAsync(CancellationToken cancellationToken)
	{
		return postgresContainerFixture.CloneAsync(
			CloneSource.Provisioned,
			clone => new ArchiveWriter(clone.WriterConnectionString).WriteAsync(
				TailArchiveRows(),
				_tailDay,
				_tailDay.AddDays(1),
				cancellationToken: cancellationToken),
			cancellationToken);
	}

	private static Task<Result<IReadOnlyList<PenHistoryEnvelope>>> ReadTailWindowAsync(
		ArchiveDatabase database,
		AggregationLayer layer)
	{
		return ReadHistoryAsync(
			database.PlotConnectionString,
			new LocalWindow(_tailWindowFrom, _tailWindowTo),
			[FreshPenId, LaggingPenId, ReachingPenId],
			layer);
	}

	private static Task<Result<IReadOnlyList<PenHistoryEnvelope>>> ReadMinuteTailAsync(
		ArchiveDatabase database,
		LocalWindow window,
		TimeProvider clock)
	{
		return ReadHistoryAsync(
			database.PlotConnectionString,
			window,
			[FreshPenId, LaggingPenId, ReachingPenId],
			AggregationLayer.Minute,
			ColumnTargetFor(window),
			clock);
	}

	// As HistoryPrefetch.Expand widens the chart's request.
	private static LocalWindow ExpandedTailWindow()
	{
		var width = _tailWindowTo - _tailWindowFrom;

		return new LocalWindow(_tailWindowFrom - width, _tailWindowTo + width);
	}

	private static FixedClock ClockAt(DateTime archiveLocal)
	{
		return new FixedClock(new DateTimeOffset(ArchiveProviderFactory.TimeConverter.ToUtc(archiveLocal)));
	}

	// A coarse row carries the value of the raw row at its instant, as a vendor copy does.
	private static IReadOnlyList<ArchiveRow> TailArchiveRows()
	{
		var rows = new List<ArchiveRow>();

		foreach (var penId in new[] { FreshPenId, LaggingPenId, ReachingPenId, LatePenId })
		{
			rows.AddRange(RawTimestamps().Select(timestamp => RawRow(penId, timestamp)));
		}

		rows.AddRange(_hourCoarse.Concat(RawTimestamps()).Select(timestamp => RawRow(HourTailPenId, timestamp)));
		rows.AddRange(BreakTailRawRows());

		rows.AddRange(CoarseRows(FreshPenId, MinuteLayer, _freshCoarse));
		rows.AddRange(CoarseRows(LaggingPenId, MinuteLayer, _laggingCoarse));
		rows.AddRange(CoarseRows(ReachingPenId, MinuteLayer, _reachingCoarse));
		rows.AddRange(CoarseRows(HourTailPenId, HourLayer, _hourCoarse));
		rows.AddRange(CoarseRows(BreakTailPenId, MinuteLayer, _freshCoarse));
		rows.AddRange(CoarseRows(LatePenId, MinuteLayer, _lateCoarse));

		return rows;
	}

	private static IEnumerable<ArchiveRow> BreakTailRawRows()
	{
		foreach (var timestamp in RawTimestamps())
		{
			if (timestamp > _tailBreakMarker && timestamp < _tailBreakResumption)
			{
				continue;
			}

			var quality = timestamp == _tailBreakMarker ? ArchiveRow.LastBeforeBreakQuality
				: timestamp == _tailBreakResumption ? ArchiveRow.FirstAfterBreakQuality
				: ArchiveRow.OrdinaryQuality;

			yield return RawRow(BreakTailPenId, timestamp) with { Quality = quality };
		}
	}

	private static ArchiveRow RawRow(int penId, DateTime timestamp)
	{
		return new ArchiveRow(
			penId,
			ArchiveRow.RawLayer,
			timestamp,
			TailValue(penId, timestamp),
			ArchiveRow.OrdinaryQuality);
	}

	private static IEnumerable<ArchiveRow> CoarseRows(int penId, short layer, IEnumerable<DateTime> timestamps)
	{
		return timestamps.Select(timestamp => RawRow(penId, timestamp) with { Layer = layer });
	}

	// Rises with time and differs per pen, so a column's extremes say which rows its bucket held.
	private static double TailValue(int penId, DateTime timestamp)
	{
		return (penId * 1000) + (timestamp - _tailWindowFrom).TotalSeconds;
	}

	private static IReadOnlyList<double> TailValues(int penId, IEnumerable<DateTime> timestamps)
	{
		return [.. timestamps.Select(timestamp => TailValue(penId, timestamp))];
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
		return [.. archiveLocal.Select(ArchiveProviderFactory.TimeConverter.ToUtc)];
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

	private sealed class FixedClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow()
		{
			return now;
		}
	}
}
