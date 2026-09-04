using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Unit.Postgres;

// The fold takes materialised rows, so nothing here opens a connection. UTC is the zone wherever the
// conversion is not the subject, which keeps the expected instants readable; Europe/Berlin appears only
// where the conversion itself is what the test is about.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class HistoryRowFoldTests
{
	private const int TargetColumnCount = 100;

	// Read from ArchiveRow, not copied, so this fold's marks cannot drift from the seeder's own; the
	// production fold keeps a private const only because SemiPlot.DataSource.Postgres must not reference
	// the seeder.
	private const int LastBeforeBreakQuality = ArchiveRow.LastBeforeBreakQuality;
	private const int FirstAfterBreakQuality = ArchiveRow.FirstAfterBreakQuality;

	private static readonly ArchiveTimeConverter _utcConverter = new(TimeZoneInfo.Utc);

	private static DateTime Local(int hour, int minute)
	{
		return new DateTime(2026, 6, 15, hour, minute, 0, DateTimeKind.Unspecified);
	}

	private static DateTime Utc(int hour, int minute)
	{
		return new DateTime(2026, 6, 15, hour, minute, 0, DateTimeKind.Utc);
	}

	// The 2026 EU fall-back day, where 02:00 to 03:00 is read twice.
	private static DateTime FallBackLocal(int hour, int minute)
	{
		return new DateTime(2026, 10, 25, hour, minute, 0, DateTimeKind.Unspecified);
	}

	[Fact]
	public void NoRowsFoldIntoNoEnvelopes()
	{
		var envelopes = HistoryRowFold.Fold([], _utcConverter, TargetColumnCount);

		envelopes.Should().BeEmpty();
	}

	[Fact]
	public void AscendingRowsBelowTheTargetPassThroughOneColumnEach()
	{
		HistoryRowFold.Row[] rows =
		[
			new(7, Local(10, 0), 1.0, 0),
			new(7, Local(10, 1), 2.0, 0),
			new(7, Local(10, 2), 3.0, 0)
		];

		var envelope = HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount).Should().ContainSingle().Which;

		envelope.PenId.Should().Be(7);
		envelope.Timestamps.Should().Equal([Utc(10, 0), Utc(10, 1), Utc(10, 2)]);
		envelope.Timestamps.Should().AllSatisfy(timestamp => timestamp.Kind.Should().Be(DateTimeKind.Utc));
		envelope.Min.Should().Equal([1.0, 2.0, 3.0]);
		envelope.Max.Should().Equal([1.0, 2.0, 3.0]);
		envelope.Center.Should().Equal([1.0, 2.0, 3.0]);
	}

	// The only test putting more rows through the fold than the target admits, so it is what pins the fold
	// forwarding targetColumnCount to the decimator at all: every other test here stays below the target,
	// where the decimator passes rows through one column each and forwards nothing observable.
	[Fact]
	public void MoreRowsThanTheTargetColumnCountAreReducedToIt()
	{
		double[] values = [0.0, 5.0, 1.0, 6.0, 2.0, 7.0, 3.0, 8.0, 4.0, 9.0];
		var rows = values
			.Select((value, index) => new HistoryRowFold.Row(7, Local(10, index), value, 0))
			.ToArray();

		var envelope = HistoryRowFold.Fold(rows, _utcConverter, 2).Should().ContainSingle().Which;

		envelope.Timestamps.Should().Equal([Utc(10, 2), Utc(10, 7)]);
		envelope.Min.Should().Equal([0.0, 3.0]);
		envelope.Max.Should().Equal([6.0, 9.0]);
		envelope.Center.Should().Equal([1.0, 8.0]);
	}

	// Europe/Berlin's spring-forward gap: 02:30 does not exist, so it converts with the standard-time
	// offset and lands at 01:30 UTC, past the 03:00 row that follows it in the archive's own ordering.
	// The later row no longer advances the series and is the one dropped.
	[Fact]
	public void ASkippedLocalHourDropsTheRowTheConversionPutsOutOfOrder()
	{
		var converter = new ArchiveTimeConverter(TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));
		HistoryRowFold.Row[] rows =
		[
			new(7, new DateTime(2026, 3, 29, 1, 59, 0, DateTimeKind.Unspecified), 1.0, 0),
			new(7, new DateTime(2026, 3, 29, 2, 30, 0, DateTimeKind.Unspecified), 2.0, 0),
			new(7, new DateTime(2026, 3, 29, 3, 0, 0, DateTimeKind.Unspecified), 3.0, 0)
		];

		var envelope = HistoryRowFold.Fold(rows, converter, TargetColumnCount).Should().ContainSingle().Which;

		envelope.Timestamps.Should().Equal(
			[
				new DateTime(2026, 3, 29, 0, 59, 0, DateTimeKind.Utc),
				new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc)
			]);
		envelope.Center.Should().Equal([1.0, 2.0]);
	}

	// Europe/Berlin's autumn fall-back: the wall clock reads 02:00 to 03:00 twice, both passes carry the
	// same naive values, and ToUtc resolves both to the standard-time instants of the second pass.
	[Fact]
	public void TheSecondPassOverTheRepeatedHourIsDropped()
	{
		var converter = new ArchiveTimeConverter(TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));
		HistoryRowFold.Row[] rows =
		[
			new(7, FallBackLocal(1, 30), 1.0, 0),
			new(7, FallBackLocal(2, 0), 2.0, 0),
			new(7, FallBackLocal(2, 30), 3.0, 0),
			new(7, FallBackLocal(2, 0), 4.0, 0),
			new(7, FallBackLocal(2, 30), 5.0, 0),
			new(7, FallBackLocal(3, 0), 6.0, 0)
		];

		var envelope = HistoryRowFold.Fold(rows, converter, TargetColumnCount).Should().ContainSingle().Which;

		envelope.Timestamps.Should().Equal(
			[
				new DateTime(2026, 10, 24, 23, 30, 0, DateTimeKind.Utc),
				new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc),
				new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc),
				new DateTime(2026, 10, 25, 2, 0, 0, DateTimeKind.Utc)
			]);

		// The values of the second pass, 4.0 and 5.0, are the hour of real archive rows this costs.
		envelope.Center.Should().Equal([1.0, 2.0, 3.0, 6.0]);
	}

	// A pen the caller asked for that has no row in the window never reaches the fold, so it gets no
	// envelope rather than an empty one.
	[Fact]
	public void OnlyThePensCarryingRowsGetAnEnvelope()
	{
		HistoryRowFold.Row[] rows =
		[
			new(3, Local(10, 0), 1.0, 0),
			new(9, Local(10, 0), 2.0, 0)
		];

		var envelopes = HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount);

		envelopes.Select(envelope => envelope.PenId).Should().Equal([3, 9]);
	}

	[Fact]
	public void ThePreviousKeptTimestampResetsAtEachPen()
	{
		HistoryRowFold.Row[] rows =
		[
			new(3, Local(10, 0), 1.0, 0),
			new(3, Local(10, 5), 2.0, 0),
			new(9, Local(9, 0), 3.0, 0),
			new(9, Local(9, 5), 4.0, 0)
		];

		var envelopes = HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount);

		envelopes.Count.Should().Be(2);
		envelopes[0].Timestamps.Should().Equal([Utc(10, 0), Utc(10, 5)]);
		envelopes[1].Timestamps.Should().Equal([Utc(9, 0), Utc(9, 5)]);
	}

	// Counts columns rather than searching for one NaN: two plausible-but-wrong folds also produce a NaN
	// between the markers, and only the column count and the markers' own values separate them.
	private static int NaNColumnCount(PenHistoryEnvelope envelope)
	{
		return envelope.Center.Count(double.IsNaN);
	}

	private static HistoryRowFold.Row[] BreakPairRows()
	{
		return
		[
			new(7, Local(10, 0), 1.0, 0),
			new(7, Local(10, 1), 2.0, LastBeforeBreakQuality),
			// Four minutes of absence: the break itself, which the archive records only by its marks.
			new(7, Local(10, 5), 3.0, FirstAfterBreakQuality),
			new(7, Local(10, 6), 4.0, 0)
		];
	}

	[Fact]
	public void ABreakMarkerPairYieldsExactlyOneGapColumn()
	{
		var envelope = HistoryRowFold.Fold(BreakPairRows(), _utcConverter, TargetColumnCount)
			.Should().ContainSingle().Which;

		// Four rows and one anchor. A fold that nulls the marker instead of appending after it gives four
		// columns; a fold that anchors after q = 16 as well gives six and two NaN columns.
		envelope.Timestamps.Count.Should().Be(5);
		NaNColumnCount(envelope).Should().Be(1);
		envelope.Timestamps.Should().Equal(
			[Utc(10, 0), Utc(10, 1), Utc(10, 1).AddTicks(1), Utc(10, 5), Utc(10, 6)]);
		envelope.Center.Should().Equal([1.0, 2.0, double.NaN, 3.0, 4.0]);
	}

	[Fact]
	public void TheBreakMarkersOwnValueSurvivesAsTheColumnBeforeTheAnchor()
	{
		var envelope = HistoryRowFold.Fold(BreakPairRows(), _utcConverter, TargetColumnCount)
			.Should().ContainSingle().Which;

		var anchorIndex = Enumerable.Range(0, envelope.Center.Count)
			.Should().ContainSingle(index => double.IsNaN(envelope.Center[index])).Which;

		envelope.Timestamps[anchorIndex - 1].Should().Be(Utc(10, 1));
		envelope.Center[anchorIndex - 1].Should().Be(2.0);
		envelope.Min[anchorIndex - 1].Should().Be(2.0);
		envelope.Max[anchorIndex - 1].Should().Be(2.0);
		envelope.Timestamps[anchorIndex].Should().Be(Utc(10, 1).AddTicks(1));
	}

	[Fact]
	public void TheResumptionMarkerIsARealColumnWithNoAnchorAfterIt()
	{
		var envelope = HistoryRowFold.Fold(BreakPairRows(), _utcConverter, TargetColumnCount)
			.Should().ContainSingle().Which;

		var resumptionIndex = envelope.Timestamps.ToList().IndexOf(Utc(10, 5));

		envelope.Center[resumptionIndex].Should().Be(3.0);
		envelope.Min[resumptionIndex].Should().Be(3.0);
		envelope.Max[resumptionIndex].Should().Be(3.0);

		// Everything from the resumption onwards is a real column, so the line restarts and stays.
		envelope.Center.Skip(resumptionIndex)
			.Should().AllSatisfy(value => double.IsNaN(value).Should().BeFalse());
		(resumptionIndex + 1).Should().Be(envelope.Timestamps.Count - 1);
	}

	// Evidence 2: a row absence carries no meaning on its own — the value simply did not change.
	[Fact]
	public void ALongAbsenceWithNoBreakMarkerYieldsNoGapColumn()
	{
		HistoryRowFold.Row[] rows =
		[
			new(7, Local(10, 0), 1.0, 0),
			new(7, Local(10, 1), 2.0, 0),
			new(7, Local(14, 30), 3.0, 0)
		];

		var envelope = HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount).Should().ContainSingle().Which;

		envelope.Timestamps.Count.Should().Be(3);
		NaNColumnCount(envelope).Should().Be(0);
	}

	[Fact]
	public void TwoBreaksInOneWindowYieldTwoAnchors()
	{
		HistoryRowFold.Row[] rows =
		[
			new(7, Local(10, 0), 1.0, 0),
			new(7, Local(10, 1), 2.0, LastBeforeBreakQuality),
			new(7, Local(10, 5), 3.0, FirstAfterBreakQuality),
			new(7, Local(10, 6), 4.0, LastBeforeBreakQuality),
			new(7, Local(10, 9), 5.0, FirstAfterBreakQuality)
		];

		var envelope = HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount).Should().ContainSingle().Which;

		envelope.Timestamps.Count.Should().Be(7);
		NaNColumnCount(envelope).Should().Be(2);
		envelope.Timestamps.Should().Equal(
			[
				Utc(10, 0),
				Utc(10, 1),
				Utc(10, 1).AddTicks(1),
				Utc(10, 5),
				Utc(10, 6),
				Utc(10, 6).AddTicks(1),
				Utc(10, 9)
			]);
		envelope.Center.Should().Equal([1.0, 2.0, double.NaN, 3.0, 4.0, double.NaN, 5.0]);
	}

	// A break running past the window's right edge has no q = 16 inside the window. Dropping its anchor
	// would draw the line straight on to the next window's first sample.
	[Fact]
	public void ABreakMarkerAsTheLastRowStillAnchors()
	{
		HistoryRowFold.Row[] rows =
		[
			new(7, Local(10, 0), 1.0, 0),
			new(7, Local(10, 1), 2.0, LastBeforeBreakQuality)
		];

		var envelope = HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount).Should().ContainSingle().Which;

		envelope.Timestamps.Count.Should().Be(3);
		NaNColumnCount(envelope).Should().Be(1);
		envelope.Timestamps[^1].Should().Be(Utc(10, 1).AddTicks(1));
		envelope.Center.Should().Equal([1.0, 2.0, double.NaN]);
	}

	// The anchor is appended to the pen's list only when the marker row itself is kept, so the strict-ascent
	// guard governs both. The drop itself is TheSecondPassOverTheRepeatedHourIsDropped; this is what the
	// anchor does about it — a row that never reached the series cannot break the line.
	[Fact]
	public void ABreakMarkerDroppedByTheStrictAscentGuardEmitsNoAnchor()
	{
		var converter = new ArchiveTimeConverter(TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));
		HistoryRowFold.Row[] rows =
		[
			new(7, FallBackLocal(1, 30), 1.0, 0),
			new(7, FallBackLocal(2, 0), 2.0, 0),
			new(7, FallBackLocal(2, 30), 3.0, 0),
			new(7, FallBackLocal(2, 0), 4.0, LastBeforeBreakQuality),
			new(7, FallBackLocal(3, 0), 5.0, 0)
		];

		var envelope = HistoryRowFold.Fold(rows, converter, TargetColumnCount).Should().ContainSingle().Which;

		NaNColumnCount(envelope).Should().Be(0);
		envelope.Timestamps.Should().Equal(
			[
				new DateTime(2026, 10, 24, 23, 30, 0, DateTimeKind.Utc),
				new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc),
				new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc),
				new DateTime(2026, 10, 25, 2, 0, 0, DateTimeKind.Utc)
			]);
		envelope.Center.Should().Equal([1.0, 2.0, 3.0, 5.0]);
	}

	// The other decimator branch: eleven entries against a target of four, so the segments are bucketed
	// instead of passed through. The anchor is appended outside any bucket and keeps the null's own
	// timestamp, so it cannot be absorbed into the marker's column.
	[Fact]
	public void TheAnchorSurvivesDecimationAsItsOwnColumn()
	{
		var rows = Enumerable
			.Range(0, 10)
			.Select(index => new HistoryRowFold.Row(
				7,
				Local(10, index),
				index,
				index == 4 ? LastBeforeBreakQuality : 0))
			.ToArray();

		var envelope = HistoryRowFold.Fold(rows, _utcConverter, 4).Should().ContainSingle().Which;

		NaNColumnCount(envelope).Should().Be(1);
		envelope.Timestamps.Count.Should().Be(5);
		envelope.Timestamps[2].Should().Be(Utc(10, 4).AddTicks(1));
		double.IsNaN(envelope.Center[2]).Should().BeTrue();
	}

	// The windowed read's UNION ALL under one outer ORDER BY id, t hands a seeded pen to the fold as one
	// ascending run with the seed at its head, indistinguishable from a window row (Evidence 7).
	[Fact]
	public void APenCarryingASeedRowAndWindowRowsYieldsExactlyOneEnvelope()
	{
		HistoryRowFold.Row[] rows =
		[
			// The seed: the pen's last sample before the window opens at 10:00.
			new(7, Local(9, 55), 1.0, 0),
			new(7, Local(10, 0), 2.0, 0),
			new(7, Local(10, 1), 3.0, 0)
		];

		var envelope = HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount).Should().ContainSingle().Which;

		envelope.PenId.Should().Be(7);
		envelope.Timestamps[0].Should().Be(Utc(9, 55));
		envelope.Center[0].Should().Be(1.0);
		envelope.Timestamps.Should().Equal([Utc(9, 55), Utc(10, 0), Utc(10, 1)]);
		envelope.Center.Should().Equal([1.0, 2.0, 3.0]);
	}

	// Two seeded pens, to show the seed does not disturb the consecutive-identifier grouping: the count is
	// one envelope per pen, not one per branch of the union.
	[Fact]
	public void EachSeededPenGetsOneEnvelopeWithItsOwnSeedFirst()
	{
		HistoryRowFold.Row[] rows =
		[
			new(3, Local(9, 50), 1.0, 0),
			new(3, Local(10, 0), 2.0, 0),
			new(9, Local(9, 40), 3.0, 0),
			new(9, Local(10, 0), 4.0, 0)
		];

		var envelopes = HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount);

		envelopes.Count.Should().Be(2);
		envelopes.Select(envelope => envelope.PenId).Should().Equal([3, 9]);
		envelopes[0].Timestamps.Should().Equal([Utc(9, 50), Utc(10, 0)]);
		envelopes[1].Timestamps.Should().Equal([Utc(9, 40), Utc(10, 0)]);
		envelopes[0].Center.Should().Equal([1.0, 2.0]);
		envelopes[1].Center.Should().Equal([3.0, 4.0]);
	}

	// The seed row carries its own q, so a seed marked q = 32 says the window opens inside a break. The
	// anchor then lands at index 1 and the series opens on the seed's value, the anchor, and only then the
	// first in-window sample — never on a line drawn from the seed across the break.
	[Fact]
	public void ASeedMarkedAsABreakOpensTheWindowInsideAGap()
	{
		HistoryRowFold.Row[] rows =
		[
			new(7, Local(9, 55), 1.0, LastBeforeBreakQuality),
			new(7, Local(10, 0), 2.0, FirstAfterBreakQuality),
			new(7, Local(10, 1), 3.0, 0)
		];

		var envelope = HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount).Should().ContainSingle().Which;

		NaNColumnCount(envelope).Should().Be(1);
		envelope.Timestamps.Should().Equal(
			[Utc(9, 55), Utc(9, 55).AddTicks(1), Utc(10, 0), Utc(10, 1)]);
		envelope.Center.Should().Equal([1.0, double.NaN, 2.0, 3.0]);
		double.IsNaN(envelope.Center[1]).Should().BeTrue();
	}

	// The same seed inside a break, on the decimator's other branch: twelve entries against a target of
	// four, so a leading segment of one populated sample is bucketed rather than passed through. It keeps
	// its own column and the anchor stays at index 1.
	[Fact]
	public void ASeedMarkedAsABreakKeepsItsOwnColumnUnderDecimation()
	{
		var rows = new List<HistoryRowFold.Row> { new(7, Local(9, 55), 1.0, LastBeforeBreakQuality) };
		rows.AddRange(Enumerable
			.Range(0, 10)
			.Select(index => new HistoryRowFold.Row(7, Local(10, index), index + 2.0, 0)));

		var envelope = HistoryRowFold.Fold(rows, _utcConverter, 4).Should().ContainSingle().Which;

		NaNColumnCount(envelope).Should().Be(1);
		envelope.Timestamps[0].Should().Be(Utc(9, 55));
		envelope.Center[0].Should().Be(1.0);
		envelope.Timestamps[1].Should().Be(Utc(9, 55).AddTicks(1));
		double.IsNaN(envelope.Center[1]).Should().BeTrue();

		// The window segment's first column carries its bucket's centre sample rather than the bucket's
		// first, so it is at or after 10:00 rather than exactly on it — the decimator's own behaviour, not
		// the seed's.
		double.IsNaN(envelope.Center[2]).Should().BeFalse();
		(envelope.Timestamps[2] >= Utc(10, 0)).Should().BeTrue();
	}
}
