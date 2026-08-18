using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

// Europe/Berlin with 2026 transitions: the EU rule — last Sunday of March and of October, both at
// 01:00 UTC — is identical in the Windows registry and in tzdata, so these dates behave the same on a
// developer's Windows machine and on the ubuntu-latest data-tests runner. A historical date or an
// unusual zone would become a defect visible on one platform only.
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
		Assert.Throws<ArgumentNullException>(() => new ArchiveTimeConverter(null!));
	}

	// Summer: the archive's wall clock runs at +02:00, so noon local is 10:00 UTC.
	[Fact]
	public void ASummerArchiveValueConvertsToTheUtcInstant()
	{
		var archiveLocal = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Unspecified);

		var utc = _converter.ToUtc(archiveLocal);

		Assert.Equal(new DateTime(2026, 6, 15, 10, 0, 0), utc);
		Assert.Equal(DateTimeKind.Utc, utc.Kind);
	}

	// Winter: standard time, +01:00.
	[Fact]
	public void AWinterArchiveValueConvertsToTheUtcInstant()
	{
		var archiveLocal = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified);

		var utc = _converter.ToUtc(archiveLocal);

		Assert.Equal(new DateTime(2026, 1, 15, 11, 0, 0), utc);
		Assert.Equal(DateTimeKind.Utc, utc.Kind);
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

		Assert.Equal(archiveLocal, back);
		Assert.Equal(DateTimeKind.Unspecified, back.Kind);
	}

	[Fact]
	public void AUtcWindowBoundBecomesTheNaiveLocalValueAQueryNeeds()
	{
		var bound = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

		var archiveLocal = _converter.ToArchiveLocal(bound);

		Assert.Equal(new DateTime(2026, 6, 15, 12, 0, 0), archiveLocal);
		Assert.Equal(DateTimeKind.Unspecified, archiveLocal.Kind);
		Assert.Equal(bound, _converter.ToUtc(archiveLocal));
	}

	// Standard time is what TimeZoneInfo resolves an ambiguous local time to, so the converter needs no
	// branch here: 02:30 on the fall-back day takes +01:00, not the +02:00 of the first pass.
	[Fact]
	public void AnAmbiguousLocalTimeResolvesToTheStandardTimeInstant()
	{
		Assert.True(_zone.IsAmbiguousTime(_ambiguousLocal));

		var utc = _converter.ToUtc(_ambiguousLocal);

		Assert.Equal(new DateTime(2026, 10, 25, 1, 30, 0), utc);
		Assert.Equal(DateTimeKind.Utc, utc.Kind);
	}

	[Fact]
	public void ASkippedLocalTimeResolvesInsteadOfThrowing()
	{
		Assert.True(_zone.IsInvalidTime(_skippedLocal));
		Assert.Throws<ArgumentException>(() => TimeZoneInfo.ConvertTimeToUtc(_skippedLocal, _zone));

		var utc = _converter.ToUtc(_skippedLocal);

		Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0), utc);
		Assert.Equal(DateTimeKind.Utc, utc.Kind);
		Assert.Equal(new DateTime(2026, 3, 29, 3, 30, 0), _converter.ToArchiveLocal(utc));
	}

	// Every minute of the gap, not only the one the previous test pins: the whole hour is reachable input.
	[Fact]
	public void EveryMinuteOfTheSkippedHourConvertsToADistinctAscendingInstant()
	{
		var gapStart = new DateTime(2026, 3, 29, 2, 0, 0, DateTimeKind.Unspecified);

		var converted = Enumerable.Range(0, 60)
			.Select(minute => _converter.ToUtc(gapStart.AddMinutes(minute)))
			.ToArray();

		Assert.All(converted, instant => Assert.Equal(DateTimeKind.Utc, instant.Kind));
		Assert.Equal(60, converted.Distinct().Count());
		Assert.Equal(converted.Order(), converted);
	}

	[Theory]
	[InlineData(DateTimeKind.Unspecified)]
	[InlineData(DateTimeKind.Utc)]
	[InlineData(DateTimeKind.Local)]
	public void TheArchiveValueIsReadAsWallClockTimeWhateverItsKind(DateTimeKind kind)
	{
		var archiveLocal = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 12, 0, 0), kind);

		var utc = _converter.ToUtc(archiveLocal);

		Assert.Equal(new DateTime(2026, 6, 15, 10, 0, 0), utc);
		Assert.Equal(DateTimeKind.Utc, utc.Kind);
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

		Assert.Equal(
			[
				new DateTime(2026, 10, 24, 23, 30, 0),
				new DateTime(2026, 10, 25, 1, 0, 0),
				new DateTime(2026, 10, 25, 1, 30, 0),
				new DateTime(2026, 10, 25, 1, 0, 0),
				new DateTime(2026, 10, 25, 1, 30, 0),
				new DateTime(2026, 10, 25, 2, 0, 0)
			],
			converted);

		Assert.Equal(4, converted.Distinct().Count());
		Assert.Equal(converted[1], converted[3]);
		Assert.Equal(converted[2], converted[4]);
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

		Assert.Equal(
			[
				new DateTime(2026, 3, 29, 0, 30, 0),
				new DateTime(2026, 3, 29, 1, 30, 0),
				new DateTime(2026, 3, 29, 1, 0, 0)
			],
			converted);

		Assert.True(converted[2] < converted[1]);
		Assert.NotEqual(converted.Order(), converted);
	}

	[Fact]
	public void AUtcWindowOverTheFallBackCollapsesToAZeroWidthLocalWindow()
	{
		var fromUtc = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
		var toUtc = new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc);

		var fromLocal = _converter.ToArchiveLocal(fromUtc);
		var toLocal = _converter.ToArchiveLocal(toUtc);

		Assert.Equal(new DateTime(2026, 10, 25, 2, 0, 0), fromLocal);
		Assert.Equal(new DateTime(2026, 10, 25, 2, 0, 0), toLocal);
		Assert.Equal(TimeSpan.Zero, toLocal - fromLocal);
	}

	[Theory]
	[InlineData(DateTimeKind.Unspecified)]
	[InlineData(DateTimeKind.Utc)]
	[InlineData(DateTimeKind.Local)]
	public void TheWindowBoundIsReadAsAnInstantWhateverItsKind(DateTimeKind kind)
	{
		var bound = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 10, 0, 0), kind);

		var archiveLocal = _converter.ToArchiveLocal(bound);

		Assert.Equal(new DateTime(2026, 6, 15, 12, 0, 0), archiveLocal);
		Assert.Equal(DateTimeKind.Unspecified, archiveLocal.Kind);
	}
}
