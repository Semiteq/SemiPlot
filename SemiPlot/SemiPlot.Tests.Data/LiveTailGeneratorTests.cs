using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data;

// One follow tick's rows. The property that carries the whole design is that a row belongs to exactly
// one span: a follow run keeps no state between ticks, and PRIMARY KEY (id, l, t) refuses a second copy
// of a row an earlier tick already wrote.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class LiveTailGeneratorTests
{
	private const string Connection = "Host=localhost;Database=archive;Username=scada_writer";

	private static readonly DateTime _midnight = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified);

	[Fact]
	public void EveryRowIsRawAndOrdinary()
	{
		var rows = LiveTailGenerator.Generate(Options(), _midnight, _midnight.AddSeconds(30));

		Assert.NotEmpty(rows);
		Assert.All(rows, row => Assert.Equal(ArchiveRow.RawLayer, row.Layer));
		Assert.All(rows, row => Assert.Equal(ArchiveRow.OrdinaryQuality, row.Quality));
	}

	[Fact]
	public void TimestampsAscendStrictlyPerPenOnWholeMilliseconds()
	{
		var from = _midnight.AddMilliseconds(137.0);
		var rows = LiveTailGenerator.Generate(Options(), from, from.AddSeconds(30));

		Assert.All(rows, row => Assert.Equal(0L, row.Timestamp.Ticks % TimeSpan.TicksPerMillisecond));

		foreach (var pen in rows.GroupBy(row => row.Id))
		{
			var timestamps = pen.Select(row => row.Timestamp).ToArray();

			Assert.Equal(timestamps.OrderBy(timestamp => timestamp).ToArray(), timestamps);
			Assert.Equal(timestamps.Distinct().Count(), timestamps.Length);
		}
	}

	[Fact]
	public void EveryRowFallsInsideItsOwnSpan()
	{
		var from = _midnight.AddMilliseconds(137.0);
		var toExclusive = from.AddSeconds(30);

		var rows = LiveTailGenerator.Generate(Options(), from, toExclusive);

		Assert.All(rows, row => Assert.InRange(row.Timestamp, from, toExclusive.AddTicks(-1)));
	}

	// The span is half-open at both ends the same way: closed at the start, open at the end. The tick loop
	// hands the previous tick's own instant back as the next span's start, and the span before it stopped
	// short of that instant, so a lattice point landing exactly there belongs to this span and to no other.
	// It is also why a follow run resuming from the archive's edge advances past that edge before the first
	// call rather than making this end exclusive — see StaleArchiveGuard.StartFrom.
	[Fact]
	public void ARowExactlyAtTheSpanStartBelongsToTheSpan()
	{
		var options = Options() with { PenCount = 1, ChangeSeconds = 0.5 };
		var onTheLattice = _midnight.AddSeconds(9.5);

		var rows = LiveTailGenerator.Generate(options, onTheLattice, onTheLattice.AddSeconds(1));

		Assert.Equal(onTheLattice, rows[0].Timestamp);
	}

	// The property a follow run stands on: consecutive spans partition the rows rather than overlapping
	// them, so the second tick's COPY meets none of the first tick's keys.
	[Fact]
	public void TwoConsecutiveSpansShareNoRow()
	{
		var first = _midnight.AddMilliseconds(137.0);
		var second = first.AddSeconds(7);
		var third = second.AddSeconds(11);

		var options = Options();
		var earlier = LiveTailGenerator.Generate(options, first, second);
		var later = LiveTailGenerator.Generate(options, second, third);

		Assert.NotEmpty(earlier);
		Assert.NotEmpty(later);

		var keys = earlier.Concat(later).Select(row => (row.Id, row.Layer, row.Timestamp)).ToArray();

		Assert.Equal(keys.Distinct().Count(), keys.Length);
	}

	// The vendor's two-rows-per-change shape: the previous value one poll interval before the change,
	// then the new value at the change tick. The very first change of a follow run has no anchor of its
	// own, because that row belongs to the span before the run started.
	[Fact]
	public void AChangeCarriesThePreviousValueOnePollIntervalAhead()
	{
		var options = Options() with { PenCount = 1 };
		var rows = LiveTailGenerator.Generate(options, _midnight, _midnight.AddSeconds(2));

		DateTime[] expected =
		[
			_midnight,
			_midnight.AddMilliseconds(900.0),
			_midnight.AddSeconds(1),
			_midnight.AddMilliseconds(1900.0)
		];

		Assert.Equal(expected, rows.Select(row => row.Timestamp).ToArray());

		Assert.Equal(rows[0].Value, rows[1].Value);
		Assert.Equal(rows[2].Value, rows[3].Value);
		Assert.NotEqual(rows[1].Value, rows[2].Value);
	}

	[Fact]
	public void TheSameSpanGeneratesTheSameRowsTwice()
	{
		var toExclusive = _midnight.AddSeconds(20);

		Assert.Equal(
			LiveTailGenerator.Generate(Options(), _midnight, toExclusive),
			LiveTailGenerator.Generate(Options(), _midnight, toExclusive));
	}

	[Fact]
	public void ThePensAreTheOnesTheSeederSelects()
	{
		var options = Options();
		var rows = LiveTailGenerator.Generate(options, _midnight, _midnight.AddSeconds(30));

		Assert.Equal(
			RawLayerGenerator.SelectPens(options.PenCount).Select(pen => (int)pen.PenId).Order().ToArray(),
			rows.Select(row => row.Id).Distinct().Order().ToArray());
	}

	[Fact]
	public void EveryValueStaysInsideItsPenRange()
	{
		var options = Options();
		var pens = RawLayerGenerator.SelectPens(options.PenCount).ToDictionary(pen => (int)pen.PenId);
		var rows = LiveTailGenerator.Generate(options, _midnight, _midnight.AddSeconds(30));

		Assert.All(rows, row => Assert.InRange(row.Value, pens[row.Id].MinValue, pens[row.Id].MaxValue));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	public void ASpanThatDoesNotAdvanceEmitsNothing(int seconds)
	{
		Assert.Empty(LiveTailGenerator.Generate(Options(), _midnight, _midnight.AddSeconds(seconds)));
	}

	// A change interval no longer than the poll interval leaves no room for the anchor, and the archive
	// holds none there either; the rows stay one per change rather than colliding on (id, l, t).
	[Fact]
	public void AChangeIntervalAtThePollIntervalEmitsNoAnchor()
	{
		var options = Options() with { PenCount = 1, ChangeSeconds = 0.1 };
		var rows = LiveTailGenerator.Generate(options, _midnight, _midnight.AddMilliseconds(500.0));

		DateTime[] expected =
		[
			_midnight,
			_midnight.AddMilliseconds(100.0),
			_midnight.AddMilliseconds(200.0),
			_midnight.AddMilliseconds(300.0),
			_midnight.AddMilliseconds(400.0)
		];

		Assert.Equal(expected, rows.Select(row => row.Timestamp).ToArray());
	}

	private static FollowOptions Options()
	{
		return new(Connection, TimeSpan.FromSeconds(1), 8, 1L, 1.0);
	}
}
