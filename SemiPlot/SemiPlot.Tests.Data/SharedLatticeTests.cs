using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data;

// The guarantee merging the two generators buys: a seeding run and a follow run write on one lattice by
// construction rather than by agreement. Nothing else compares the two — LiveTailGeneratorTests reads the
// follow path alone, FollowRestartTests follows on both sides of the restart, and RawLayerGeneratorTests
// reads the seeding path alone — so re-splitting them leaves every suite green until a seeded archive is followed and
// the COPY meets a key it already holds. That is the collision f91889d fixed and the seam hole caa935f
// fixed, and this is where a second lattice goes red.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class SharedLatticeTests
{
	private const int PenCount = 3;

	// A minute between changes, not the standard slice's five seconds: the comparison covers a whole day
	// row for row, and the lattice it asserts on is the same one at any interval.
	private const double ChangeSeconds = 60.0;

	// No break, so the seeding run is one archiving window carrying no marker pair. That is what makes it
	// comparable row for row against a follow tick over the same span.
	private static readonly SeederOptions _seeding =
		BenchOptions.For(pens: PenCount, changeSeconds: ChangeSeconds, breaks: 0);

	private static readonly FollowOptions _following = new(
		BenchOptions.ConnectionString,
		TimeSpan.FromSeconds(1),
		PenCount,
		SeederOptions.DefaultSeed,
		ChangeSeconds);

	// A restart by hand, inside StaleArchiveGuard.MaximumAge of the edge the seeding run left.
	private static readonly DateTime _restartClock = BenchOptions.End.AddMinutes(3);

	private static readonly TimeSpan _changeInterval = TimeSpan.FromSeconds(ChangeSeconds);

	// The seeding span is closed at its start and open at its end, the follow window the other way round;
	// one tick back on both bounds is the exact conversion, since every row is a whole millisecond.
	[Fact]
	public void ASeedingRunAndAFollowTickEmitTheSameRowsOverTheSameSpan()
	{
		var seeded = RawLayerGenerator.Generate(_seeding);
		var followed = LiveTailGenerator.Generate(
			_following, _seeding.Start.AddTicks(-1), _seeding.End.AddTicks(-1));

		Assert.NotEmpty(seeded);
		Assert.Equal(seeded, followed);
	}

	// The operator's sequence no other test performs: seed, then follow from the archive's own edge. A
	// lattice of its own on either side either regenerates the edge row, which PRIMARY KEY (id, l, t)
	// refuses, or steps clear of it and leaves a hole no marker in the archive explains.
	[Fact]
	public void AFollowRunResumingFromASeededEdgeRepeatsNoRowAndLeavesNoHole()
	{
		var seeded = RawLayerGenerator.Generate(_seeding);
		var edges = seeded.GroupBy(row => row.Id).ToDictionary(pen => pen.Key, pen => pen.Max(row => row.Timestamp));

		var followed = LiveTailGenerator.Generate(_following, edges.Values.Max(), _restartClock);

		Assert.NotEmpty(followed);

		var keys = seeded.Concat(followed).Select(row => (row.Id, row.Layer, row.Timestamp)).ToArray();

		Assert.Equal(keys.Distinct().Count(), keys.Length);
		Assert.Equal(edges.Keys.Order(), followed.Select(row => row.Id).Distinct().Order());

		// The lattice is absolute (docs/architecture/bench.md): a change sits at index * interval from tick
		// zero and its anchor one poll interval ahead of that. A lattice drawn from a run's own start passes
		// the two checks above — its rows simply miss the seeded ones — and fails here.
		var interval = _changeInterval.Ticks;
		var anchorOffset = RawLayerGenerator.PollInterval.Ticks;

		foreach (var row in seeded.Concat(followed))
		{
			var offset = row.Timestamp.Ticks % interval;

			Assert.True(
				offset == 0L || offset == interval - anchorOffset,
				$"{row.Timestamp:O} sits off the absolute lattice.");
		}

		foreach (var pen in followed.GroupBy(row => row.Id))
		{
			var seam = pen.Min(row => row.Timestamp) - edges[pen.Key];

			Assert.InRange(seam, TimeSpan.FromMilliseconds(1.0), _changeInterval);
		}
	}
}
