using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data;

// One follow tick's rows. The property that carries the whole design is that a row belongs to exactly
// one window: a follow run keeps no state between ticks, and PRIMARY KEY (id, l, t) refuses a second copy
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
		var after = _midnight.AddMilliseconds(137.0);
		var rows = LiveTailGenerator.Generate(Options(), after, after.AddSeconds(30));

		Assert.All(rows, row => Assert.Equal(0L, row.Timestamp.Ticks % TimeSpan.TicksPerMillisecond));

		foreach (var pen in rows.GroupBy(row => row.Id))
		{
			var timestamps = pen.Select(row => row.Timestamp).ToArray();

			Assert.Equal(timestamps.OrderBy(timestamp => timestamp).ToArray(), timestamps);
			Assert.Equal(timestamps.Distinct().Count(), timestamps.Length);
		}
	}

	[Fact]
	public void EveryRowFallsInsideItsOwnWindow()
	{
		var after = _midnight.AddMilliseconds(137.0);
		var to = after.AddSeconds(30);

		var rows = LiveTailGenerator.Generate(Options(), after, to);

		Assert.All(rows, row => Assert.InRange(row.Timestamp, after.AddTicks(1), to));
	}

	// The window is open at its start: `after` is an instant the archive already accounts for — the newest
	// row a stopped run left, which a restart hands in, or the previous tick's own instant, whose rows that
	// tick wrote — so a lattice point sitting exactly there is never written twice.
	[Fact]
	public void ARowExactlyAtTheWindowStartIsNotEmitted()
	{
		var options = Options() with { PenCount = 1, ChangeSeconds = 0.5 };
		var onTheLattice = _midnight.AddSeconds(9.5);

		var rows = LiveTailGenerator.Generate(options, onTheLattice, onTheLattice.AddSeconds(1));

		Assert.DoesNotContain(rows, row => row.Timestamp == onTheLattice);
		Assert.Equal(onTheLattice.AddMilliseconds(400.0), rows[0].Timestamp);
	}

	// The window is closed at its end, so the row a tick's own instant lands on belongs to that tick, and
	// the next tick, which starts after that instant, leaves it alone.
	[Fact]
	public void ARowExactlyAtTheWindowEndIsEmitted()
	{
		var options = Options() with { PenCount = 1, ChangeSeconds = 0.5 };
		var onTheLattice = _midnight.AddSeconds(9.5);

		var rows = LiveTailGenerator.Generate(options, onTheLattice.AddSeconds(-1), onTheLattice);

		Assert.Equal(onTheLattice, rows[^1].Timestamp);
	}

	// The property a follow run stands on: consecutive windows partition the rows rather than overlapping
	// them, so the second tick's COPY meets none of the first tick's keys.
	[Fact]
	public void TwoConsecutiveWindowsShareNoRow()
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
	// then the new value at the change tick. The change sitting on `after` itself is the archive's already;
	// what the window opens with is that change's successor's anchor, carrying the value the edge row holds.
	[Fact]
	public void AChangeCarriesThePreviousValueOnePollIntervalAhead()
	{
		var options = Options() with { PenCount = 1 };
		var rows = LiveTailGenerator.Generate(options, _midnight, _midnight.AddSeconds(2));

		DateTime[] expected =
		[
			_midnight.AddMilliseconds(900.0),
			_midnight.AddSeconds(1),
			_midnight.AddMilliseconds(1900.0),
			_midnight.AddSeconds(2)
		];

		Assert.Equal(expected, rows.Select(row => row.Timestamp).ToArray());

		Assert.Equal(rows[1].Value, rows[2].Value);
		Assert.NotEqual(rows[0].Value, rows[1].Value);
		Assert.NotEqual(rows[2].Value, rows[3].Value);
	}

	[Fact]
	public void TheSameWindowGeneratesTheSameRowsTwice()
	{
		var to = _midnight.AddSeconds(20);

		Assert.Equal(
			LiveTailGenerator.Generate(Options(), _midnight, to),
			LiveTailGenerator.Generate(Options(), _midnight, to));
	}

	[Fact]
	public void ThePensAreTheOnesTheSeederSelects()
	{
		var options = Options();
		var rows = LiveTailGenerator.Generate(options, _midnight, _midnight.AddSeconds(30));

		Assert.Equal(
			RawLayerGenerator.SelectPens(options.PenCount).Select(pen => pen.PenId).Order().ToArray(),
			rows.Select(row => row.Id).Distinct().Order().ToArray());
	}

	[Fact]
	public void EveryValueStaysInsideItsPenRange()
	{
		var options = Options();
		var pens = RawLayerGenerator.SelectPens(options.PenCount).ToDictionary(pen => pen.PenId);
		var rows = LiveTailGenerator.Generate(options, _midnight, _midnight.AddSeconds(30));

		Assert.All(rows, row => Assert.InRange(row.Value, pens[row.Id].MinValue, pens[row.Id].MaxValue));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	public void AWindowThatDoesNotAdvanceEmitsNothing(int seconds)
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
			_midnight.AddMilliseconds(100.0),
			_midnight.AddMilliseconds(200.0),
			_midnight.AddMilliseconds(300.0),
			_midnight.AddMilliseconds(400.0),
			_midnight.AddMilliseconds(500.0)
		];

		Assert.Equal(expected, rows.Select(row => row.Timestamp).ToArray());
	}

	private static FollowOptions Options()
	{
		return new(Connection, TimeSpan.FromSeconds(1), 8, 1L, 1.0);
	}
}
