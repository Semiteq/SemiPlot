using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tests.Unit.Fixtures;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Unit.Postgres;

// The same fold as HistoryRowFoldTests, driven by RealArchiveFixture's extract of the vendor's own
// archive dump, so these tests measure the archive rather than this repository's model of it. No
// database is involved; the target column count stays far above the row count, so no bucket forms.
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

		breakMarkers.Should().Be(4);
		resumptions.Should().Be(3);

		var envelope = FoldOf(run);

		// Counting rather than finding: a fold that nulls the marker instead of appending after it keeps
		// the row count, and a fold that anchors after q = 16 as well produces seven anchors here.
		envelope.Timestamps.Count.Should().Be(run.Count + breakMarkers);
		NaNColumnCount(envelope).Should().Be(breakMarkers);
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

		envelope.Timestamps.Count.Should().Be(slice.Length + 1);
		NaNColumnCount(envelope).Should().Be(1);

		// The marker's own sample survives as a real column and the anchor goes one tick after it.
		envelope.Timestamps[0].Should().Be(Utc(opening.Timestamp));
		envelope.Center[0].Should().Be(opening.Value);
		envelope.Timestamps[1].Should().Be(Utc(opening.Timestamp).AddTicks(1));
		double.IsNaN(envelope.Center[1]).Should().BeTrue();

		// The resumption is a real column with no anchor after it, so the line restarts there and stays.
		envelope.Timestamps[2].Should().Be(Utc(resumption.Timestamp));
		envelope.Center[2].Should().Be(resumption.Value);
		envelope.Center.Skip(2).Should().AllSatisfy(value => double.IsNaN(value).Should().BeFalse());
	}

	// The archive's own proof that a row absence is not a break: pen 9002 wrote nothing for 4 min
	// 18.377 s with no break marker on either bound, and a fold treating absence as a break would
	// shred this into two segments while still passing every marker-pair test above.
	[Fact]
	public void TheMarkerlessAbsenceOfPen9002YieldsNoAnchor()
	{
		var run = RawRun(MarkerlessAbsencePen);
		var lastBefore = run.Single(row => row.Timestamp == _lastRowBeforeTheAbsence);
		var firstAfter = run.Single(row => row.Timestamp == _firstRowAfterTheAbsence);

		lastBefore.Quality.Should().Be(ArchiveRow.OrdinaryQuality);
		(firstAfter.Timestamp - lastBefore.Timestamp).Should().Be(TimeSpan.FromMilliseconds(258377));
		(firstAfter.Timestamp - lastBefore.Timestamp > RealArchiveFixture.PollInterval * 2500).Should().BeTrue(
			"the absence is shorter than the 2 500 polls this test is about");

		var envelope = FoldOf(run);

		// Nothing at all lands inside the absence: not an anchor, and not a column of any other kind.
		envelope.Timestamps.Should().NotContain(
			timestamp => timestamp > Utc(lastBefore.Timestamp) && timestamp < Utc(firstAfter.Timestamp));

		// The two real samples across it are adjacent columns, both carrying their recorded value.
		var closingIndex = IndexOf(envelope, firstAfter.Timestamp);

		envelope.Timestamps[closingIndex - 1].Should().Be(Utc(lastBefore.Timestamp));
		envelope.Center[closingIndex - 1].Should().Be(lastBefore.Value);
		envelope.Center[closingIndex].Should().Be(firstAfter.Value);
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

		trailing.Quality.Should().Be(ArchiveRow.LastBeforeBreakQuality);
		run.Should().NotContain(
			row => row.Quality == ArchiveRow.FirstAfterBreakQuality && row.Timestamp > trailing.Timestamp);

		var envelope = FoldOf(run);

		envelope.Timestamps[^2].Should().Be(Utc(trailing.Timestamp));
		envelope.Center[^2].Should().Be(trailing.Value);
		envelope.Timestamps[^1].Should().Be(Utc(trailing.Timestamp).AddTicks(1));
		double.IsNaN(envelope.Center[^1]).Should().BeTrue();
	}

	private static IReadOnlyList<ArchiveRow> RawRun(int pen)
	{
		// The fold takes rows in the order the windowed statement produces them, ORDER BY id, t, and one
		// pen at a time is one consecutive ascending run.
		return [.. RealArchiveFixture.RawRows.Where(row => row.Id == pen).OrderBy(row => row.Timestamp)];
	}

	private static PenHistoryEnvelope FoldOf(IReadOnlyList<ArchiveRow> rows)
	{
		var folded = rows
			.Select(row => new HistoryRowFold.Row(row.Id, row.Timestamp, row.Value, row.Quality))
			.ToArray();

		return HistoryRowFold.Fold(folded, _utcConverter, NoDecimation).Should().ContainSingle().Which;
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
