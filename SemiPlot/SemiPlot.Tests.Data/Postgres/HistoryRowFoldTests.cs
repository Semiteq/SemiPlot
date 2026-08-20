using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

// The fold takes materialised rows, so nothing here opens a connection. UTC is the zone wherever the
// conversion is not the subject, which keeps the expected instants readable; Europe/Berlin appears only
// where the conversion itself is what the test is about.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class HistoryRowFoldTests
{
	private const int TargetColumnCount = 100;

	// The archive's break marks come from ArchiveRow rather than from a copy of the two literals: the
	// seeder writes the same marks this fold reads, the reference is already in this project, and a third
	// private copy would let the two drift. HistoryRowFold keeps a private const of its own only because
	// SemiPlot.DataSource.Postgres must not reference the seeder.
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

		Assert.Empty(envelopes);
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

		var envelope = Assert.Single(HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount));

		Assert.Equal(7L, envelope.PenId);
		Assert.Equal([Utc(10, 0), Utc(10, 1), Utc(10, 2)], envelope.Timestamps);
		Assert.All(envelope.Timestamps, timestamp => Assert.Equal(DateTimeKind.Utc, timestamp.Kind));
		Assert.Equal([1.0, 2.0, 3.0], envelope.Min);
		Assert.Equal([1.0, 2.0, 3.0], envelope.Max);
		Assert.Equal([1.0, 2.0, 3.0], envelope.Center);
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

		var envelope = Assert.Single(HistoryRowFold.Fold(rows, _utcConverter, 2));

		Assert.Equal([Utc(10, 2), Utc(10, 7)], envelope.Timestamps);
		Assert.Equal([0.0, 3.0], envelope.Min);
		Assert.Equal([6.0, 9.0], envelope.Max);
		Assert.Equal([1.0, 8.0], envelope.Center);
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

		var envelope = Assert.Single(HistoryRowFold.Fold(rows, converter, TargetColumnCount));

		Assert.Equal(
			[
				new DateTime(2026, 3, 29, 0, 59, 0, DateTimeKind.Utc),
				new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc)
			],
			envelope.Timestamps);
		Assert.Equal([1.0, 2.0], envelope.Center);
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

		var envelope = Assert.Single(HistoryRowFold.Fold(rows, converter, TargetColumnCount));

		Assert.Equal(
			[
				new DateTime(2026, 10, 24, 23, 30, 0, DateTimeKind.Utc),
				new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc),
				new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc),
				new DateTime(2026, 10, 25, 2, 0, 0, DateTimeKind.Utc)
			],
			envelope.Timestamps);

		// The values of the second pass, 4.0 and 5.0, are the hour of real archive rows this costs.
		Assert.Equal([1.0, 2.0, 3.0, 6.0], envelope.Center);
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

		Assert.Equal([3L, 9L], envelopes.Select(envelope => envelope.PenId));
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

		Assert.Equal(2, envelopes.Count);
		Assert.Equal([Utc(10, 0), Utc(10, 5)], envelopes[0].Timestamps);
		Assert.Equal([Utc(9, 0), Utc(9, 5)], envelopes[1].Timestamps);
	}

	// The gap tests count columns rather than search for one NaN. Two plausible-but-wrong folds both
	// produce a NaN between the markers: one that replaces the q = 32 row's value with a null instead of
	// appending after it, losing a real sample and stopping the line one poll early, and one that anchors
	// after q = 16 as well, re-breaking every resumption. Only the column count and the marker rows' own
	// values separate them from the contract.
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
		var envelope = Assert.Single(HistoryRowFold.Fold(BreakPairRows(), _utcConverter, TargetColumnCount));

		// Four rows and one anchor. A fold that nulls the marker instead of appending after it gives four
		// columns; a fold that anchors after q = 16 as well gives six and two NaN columns.
		Assert.Equal(5, envelope.Timestamps.Count);
		Assert.Equal(1, NaNColumnCount(envelope));
		Assert.Equal(
			[Utc(10, 0), Utc(10, 1), Utc(10, 1).AddTicks(1), Utc(10, 5), Utc(10, 6)],
			envelope.Timestamps);
		Assert.Equal([1.0, 2.0, double.NaN, 3.0, 4.0], envelope.Center);
	}

	[Fact]
	public void TheBreakMarkersOwnValueSurvivesAsTheColumnBeforeTheAnchor()
	{
		var envelope = Assert.Single(HistoryRowFold.Fold(BreakPairRows(), _utcConverter, TargetColumnCount));

		var anchorIndex = Assert.Single(
			Enumerable.Range(0, envelope.Center.Count),
			index => double.IsNaN(envelope.Center[index]));

		Assert.Equal(Utc(10, 1), envelope.Timestamps[anchorIndex - 1]);
		Assert.Equal(2.0, envelope.Center[anchorIndex - 1]);
		Assert.Equal(2.0, envelope.Min[anchorIndex - 1]);
		Assert.Equal(2.0, envelope.Max[anchorIndex - 1]);
		Assert.Equal(Utc(10, 1).AddTicks(1), envelope.Timestamps[anchorIndex]);
	}

	[Fact]
	public void TheResumptionMarkerIsARealColumnWithNoAnchorAfterIt()
	{
		var envelope = Assert.Single(HistoryRowFold.Fold(BreakPairRows(), _utcConverter, TargetColumnCount));

		var resumptionIndex = envelope.Timestamps.ToList().IndexOf(Utc(10, 5));

		Assert.Equal(3.0, envelope.Center[resumptionIndex]);
		Assert.Equal(3.0, envelope.Min[resumptionIndex]);
		Assert.Equal(3.0, envelope.Max[resumptionIndex]);

		// Everything from the resumption onwards is a real column, so the line restarts and stays.
		Assert.All(
			envelope.Center.Skip(resumptionIndex),
			value => Assert.False(double.IsNaN(value)));
		Assert.Equal(envelope.Timestamps.Count - 1, resumptionIndex + 1);
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

		var envelope = Assert.Single(HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount));

		Assert.Equal(3, envelope.Timestamps.Count);
		Assert.Equal(0, NaNColumnCount(envelope));
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

		var envelope = Assert.Single(HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount));

		Assert.Equal(7, envelope.Timestamps.Count);
		Assert.Equal(2, NaNColumnCount(envelope));
		Assert.Equal(
			[
				Utc(10, 0),
				Utc(10, 1),
				Utc(10, 1).AddTicks(1),
				Utc(10, 5),
				Utc(10, 6),
				Utc(10, 6).AddTicks(1),
				Utc(10, 9)
			],
			envelope.Timestamps);
		Assert.Equal([1.0, 2.0, double.NaN, 3.0, 4.0, double.NaN, 5.0], envelope.Center);
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

		var envelope = Assert.Single(HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount));

		Assert.Equal(3, envelope.Timestamps.Count);
		Assert.Equal(1, NaNColumnCount(envelope));
		Assert.Equal(Utc(10, 1).AddTicks(1), envelope.Timestamps[^1]);
		Assert.Equal([1.0, 2.0, double.NaN], envelope.Center);
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

		var envelope = Assert.Single(HistoryRowFold.Fold(rows, converter, TargetColumnCount));

		Assert.Equal(0, NaNColumnCount(envelope));
		Assert.Equal(
			[
				new DateTime(2026, 10, 24, 23, 30, 0, DateTimeKind.Utc),
				new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc),
				new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc),
				new DateTime(2026, 10, 25, 2, 0, 0, DateTimeKind.Utc)
			],
			envelope.Timestamps);
		Assert.Equal([1.0, 2.0, 3.0, 5.0], envelope.Center);
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

		var envelope = Assert.Single(HistoryRowFold.Fold(rows, _utcConverter, 4));

		Assert.Equal(1, NaNColumnCount(envelope));
		Assert.Equal(5, envelope.Timestamps.Count);
		Assert.Equal(Utc(10, 4).AddTicks(1), envelope.Timestamps[2]);
		Assert.True(double.IsNaN(envelope.Center[2]));
	}

	// The windowed read is a UNION ALL of a seed branch and a window branch under one outer ORDER BY id, t,
	// so a seeded pen reaches the fold as one ascending run with the seed at its head. Nothing in the fold
	// distinguishes the seed from a window row, and these tests pin that: the seed is folded like any other
	// row, and one pen still yields one envelope (Evidence 7).
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

		var envelope = Assert.Single(HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount));

		Assert.Equal(7L, envelope.PenId);
		Assert.Equal(Utc(9, 55), envelope.Timestamps[0]);
		Assert.Equal(1.0, envelope.Center[0]);
		Assert.Equal([Utc(9, 55), Utc(10, 0), Utc(10, 1)], envelope.Timestamps);
		Assert.Equal([1.0, 2.0, 3.0], envelope.Center);
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

		Assert.Equal(2, envelopes.Count);
		Assert.Equal([3L, 9L], envelopes.Select(envelope => envelope.PenId));
		Assert.Equal([Utc(9, 50), Utc(10, 0)], envelopes[0].Timestamps);
		Assert.Equal([Utc(9, 40), Utc(10, 0)], envelopes[1].Timestamps);
		Assert.Equal([1.0, 2.0], envelopes[0].Center);
		Assert.Equal([3.0, 4.0], envelopes[1].Center);
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

		var envelope = Assert.Single(HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount));

		Assert.Equal(1, NaNColumnCount(envelope));
		Assert.Equal(
			[Utc(9, 55), Utc(9, 55).AddTicks(1), Utc(10, 0), Utc(10, 1)],
			envelope.Timestamps);
		Assert.Equal([1.0, double.NaN, 2.0, 3.0], envelope.Center);
		Assert.True(double.IsNaN(envelope.Center[1]));
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

		var envelope = Assert.Single(HistoryRowFold.Fold(rows, _utcConverter, 4));

		Assert.Equal(1, NaNColumnCount(envelope));
		Assert.Equal(Utc(9, 55), envelope.Timestamps[0]);
		Assert.Equal(1.0, envelope.Center[0]);
		Assert.Equal(Utc(9, 55).AddTicks(1), envelope.Timestamps[1]);
		Assert.True(double.IsNaN(envelope.Center[1]));

		// The window segment's first column carries its bucket's centre sample rather than the bucket's
		// first, so it is at or after 10:00 rather than exactly on it — the decimator's own behaviour, not
		// the seed's.
		Assert.False(double.IsNaN(envelope.Center[2]));
		Assert.True(envelope.Timestamps[2] >= Utc(10, 0));
	}
}
