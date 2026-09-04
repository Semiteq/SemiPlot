using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Unit.Postgres;

// The bound and the merge take materialised rows, so nothing here opens a connection. Every timestamp is
// the archive's own naive local wall clock, which is the side the bound is computed on.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class FreshTailBoundTests
{
	private static readonly DateTime _windowStart = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Unspecified);

	private static readonly DateTime _windowEnd = _windowStart.AddHours(1);

	private static readonly int[] _penIds = [1, 2];

	// Restated here rather than read off AggregationLayerExtensions.ToPointSpacing: an expectation computed
	// through the call the function under test makes would pass whatever that call became.
	private const int MinuteSpacingSeconds = 15;

	private const int HourSpacingSeconds = 15 * 60;

	private const int DaySpacingSeconds = 6 * 60 * 60;

	// Raw is what a tail is read from, so a raw window has nothing coarser to be short of and issues the
	// one read it always did.
	[Fact]
	public void NoTailIsReadAtTheRawLayer()
	{
		var seams = FreshTail.Seams([], _penIds, _windowStart);

		FreshTail.Start(AggregationLayer.Raw, seams, _windowEnd).Should().BeNull();
	}

	[Fact]
	public void NoTailIsReadWhenNoPenWasRequested()
	{
		var seams = FreshTail.Seams([], [], _windowStart);

		FreshTail.Start(AggregationLayer.Minute, seams, _windowEnd).Should().BeNull();
	}

	// A layer whose newest row is inside one of its own points is not short of anything a reader can see.
	[Theory]
	[InlineData(AggregationLayer.Minute, MinuteSpacingSeconds)]
	[InlineData(AggregationLayer.Hour, HourSpacingSeconds)]
	[InlineData(AggregationLayer.Day, DaySpacingSeconds)]
	public void NoTailIsReadWhenEveryPenReachesWithinOnePointSpacingOfTheWindowEnd(
		AggregationLayer layer,
		int spacingSeconds)
	{
		var seams = SeamsAt(_windowEnd - TimeSpan.FromSeconds(spacingSeconds));

		FreshTail.Start(layer, seams, _windowEnd).Should().BeNull();
	}

	// The threshold is the layer's own spacing and the comparison is strict, so a deficit of exactly one
	// spacing still costs no round trip and one tick more starts one.
	[Theory]
	[InlineData(AggregationLayer.Minute, MinuteSpacingSeconds)]
	[InlineData(AggregationLayer.Hour, HourSpacingSeconds)]
	[InlineData(AggregationLayer.Day, DaySpacingSeconds)]
	public void TheSkipThresholdIsTheLayersPointSpacing(AggregationLayer layer, int spacingSeconds)
	{
		var spacing = TimeSpan.FromSeconds(spacingSeconds);

		FreshTail.Start(layer, SeamsAt(_windowEnd - spacing), _windowEnd).Should().BeNull();
		FreshTail.Start(layer, SeamsAt(_windowEnd - spacing - TimeSpan.FromTicks(1)), _windowEnd).Should().NotBeNull();
	}

	// Guards a volume failure an EXPLAIN check cannot see: a pen with no coarse row seams at the window
	// start and sits behind the clamp, so without this bound the read falls back to a full layer period
	// of raw rows per pen (24 h at Day).
	[Theory]
	[InlineData(AggregationLayer.Minute, MinuteSpacingSeconds)]
	[InlineData(AggregationLayer.Hour, HourSpacingSeconds)]
	[InlineData(AggregationLayer.Day, DaySpacingSeconds)]
	public void APenWithNoCoarseRowDoesNotCostAFreshPenATailRead(AggregationLayer layer, int spacingSeconds)
	{
		var windowStart = _windowEnd - (TimeSpan.FromSeconds(spacingSeconds) * 8);
		var freshSeam = _windowEnd.AddSeconds(-1);
		var seams = FreshTail.Seams([Row(_penIds[0], freshSeam)], _penIds, windowStart);

		seams[_penIds[1]].Should().Be(windowStart);
		FreshTail.Start(layer, seams, _windowEnd).Should().BeNull();
	}

	// The same pair with the fresh pen behind its own spacing: the tail is read, and it starts at that
	// pen's seam rather than at the clamp the absent pen would have dragged it to.
	[Theory]
	[InlineData(AggregationLayer.Minute, MinuteSpacingSeconds)]
	[InlineData(AggregationLayer.Hour, HourSpacingSeconds)]
	[InlineData(AggregationLayer.Day, DaySpacingSeconds)]
	public void ATailForALaggingPenStartsAtItsOwnSeamAndNotAtTheClamp(
		AggregationLayer layer,
		int spacingSeconds)
	{
		var spacing = TimeSpan.FromSeconds(spacingSeconds);
		var windowStart = _windowEnd - (spacing * 8);
		var laggingSeam = _windowEnd - (spacing * 2);
		var seams = FreshTail.Seams([Row(_penIds[0], laggingSeam)], _penIds, windowStart);

		FreshTail.Start(layer, seams, _windowEnd).Should().Be(laggingSeam);
	}

	// Within one period the read starts at the earliest seam itself, so nothing between the coarse rows
	// and the tail is left uncovered.
	[Fact]
	public void ATailInsideOnePeriodStartsAtTheEarliestSeam()
	{
		var earliest = _windowEnd.AddSeconds(-40);
		var seams = FreshTail.Seams(
			[Row(_penIds[0], earliest), Row(_penIds[1], _windowEnd.AddSeconds(-20))],
			_penIds,
			_windowStart);

		FreshTail.Start(AggregationLayer.Minute, seams, _windowEnd).Should().Be(earliest);
	}

	// The clamp is a cost bound: one history query pulls at most one period of raw rows. A layer's spacing
	// is a quarter of its period, so the period is four of them. A pen sitting inside the clamp sets the
	// start, and the read can therefore never reach further back than the clamp itself.
	[Theory]
	[InlineData(AggregationLayer.Minute, MinuteSpacingSeconds)]
	[InlineData(AggregationLayer.Hour, HourSpacingSeconds)]
	[InlineData(AggregationLayer.Day, DaySpacingSeconds)]
	public void TheClampCapsTheTailAtOnePeriod(AggregationLayer layer, int spacingSeconds)
	{
		var onePeriod = TimeSpan.FromSeconds(spacingSeconds) * 4;
		var atTheClamp = _windowEnd - onePeriod;
		var seams = FreshTail.Seams(
			[Row(_penIds[0], atTheClamp), Row(_penIds[1], atTheClamp - TimeSpan.FromDays(30))],
			_penIds,
			_windowStart);

		FreshTail.Start(layer, seams, _windowEnd).Should().Be(atTheClamp);
	}

	// Every pen behind the clamp keeps the short right edge it already had, because Merge would discard
	// every row a tail read returned for it. The read is skipped rather than issued and thrown away.
	[Theory]
	[InlineData(AggregationLayer.Minute, MinuteSpacingSeconds)]
	[InlineData(AggregationLayer.Hour, HourSpacingSeconds)]
	[InlineData(AggregationLayer.Day, DaySpacingSeconds)]
	public void NoTailIsReadWhenEveryPenSitsBehindTheClamp(AggregationLayer layer, int spacingSeconds)
	{
		var seams = SeamsAt(_windowEnd - (TimeSpan.FromSeconds(spacingSeconds) * 4) - TimeSpan.FromTicks(1));

		FreshTail.Start(layer, seams, _windowEnd).Should().BeNull();
	}

	// A pen the coarse read answered nothing for takes the window start as its seam, so it is treated as
	// a pen trailing by the whole window rather than as a pen with nothing to be short of.
	[Fact]
	public void APenWithNoCoarseRowSeamsAtTheWindowStart()
	{
		var seams = FreshTail.Seams([Row(_penIds[0], _windowEnd.AddMinutes(-1))], _penIds, _windowStart);

		seams[_penIds[1]].Should().Be(_windowStart);
	}

	[Fact]
	public void APensSeamIsTheNewestRowTheCoarseReadReturnedForIt()
	{
		var newest = _windowStart.AddMinutes(40);
		var seams = FreshTail.Seams(
			[Row(_penIds[0], _windowStart.AddMinutes(20)), Row(_penIds[0], newest)],
			_penIds,
			_windowStart);

		seams[_penIds[0]].Should().Be(newest);
	}

	// The failure the exclusion exists for: coarse rows, then a range no row covers, then tail rows. That
	// range carries no null, so HistoryRowFold opens no gap and MinMaxDecimator writes no NaN column — it
	// draws as one straight interpolated segment across the hole.
	[Fact]
	public void APenWhoseSeamPrecedesTheTailStartContributesNoTailRow()
	{
		var behindSeam = _windowStart.AddMinutes(5);
		var freshSeam = _windowEnd.AddSeconds(-30);
		var tailRow = _windowEnd.AddSeconds(-10);
		var coarse = new[] { Row(_penIds[0], behindSeam), Row(_penIds[1], freshSeam) };
		var seams = FreshTail.Seams(coarse, _penIds, _windowStart);

		var tailStart = FreshTail.Start(AggregationLayer.Minute, seams, _windowEnd);

		tailStart.Should().NotBeNull();
		(behindSeam < tailStart).Should().BeTrue("The lagging pen has to sit behind the clamped tail start.");
		(freshSeam >= tailStart).Should().BeTrue("The fresh pen has to clear the clamped tail start.");

		var tail = new[] { Row(_penIds[0], tailRow), Row(_penIds[1], tailRow) };

		var merged = FreshTail.Merge(coarse, tail, seams, tailStart.Value);

		merged.Should().Equal(
			[Row(_penIds[0], behindSeam), Row(_penIds[1], freshSeam), Row(_penIds[1], tailRow)]);
	}

	// One consecutive run per pen on ascending identifiers, coarse rows then tail rows — the ordering
	// HistoryRowFold.Fold requires, and the one a per-pen concatenation would lose.
	[Fact]
	public void TheMergeKeepsOneAscendingRunPerPen()
	{
		var seam = _windowEnd.AddMinutes(-1);
		var coarse = new[] { Row(_penIds[0], seam), Row(_penIds[1], seam) };
		var seams = FreshTail.Seams(coarse, _penIds, _windowStart);

		var tail = new[]
		{
			Row(_penIds[0], seam.AddSeconds(10)),
			Row(_penIds[0], seam.AddSeconds(20)),
			Row(_penIds[1], seam.AddSeconds(10))
		};

		var merged = FreshTail.Merge(coarse, tail, seams, seam);

		merged.Select(row => row.PenId).Should().Equal(
			[_penIds[0], _penIds[0], _penIds[0], _penIds[1], _penIds[1]]);
		merged.Select(row => row.ArchiveLocal).Should().Equal(
			[seam, seam.AddSeconds(10), seam.AddSeconds(20), seam, seam.AddSeconds(10)]);
	}

	// A pen present only in the tail keeps its rows when its seam clears the bound, and it lands in
	// identifier order rather than after every pen the coarse read answered.
	[Fact]
	public void APenPresentOnlyInTheTailLandsInIdentifierOrder()
	{
		var seams = FreshTail.Seams([Row(_penIds[1], _windowStart)], _penIds, _windowStart);
		var tail = new[] { Row(_penIds[0], _windowStart.AddSeconds(1)) };

		var merged = FreshTail.Merge([Row(_penIds[1], _windowStart)], tail, seams, _windowStart);

		merged.Select(row => row.PenId).Should().Equal([_penIds[0], _penIds[1]]);
	}

	private static Dictionary<int, DateTime> SeamsAt(DateTime archiveLocal)
	{
		return FreshTail.Seams(
			[.. _penIds.Select(penId => Row(penId, archiveLocal))],
			_penIds,
			_windowStart);
	}

	private static HistoryRowFold.Row Row(int penId, DateTime archiveLocal)
	{
		return new HistoryRowFold.Row(penId, archiveLocal, penId, ArchiveRow.OrdinaryQuality);
	}
}
