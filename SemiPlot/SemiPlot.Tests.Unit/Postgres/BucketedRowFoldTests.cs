using AwesomeAssertions;

using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Unit.Postgres;

// UTC is the zone wherever the conversion is not the subject; Europe/Berlin appears only where it is.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class BucketedRowFoldTests
{
	private static readonly ArchiveTimeConverter _utcConverter = new(TimeZoneInfo.Utc);

	[Fact]
	public void NoRowsFoldIntoNoEnvelopes()
	{
		BucketedRowFold.Fold([], _utcConverter).Should().BeEmpty();
	}

	[Fact]
	public void EachBucketBecomesOneColumnInOrder()
	{
		BucketedRowFold.Row[] rows =
		[
			new(7, Local(10, 0), 1.5, 1.0, 2.0, false),
			new(7, Local(10, 1), 2.5, 2.0, 3.0, false),
			new(7, Local(10, 2), 3.5, 3.0, 4.0, false)
		];

		var envelope = BucketedRowFold.Fold(rows, _utcConverter).Should().ContainSingle().Which;

		envelope.PenId.Should().Be(7);
		envelope.Timestamps.Should().Equal([Utc(10, 0), Utc(10, 1), Utc(10, 2)]);
		envelope.Timestamps.Should().AllSatisfy(timestamp => timestamp.Kind.Should().Be(DateTimeKind.Utc));
		envelope.Min.Should().Equal([1.0, 2.0, 3.0]);
		envelope.Max.Should().Equal([2.0, 3.0, 4.0]);
		envelope.Center.Should().Equal([1.5, 2.5, 3.5]);
	}

	[Fact]
	public void ABucketEndingOnABreakGetsOneGapColumnATickLater()
	{
		BucketedRowFold.Row[] rows =
		[
			new(7, Local(10, 0), 1.0, 1.0, 1.0, true),
			new(7, Local(10, 5), 2.0, 2.0, 2.0, false)
		];

		var envelope = BucketedRowFold.Fold(rows, _utcConverter).Should().ContainSingle().Which;

		envelope.Timestamps.Should().Equal([Utc(10, 0), Utc(10, 0).AddTicks(1), Utc(10, 5)]);
		envelope.Center[0].Should().Be(1.0);
		double.IsNaN(envelope.Min[1]).Should().BeTrue();
		double.IsNaN(envelope.Max[1]).Should().BeTrue();
		double.IsNaN(envelope.Center[1]).Should().BeTrue();
		envelope.Center[2].Should().Be(2.0);
	}

	// ToUtc is not monotonic across the fall-back hour, and PenHistoryEnvelope demands strictly ascending
	// timestamps, so the second pass over the repeated hour is dropped rather than folded in.
	[Fact]
	public void ABucketWhoseConvertedTimestampDoesNotAdvanceIsDropped()
	{
		var converter = new ArchiveTimeConverter(TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));

		BucketedRowFold.Row[] rows =
		[
			new(7, FallBackLocal(2, 0), 1.0, 1.0, 1.0, false),
			new(7, FallBackLocal(2, 30), 2.0, 2.0, 2.0, false),
			new(7, FallBackLocal(2, 0), 3.0, 3.0, 3.0, false),
			new(7, FallBackLocal(3, 0), 4.0, 4.0, 4.0, false)
		];

		var envelope = BucketedRowFold.Fold(rows, converter).Should().ContainSingle().Which;

		envelope.Timestamps.Should().Equal(
			[
				new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc),
				new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc),
				new DateTime(2026, 10, 25, 2, 0, 0, DateTimeKind.Utc)
			]);

		envelope.Center.Should().Equal([1.0, 2.0, 4.0]);
	}

	[Fact]
	public void ABucketOfNullsBecomesOneGapColumnOfItsOwn()
	{
		BucketedRowFold.Row[] rows =
		[
			new(7, Local(10, 0), 1.0, 1.0, 1.0, false),
			new(7, Local(10, 1), double.NaN, double.NaN, double.NaN, false),
			new(7, Local(10, 2), 2.0, 2.0, 2.0, false)
		];

		var envelope = BucketedRowFold.Fold(rows, _utcConverter).Should().ContainSingle().Which;

		envelope.Timestamps.Should().Equal([Utc(10, 0), Utc(10, 1), Utc(10, 2)]);
		double.IsNaN(envelope.Center[1]).Should().BeTrue();
		double.IsNaN(envelope.Min[1]).Should().BeTrue();
		double.IsNaN(envelope.Max[1]).Should().BeTrue();
	}

	[Fact]
	public void ABucketHoldingANullEndsInAGapAfterItsNewestValue()
	{
		BucketedRowFold.Row[] rows =
		[
			new(7, Local(10, 0), 1.0, 1.0, 3.0, true),
			new(7, Local(10, 2), 4.0, 4.0, 4.0, false)
		];

		var envelope = BucketedRowFold.Fold(rows, _utcConverter).Should().ContainSingle().Which;

		envelope.Timestamps.Should().Equal([Utc(10, 0), Utc(10, 0).AddTicks(1), Utc(10, 2)]);
		envelope.Center[0].Should().Be(1.0);
		envelope.Max[0].Should().Be(3.0);
		double.IsNaN(envelope.Center[1]).Should().BeTrue();
		envelope.Center[2].Should().Be(4.0);
	}

	[Fact]
	public void ABreakOnAPensLastBucketStillAnchorsBeforeTheNextPen()
	{
		BucketedRowFold.Row[] rows =
		[
			new(3, Local(10, 0), 1.0, 1.0, 1.0, true),
			new(9, Local(10, 0), 2.0, 2.0, 2.0, false)
		];

		var envelopes = BucketedRowFold.Fold(rows, _utcConverter);

		envelopes.Select(envelope => envelope.PenId).Should().Equal([3, 9]);
		envelopes[0].Timestamps.Should().Equal([Utc(10, 0), Utc(10, 0).AddTicks(1)]);
		double.IsNaN(envelopes[0].Center[1]).Should().BeTrue();
		envelopes[1].Timestamps.Should().Equal([Utc(10, 0)]);
	}

	// The repeated hour already holds the same break drawn from the first pass over it.
	[Fact]
	public void ABreakOnADroppedBucketAnchorsNothing()
	{
		var converter = new ArchiveTimeConverter(TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));

		BucketedRowFold.Row[] rows =
		[
			new(7, FallBackLocal(2, 30), 1.0, 1.0, 1.0, false),
			new(7, FallBackLocal(2, 0), 2.0, 2.0, 2.0, true)
		];

		var envelope = BucketedRowFold.Fold(rows, converter).Should().ContainSingle().Which;

		envelope.Timestamps.Should().Equal([new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc)]);
		envelope.Center.Should().Equal([1.0]);
	}

	[Fact]
	public void EachPenGetsItsOwnEnvelopeLedByItsSeed()
	{
		BucketedRowFold.Row[] rows =
		[
			new(3, Local(9, 0), 1.0, 1.0, 1.0, false),
			new(3, Local(10, 0), 2.0, 1.0, 3.0, false),
			new(9, Local(9, 30), 4.0, 4.0, 4.0, false),
			new(9, Local(10, 0), 5.0, 4.0, 6.0, false)
		];

		var envelopes = BucketedRowFold.Fold(rows, _utcConverter);

		envelopes.Select(envelope => envelope.PenId).Should().Equal([3, 9]);
		envelopes[0].Timestamps.Should().Equal([Utc(9, 0), Utc(10, 0)]);
		envelopes[1].Timestamps.Should().Equal([Utc(9, 30), Utc(10, 0)]);
		envelopes[0].Center.Should().Equal([1.0, 2.0]);
		envelopes[1].Center.Should().Equal([4.0, 5.0]);
	}

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
}
