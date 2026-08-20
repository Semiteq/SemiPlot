using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tests.Data.Fixtures;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

// The same fold as HistoryRowFoldTests, driven by rows the vendor actually wrote. RealArchiveFixture
// holds an extract of the customer archive dump: the two identifiers are synthetic and every timestamp
// carries one fixed offset, but the intervals, the values and the quality codes are the vendor's own.
// So these tests measure the archive rather than this repository's model of it — a mistake in what we
// imagined a break looks like cannot hide here the way it can in rows we invented.
//
// No database is involved; the CSV is the evidence. The target column count is far above the row count
// throughout, so the decimator passes rows through one column each and every column below is a row or
// an anchor rather than a bucket.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class RealArchiveGapTests
{
	private const int NoDecimation = 1000;

	// The pen the plan names for the markerless absence. Its twin 9001 carries the same absence at the
	// same instants, which is why the counting tests below run over both.
	private const int MarkerlessAbsencePen = 9002;

	// The absence itself: the last row before it and the first row after it, both the vendor's own.
	private static readonly DateTime _lastRowBeforeTheAbsence = new(2000, 1, 1, 13, 50, 46, 437);
	private static readonly DateTime _firstRowAfterTheAbsence = new(2000, 1, 1, 13, 55, 4, 814);

	private static readonly ArchiveTimeConverter _utcConverter = new(TimeZoneInfo.Utc);

	// The fixture's extract holds four break markers per pen against three resumptions: its last break
	// runs past the right edge of the extract, which is the trailing-break case from real data.
	[Theory]
	[InlineData(9001)]
	[InlineData(9002)]
	public void TheRawRunYieldsOneAnchorPerBreakMarkerAndNothingMore(int pen)
	{
		var run = RawRun(pen);
		var breakMarkers = run.Count(row => row.Quality == ArchiveRow.LastBeforeBreakQuality);
		var resumptions = run.Count(row => row.Quality == ArchiveRow.FirstAfterBreakQuality);

		Assert.Equal(4, breakMarkers);
		Assert.Equal(3, resumptions);

		var envelope = FoldOf(run);

		// Counting rather than finding: a fold that nulls the marker instead of appending after it keeps
		// the row count, and a fold that anchors after q = 16 as well produces seven anchors here.
		Assert.Equal(run.Count + breakMarkers, envelope.Timestamps.Count);
		Assert.Equal(breakMarkers, NaNColumnCount(envelope));
	}

	// The marker pair the extract was chosen around: q = 32 at 13:55:04.814 and q = 16 at 13:55:08.369,
	// a break of 3.555 s the vendor recorded by its marks alone. The slice stops before the pen's own
	// trailing marker, which TheUnpairedTrailingBreakMarkerAnchors takes on its own.
	[Theory]
	[InlineData(9001)]
	[InlineData(9002)]
	public void TheMarkerPairInTheChosenMinuteYieldsExactlyOneGapColumn(int pen)
	{
		var run = RawRun(pen);
		var opening = run.First(row =>
			row.Quality == ArchiveRow.LastBeforeBreakQuality && row.Timestamp >= RealArchiveFixture.ChosenMinute);
		var resumption = run.First(row =>
			row.Quality == ArchiveRow.FirstAfterBreakQuality && row.Timestamp > opening.Timestamp);
		var nextOpening = run.First(row =>
			row.Quality == ArchiveRow.LastBeforeBreakQuality && row.Timestamp > resumption.Timestamp);
		var slice = run
			.Where(row => row.Timestamp >= opening.Timestamp && row.Timestamp < nextOpening.Timestamp)
			.ToArray();

		var envelope = FoldOf(slice);

		Assert.Equal(slice.Length + 1, envelope.Timestamps.Count);
		Assert.Equal(1, NaNColumnCount(envelope));

		// The marker's own sample survives as a real column and the anchor goes one tick after it.
		Assert.Equal(Utc(opening.Timestamp), envelope.Timestamps[0]);
		Assert.Equal(opening.Value, envelope.Center[0]);
		Assert.Equal(Utc(opening.Timestamp).AddTicks(1), envelope.Timestamps[1]);
		Assert.True(double.IsNaN(envelope.Center[1]));

		// The resumption is a real column with no anchor after it, so the line restarts there and stays.
		Assert.Equal(Utc(resumption.Timestamp), envelope.Timestamps[2]);
		Assert.Equal(resumption.Value, envelope.Center[2]);
		Assert.All(envelope.Center.Skip(2), value => Assert.False(double.IsNaN(value)));
	}

	// The archive's own proof that a row absence is not a break. Pen 9002 wrote nothing between
	// 13:50:46.437 and 13:55:04.814 — 4 min 18.377 s in a project polled every 100 ms, so roughly
	// 2 584 polls that recorded nothing because the value did not change. Neither bound carries a break
	// marker on the absence's side, and a fold treating absence as a break would shred this into two
	// segments while still passing every marker-pair test above.
	[Fact]
	public void TheMarkerlessAbsenceOfPen9002YieldsNoAnchor()
	{
		var run = RawRun(MarkerlessAbsencePen);
		var lastBefore = run.Single(row => row.Timestamp == _lastRowBeforeTheAbsence);
		var firstAfter = run.Single(row => row.Timestamp == _firstRowAfterTheAbsence);

		Assert.Equal(ArchiveRow.OrdinaryQuality, lastBefore.Quality);
		Assert.Equal(TimeSpan.FromMilliseconds(258377), firstAfter.Timestamp - lastBefore.Timestamp);
		Assert.True(
			firstAfter.Timestamp - lastBefore.Timestamp > RealArchiveFixture.PollInterval * 2500,
			"the absence is shorter than the 2 500 polls this test is about");

		var envelope = FoldOf(run);

		// Nothing at all lands inside the absence: not an anchor, and not a column of any other kind.
		Assert.DoesNotContain(
			envelope.Timestamps,
			timestamp => timestamp > Utc(lastBefore.Timestamp) && timestamp < Utc(firstAfter.Timestamp));

		// The two real samples across it are adjacent columns, both carrying their recorded value.
		var closingIndex = IndexOf(envelope, firstAfter.Timestamp);

		Assert.Equal(Utc(lastBefore.Timestamp), envelope.Timestamps[closingIndex - 1]);
		Assert.Equal(lastBefore.Value, envelope.Center[closingIndex - 1]);
		Assert.Equal(firstAfter.Value, envelope.Center[closingIndex]);
	}

	// A break running past the right edge of the read has no q = 16 to close it. Dropping its anchor
	// would draw the line straight on to whatever comes next.
	[Theory]
	[InlineData(9001)]
	[InlineData(9002)]
	public void TheUnpairedTrailingBreakMarkerAnchors(int pen)
	{
		var run = RawRun(pen);
		var trailing = run[^1];

		Assert.Equal(ArchiveRow.LastBeforeBreakQuality, trailing.Quality);
		Assert.DoesNotContain(
			run,
			row => row.Quality == ArchiveRow.FirstAfterBreakQuality && row.Timestamp > trailing.Timestamp);

		var envelope = FoldOf(run);

		Assert.Equal(Utc(trailing.Timestamp), envelope.Timestamps[^2]);
		Assert.Equal(trailing.Value, envelope.Center[^2]);
		Assert.Equal(Utc(trailing.Timestamp).AddTicks(1), envelope.Timestamps[^1]);
		Assert.True(double.IsNaN(envelope.Center[^1]));
	}

	private static IReadOnlyList<ArchiveRow> RawRun(int pen)
	{
		// The fold takes rows in the order the windowed statement produces them, ORDER BY id, t, and one
		// pen at a time is one consecutive ascending run.
		return RealArchiveFixture.RawRows.Where(row => row.Id == pen).OrderBy(row => row.Timestamp).ToArray();
	}

	private static PenHistoryEnvelope FoldOf(IReadOnlyList<ArchiveRow> rows)
	{
		var folded = rows
			.Select(row => new HistoryRowFold.Row(row.Id, row.Timestamp, row.Value, row.Quality))
			.ToArray();

		return Assert.Single(HistoryRowFold.Fold(folded, _utcConverter, NoDecimation));
	}

	private static int NaNColumnCount(PenHistoryEnvelope envelope)
	{
		return envelope.Center.Count(double.IsNaN);
	}

	private static int IndexOf(PenHistoryEnvelope envelope, DateTime archiveLocal)
	{
		return envelope.Timestamps.ToList().IndexOf(Utc(archiveLocal));
	}

	// The fixture's timestamps are naive archive wall-clock time and the converter here is UTC, so a
	// column's instant is the row's own value relabelled.
	private static DateTime Utc(DateTime archiveLocal)
	{
		return DateTime.SpecifyKind(archiveLocal, DateTimeKind.Utc);
	}
}
