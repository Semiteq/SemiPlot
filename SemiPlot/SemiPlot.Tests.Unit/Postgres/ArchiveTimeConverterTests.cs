using AwesomeAssertions;

using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Unit.Postgres;

// Europe/Berlin, 2026 transitions: the EU rule is identical in the Windows registry and in tzdata, so
// these dates behave the same on Windows and on the ubuntu-latest runner; a historical or unusual
// zone would not.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ArchiveTimeConverterTests
{
	private static readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

	private static readonly DateTime _skippedLocal = new(2026, 3, 29, 2, 30, 0, DateTimeKind.Unspecified);

	private static readonly DateTime _ambiguousLocal = new(2026, 10, 25, 2, 30, 0, DateTimeKind.Unspecified);

	private readonly ArchiveTimeConverter _converter = new(_zone);

	[Fact]
	public void AConverterWithoutAZoneIsRejected()
	{
		Action act = () => _ = new ArchiveTimeConverter(null!);

		act.Should().Throw<ArgumentNullException>();
	}

	// Summer: the archive's wall clock runs at +02:00, so noon local is 10:00 UTC.
	[Fact]
	public void ASummerArchiveValueConvertsToTheUtcInstant()
	{
		var archiveLocal = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Unspecified);

		var utc = _converter.ToUtc(archiveLocal);

		utc.Should().Be(new DateTime(2026, 6, 15, 10, 0, 0));
		utc.Kind.Should().Be(DateTimeKind.Utc);
	}

	// Winter: standard time, +01:00.
	[Fact]
	public void AWinterArchiveValueConvertsToTheUtcInstant()
	{
		var archiveLocal = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified);

		var utc = _converter.ToUtc(archiveLocal);

		utc.Should().Be(new DateTime(2026, 1, 15, 11, 0, 0));
		utc.Kind.Should().Be(DateTimeKind.Utc);
	}

	[Theory]
	[InlineData(2026, 6, 15, 12, 0)]
	[InlineData(2026, 1, 15, 12, 0)]
	[InlineData(2026, 3, 29, 4, 0)]
	[InlineData(2026, 10, 25, 5, 0)]
	public void AnArchiveValueOutsideATransitionRoundTrips(int year, int month, int day, int hour, int minute)
	{
		var archiveLocal = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

		var back = _converter.ToArchiveLocal(_converter.ToUtc(archiveLocal));

		back.Should().Be(archiveLocal);
		back.Kind.Should().Be(DateTimeKind.Unspecified);
	}

	[Fact]
	public void AUtcWindowBoundBecomesTheNaiveLocalValueAQueryNeeds()
	{
		var bound = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

		var archiveLocal = _converter.ToArchiveLocal(bound);

		archiveLocal.Should().Be(new DateTime(2026, 6, 15, 12, 0, 0));
		archiveLocal.Kind.Should().Be(DateTimeKind.Unspecified);
		_converter.ToUtc(archiveLocal).Should().Be(bound);
	}

	// Standard time is what TimeZoneInfo resolves an ambiguous local time to, so the converter needs no
	// branch here: 02:30 on the fall-back day takes +01:00, not the +02:00 of the first pass.
	[Fact]
	public void AnAmbiguousLocalTimeResolvesToTheStandardTimeInstant()
	{
		_zone.IsAmbiguousTime(_ambiguousLocal).Should().BeTrue();

		var utc = _converter.ToUtc(_ambiguousLocal);

		utc.Should().Be(new DateTime(2026, 10, 25, 1, 30, 0));
		utc.Kind.Should().Be(DateTimeKind.Utc);
	}

	[Fact]
	public void ASkippedLocalTimeResolvesInsteadOfThrowing()
	{
		_zone.IsInvalidTime(_skippedLocal).Should().BeTrue();
		Action convert = () => TimeZoneInfo.ConvertTimeToUtc(_skippedLocal, _zone);
		convert.Should().Throw<ArgumentException>();

		var utc = _converter.ToUtc(_skippedLocal);

		utc.Should().Be(new DateTime(2026, 3, 29, 1, 30, 0));
		utc.Kind.Should().Be(DateTimeKind.Utc);
		_converter.ToArchiveLocal(utc).Should().Be(new DateTime(2026, 3, 29, 3, 30, 0));
	}

	// Every minute of the gap, not only the one the previous test pins: the whole hour is reachable input.
	[Fact]
	public void EveryMinuteOfTheSkippedHourConvertsToADistinctAscendingInstant()
	{
		var gapStart = new DateTime(2026, 3, 29, 2, 0, 0, DateTimeKind.Unspecified);

		var converted = Enumerable.Range(0, 60)
			.Select(minute => _converter.ToUtc(gapStart.AddMinutes(minute)))
			.ToArray();

		converted.Should().AllSatisfy(instant => instant.Kind.Should().Be(DateTimeKind.Utc));
		converted.Distinct().Count().Should().Be(60);
		converted.Should().Equal(converted.Order());
	}

	[Theory]
	[InlineData(DateTimeKind.Unspecified)]
	[InlineData(DateTimeKind.Utc)]
	[InlineData(DateTimeKind.Local)]
	public void TheArchiveValueIsReadAsWallClockTimeWhateverItsKind(DateTimeKind kind)
	{
		var archiveLocal = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 12, 0, 0), kind);

		var utc = _converter.ToUtc(archiveLocal);

		utc.Should().Be(new DateTime(2026, 6, 15, 10, 0, 0));
		utc.Kind.Should().Be(DateTimeKind.Utc);
	}

	// The cosmetic duplicate docs/architecture/data-integration.md:216 accepts, not a defect.
	[Fact]
	public void ASequenceSpanningTheFallBackRepeatsAnHour()
	{
		DateTime[] archiveLocal =
		[
			new(2026, 10, 25, 1, 30, 0, DateTimeKind.Unspecified),
			new(2026, 10, 25, 2, 0, 0, DateTimeKind.Unspecified),
			new(2026, 10, 25, 2, 30, 0, DateTimeKind.Unspecified),
			new(2026, 10, 25, 2, 0, 0, DateTimeKind.Unspecified),
			new(2026, 10, 25, 2, 30, 0, DateTimeKind.Unspecified),
			new(2026, 10, 25, 3, 0, 0, DateTimeKind.Unspecified)
		];

		var converted = archiveLocal.Select(_converter.ToUtc).ToArray();

		converted.Should().Equal(
			[
				new DateTime(2026, 10, 24, 23, 30, 0),
				new DateTime(2026, 10, 25, 1, 0, 0),
				new DateTime(2026, 10, 25, 1, 30, 0),
				new DateTime(2026, 10, 25, 1, 0, 0),
				new DateTime(2026, 10, 25, 1, 30, 0),
				new DateTime(2026, 10, 25, 2, 0, 0)
			]);

		converted.Distinct().Count().Should().Be(4);
		converted[3].Should().Be(converted[1]);
		converted[4].Should().Be(converted[2]);
	}

	// Ordering across the gap is the envelope assembler's, not the converter's (data-integration.md:57).
	[Fact]
	public void AnAscendingSequenceSpanningTheSpringForwardGapDescends()
	{
		DateTime[] archiveLocal =
		[
			new(2026, 3, 29, 1, 30, 0, DateTimeKind.Unspecified),
			new(2026, 3, 29, 2, 30, 0, DateTimeKind.Unspecified),
			new(2026, 3, 29, 3, 0, 0, DateTimeKind.Unspecified)
		];

		var converted = archiveLocal.Select(_converter.ToUtc).ToArray();

		converted.Should().Equal(
			[
				new DateTime(2026, 3, 29, 0, 30, 0),
				new DateTime(2026, 3, 29, 1, 30, 0),
				new DateTime(2026, 3, 29, 1, 0, 0)
			]);

		(converted[2] < converted[1]).Should().BeTrue();
		converted.Should().NotEqual(converted.Order());
	}

	[Fact]
	public void AUtcWindowOverTheFallBackCollapsesToAZeroWidthLocalWindow()
	{
		var fromUtc = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
		var toUtc = new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc);

		var fromLocal = _converter.ToArchiveLocal(fromUtc);
		var toLocal = _converter.ToArchiveLocal(toUtc);

		fromLocal.Should().Be(new DateTime(2026, 10, 25, 2, 0, 0));
		toLocal.Should().Be(new DateTime(2026, 10, 25, 2, 0, 0));
		(toLocal - fromLocal).Should().Be(TimeSpan.Zero);
	}

	[Theory]
	[InlineData(DateTimeKind.Unspecified)]
	[InlineData(DateTimeKind.Utc)]
	[InlineData(DateTimeKind.Local)]
	public void TheWindowBoundIsReadAsAnInstantWhateverItsKind(DateTimeKind kind)
	{
		var bound = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 10, 0, 0), kind);

		var archiveLocal = _converter.ToArchiveLocal(bound);

		archiveLocal.Should().Be(new DateTime(2026, 6, 15, 12, 0, 0));
		archiveLocal.Kind.Should().Be(DateTimeKind.Unspecified);
	}
}
