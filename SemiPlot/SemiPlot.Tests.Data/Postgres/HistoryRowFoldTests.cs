using SemiPlot.DataSource.Postgres;

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
			new(7, Local(10, 0), 1.0),
			new(7, Local(10, 1), 2.0),
			new(7, Local(10, 2), 3.0)
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
			.Select((value, index) => new HistoryRowFold.Row(7, Local(10, index), value))
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
			new(7, new DateTime(2026, 3, 29, 1, 59, 0, DateTimeKind.Unspecified), 1.0),
			new(7, new DateTime(2026, 3, 29, 2, 30, 0, DateTimeKind.Unspecified), 2.0),
			new(7, new DateTime(2026, 3, 29, 3, 0, 0, DateTimeKind.Unspecified), 3.0)
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
			new(7, FallBackLocal(1, 30), 1.0),
			new(7, FallBackLocal(2, 0), 2.0),
			new(7, FallBackLocal(2, 30), 3.0),
			new(7, FallBackLocal(2, 0), 4.0),
			new(7, FallBackLocal(2, 30), 5.0),
			new(7, FallBackLocal(3, 0), 6.0)
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
			new(3, Local(10, 0), 1.0),
			new(9, Local(10, 0), 2.0)
		];

		var envelopes = HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount);

		Assert.Equal([3L, 9L], envelopes.Select(envelope => envelope.PenId));
	}

	[Fact]
	public void ThePreviousKeptTimestampResetsAtEachPen()
	{
		HistoryRowFold.Row[] rows =
		[
			new(3, Local(10, 0), 1.0),
			new(3, Local(10, 5), 2.0),
			new(9, Local(9, 0), 3.0),
			new(9, Local(9, 5), 4.0)
		];

		var envelopes = HistoryRowFold.Fold(rows, _utcConverter, TargetColumnCount);

		Assert.Equal(2, envelopes.Count);
		Assert.Equal([Utc(10, 0), Utc(10, 5)], envelopes[0].Timestamps);
		Assert.Equal([Utc(9, 0), Utc(9, 5)], envelopes[1].Timestamps);
	}
}
