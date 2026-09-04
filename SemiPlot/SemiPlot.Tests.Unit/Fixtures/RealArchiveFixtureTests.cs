using AwesomeAssertions;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Unit.Fixtures;

// The bench's thinning rule confronted with rows the vendor actually wrote; only what
// docs/architecture/scada-archive.md records as measured is asserted (#layers, #quality-and-gaps,
// #not-established).
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class RealArchiveFixtureTests(ITestOutputHelper testOutputHelper)
{
	// The archived project polled at 100 ms, but a poll tick is a software timer and not a lattice:
	// the extract holds four ticks that ran late. The invariant is that no change row is closer to its
	// predecessor than one poll interval, and none is further than one interval plus this.
	private static readonly TimeSpan _maxPollJitter = TimeSpan.FromMilliseconds(10);

	[Fact]
	public void TheFixtureCarriesAllFourLayersForBothPens()
	{
		var pens = RealArchiveFixture.Pens();

		pens.Count.Should().Be(2);

		foreach (var pen in pens)
		{
			foreach (var layer in (short[])[ArchiveRow.RawLayer, .. LayerThinner.CoarseLayers])
			{
				RealArchiveFixture.Rows.Should().Contain(row => row.Id == pen && row.Layer == layer);
			}
		}

		InMinute(RealArchiveFixture.RawRows, RealArchiveFixture.ChosenMinute).Should().NotBeEmpty();
	}

	// The extract was chosen to hold all four shapes the bench reproduces, so a generator that stopped
	// producing one of them could still be compared against something real.
	[Fact]
	public void TheFixtureCoversAnAnchorPairASteadyStretchAndAMarkerPair()
	{
		var anchorPairs = 0;
		var steadyStretches = 0;
		var markerPairs = 0;

		foreach (var pen in RealArchiveFixture.Pens())
		{
			var ordered = Ordered(RealArchiveFixture.RawRows, pen);

			for (var index = 1; index < ordered.Count; index++)
			{
				var span = ordered[index].Timestamp - ordered[index - 1].Timestamp;
				var changed = !ordered[index].Value.Equals(ordered[index - 1].Value);

				anchorPairs += changed && span == RealArchiveFixture.PollInterval ? 1 : 0;
				steadyStretches += !changed && span > TimeSpan.FromMinutes(1) ? 1 : 0;
				markerPairs += ordered[index - 1].Quality == ArchiveRow.LastBeforeBreakQuality
					&& ordered[index].Quality == ArchiveRow.FirstAfterBreakQuality
					? 1
					: 0;
			}
		}

		(anchorPairs > 0).Should().BeTrue("the extract holds no change row one poll interval after its predecessor");
		(steadyStretches > 0).Should().BeTrue("the extract holds no stretch longer than a minute without a change");
		(markerPairs > 0).Should().BeTrue("the extract holds no 32/16 marker pair");
	}

	// docs/architecture/scada-archive.md#layers — every coarse row reproduces the timestamp, value and
	// quality of an existing raw row. 170 of 170 matched in the whole dump; all of them do here too.
	[Theory]
	[InlineData(LayerThinner.MinuteLayer)]
	[InlineData(LayerThinner.HourLayer)]
	[InlineData(LayerThinner.DayLayer)]
	public void EveryCoarseRowCopiesARawRowExactly(short layer)
	{
		var raw = RealArchiveFixture.RawRows.Select(BenchRows.Identity).ToHashSet();
		var coarse = RealArchiveFixture.Layer(layer);

		coarse.Should().NotBeEmpty();
		coarse.Should().AllSatisfy(row => raw.Should().Contain(BenchRows.Identity(row)));
	}

	// docs/architecture/scada-archive.md#layers — the minute layer carries that minute's lowest and
	// highest samples, which is what keeps the amplitude of an excursion visible at every zoom level.
	[Fact]
	public void EveryMinuteKeepsItsExtremesInTheMinuteLayer()
	{
		var minuteLayer = RealArchiveFixture.Layer(LayerThinner.MinuteLayer);

		foreach (var minute in ByMinute(RealArchiveFixture.RawRows))
		{
			var kept = ByMinute(minuteLayer)
				.Single(period => period.Key == minute.Key)
				.Select(row => row.Value)
				.ToArray();

			kept.Should().Contain(minute.Min(row => row.Value));
			kept.Should().Contain(minute.Max(row => row.Value));
		}
	}

	// docs/architecture/scada-archive.md#quality-and-gaps — marker rows are copied into every layer
	// unchanged, so a gap boundary survives thinning and a broken line renders correctly at any zoom
	// level.
	[Fact]
	public void EveryMarkerRowAppearsInEveryLayer()
	{
		var markers = RealArchiveFixture.RawRows
			.Where(row => row.Quality != ArchiveRow.OrdinaryQuality)
			.Select(BenchRows.Identity)
			.ToArray();

		markers.Should().NotBeEmpty();

		foreach (var layer in LayerThinner.CoarseLayers)
		{
			var kept = RealArchiveFixture.Layer(layer).Select(BenchRows.Identity).ToHashSet();

			markers.Should().AllSatisfy(marker => kept.Should().Contain(marker));
		}
	}

	// docs/architecture/scada-archive.md#write-behavior over real rows: a changed value sits one poll
	// interval after its predecessor, except the extract's first row and a q = 16 row after a break.
	[Fact]
	public void EveryChangeRowFollowsItsPredecessorByOnePollInterval()
	{
		var spans = ChangeRowSpans().ToArray();

		spans.Should().NotBeEmpty();
		spans.Should().AllSatisfy(span => span.Should().BeGreaterThanOrEqualTo(RealArchiveFixture.PollInterval)
			.And.BeLessThanOrEqualTo(RealArchiveFixture.PollInterval + _maxPollJitter));

		testOutputHelper.WriteLine(
			$"{spans.Count(span => span == RealArchiveFixture.PollInterval)} of {spans.Length} change rows "
			+ "sit exactly one poll interval after their predecessor; the widest is "
			+ $"{spans.Max().TotalMilliseconds} ms.");
	}

	// Reported, never asserted: exact set equality against the vendor's own selection is unsafe to gate
	// on, so only that both selections draw from the same raw rows and cover the same minutes is asserted.
	[Fact]
	public void TheThinnerIsComparedWithTheRealMinuteLayer()
	{
		var raw = RealArchiveFixture.RawRows;
		var ours = LayerThinner.Thin(raw, LayerThinner.MinuteLayer).Select(BenchRows.Identity).ToHashSet();
		var vendor = RealArchiveFixture.Layer(LayerThinner.MinuteLayer).Select(BenchRows.Identity).ToHashSet();
		var rawIdentities = raw.Select(BenchRows.Identity).ToHashSet();

		ours.Should().BeSubsetOf(rawIdentities);
		vendor.Should().BeSubsetOf(rawIdentities);
		Minutes(ours).Should().Equal(Minutes(vendor));

		testOutputHelper.WriteLine(
			$"real l=1 rows {vendor.Count}, LayerThinner rows {ours.Count}, agreed {ours.Intersect(vendor).Count()}");
		Report("only LayerThinner produced it", ours.Except(vendor));
		Report("only the vendor wrote it", vendor.Except(ours));
	}

	private static IEnumerable<TimeSpan> ChangeRowSpans()
	{
		foreach (var pen in RealArchiveFixture.Pens())
		{
			var ordered = Ordered(RealArchiveFixture.RawRows, pen);

			for (var index = 1; index < ordered.Count; index++)
			{
				var row = ordered[index];

				if (row.Value.Equals(ordered[index - 1].Value) || row.Quality == ArchiveRow.FirstAfterBreakQuality)
				{
					continue;
				}

				yield return row.Timestamp - ordered[index - 1].Timestamp;
			}
		}
	}

	private void Report(string heading, IEnumerable<(int Id, DateTime Timestamp, double Value, int Quality)> rows)
	{
		var listed = rows.OrderBy(row => row.Id).ThenBy(row => row.Timestamp).ToArray();

		testOutputHelper.WriteLine($"{listed.Length} rows {heading}:");

		foreach (var (id, timestamp, value, quality) in listed)
		{
			testOutputHelper.WriteLine($"  id={id} t={timestamp:HH:mm:ss.fff} v={value} q={quality}");
		}
	}

	private static IReadOnlyList<ArchiveRow> Ordered(IEnumerable<ArchiveRow> rows, int pen)
	{
		return [.. rows.Where(row => row.Id == pen).OrderBy(row => row.Timestamp)];
	}

	private static IReadOnlyList<ArchiveRow> InMinute(IEnumerable<ArchiveRow> rows, DateTime minute)
	{
		return [.. rows.Where(row => LayerThinner.PeriodStart(row.Timestamp, LayerThinner.MinuteLayer) == minute)];
	}

	private static IEnumerable<IGrouping<(int Id, DateTime Minute), ArchiveRow>> ByMinute(IEnumerable<ArchiveRow> rows)
	{
		return rows.GroupBy(row => (row.Id, Minute: LayerThinner.PeriodStart(row.Timestamp, LayerThinner.MinuteLayer)));
	}

	private static IReadOnlyList<(int Id, DateTime Minute)> Minutes(
		IEnumerable<(int Id, DateTime Timestamp, double Value, int Quality)> rows)
	{
		return [.. rows
			.Select(row => (row.Id, Minute: LayerThinner.PeriodStart(row.Timestamp, LayerThinner.MinuteLayer)))
			.Distinct()
			.Order()];
	}
}
