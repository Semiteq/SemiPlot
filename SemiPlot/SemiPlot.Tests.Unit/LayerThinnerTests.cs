using System.Globalization;

using AwesomeAssertions;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Unit;

// docs/architecture/scada-archive.md#layers and #quality-and-gaps. A coarse layer holds verbatim
// copies of raw rows — up to four per period, plus every marker row regardless of selection.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class LayerThinnerTests
{
	// The measured volume of the standard slice across all four layers, raw plus the coarse rows the
	// thinner selects from them. A floor, not a pin: a waveform change that keeps the volume moves nothing.
	private const int StandardSliceLayeredRowCount = 314845;

	private const int Budget = 4;

	private static readonly DateTime _base = new(2026, 1, 2, 10, 0, 0, DateTimeKind.Unspecified);

	private static readonly IReadOnlyList<ArchiveRow> _rawRows = RawLayerGenerator.Generate(Options());

	[Theory]
	[InlineData(LayerThinner.MinuteLayer)]
	[InlineData(LayerThinner.HourLayer)]
	[InlineData(LayerThinner.DayLayer)]
	public void NoPeriodHoldsMoreThanFourNonMarkerRows(short layer)
	{
		foreach (var period in ByPeriod(LayerThinner.Thin(_rawRows, layer), layer))
		{
			var ordinary = period.Count(row => row.Quality == ArchiveRow.OrdinaryQuality);

			(ordinary <= Budget).Should().BeTrue(
				$"pen {period.Key.Id} kept {ordinary} rows in the period starting {period.Key.Period:O}");
		}
	}

	// Selection is by magnitude, so the amplitude of an excursion survives every layer even though its
	// shape inside the period is lost.
	[Theory]
	[InlineData(LayerThinner.MinuteLayer)]
	[InlineData(LayerThinner.HourLayer)]
	[InlineData(LayerThinner.DayLayer)]
	public void EveryPeriodKeepsItsMinimumAndMaximum(short layer)
	{
		var thinned = ByPeriod(LayerThinner.Thin(_rawRows, layer), layer)
			.ToDictionary(period => period.Key, period => period.ToArray());

		foreach (var period in ByPeriod(_rawRows, layer))
		{
			var kept = thinned[period.Key];

			kept.Select(row => row.Value).Should().Contain(period.Min(row => row.Value));
			kept.Select(row => row.Value).Should().Contain(period.Max(row => row.Value));
		}
	}

	[Theory]
	[InlineData(LayerThinner.MinuteLayer)]
	[InlineData(LayerThinner.HourLayer)]
	[InlineData(LayerThinner.DayLayer)]
	public void EveryPeriodKeepsItsFirstAndLastRow(short layer)
	{
		var thinned = ByPeriod(LayerThinner.Thin(_rawRows, layer), layer)
			.ToDictionary(period => period.Key, period => period.Select(BenchRows.Identity).ToHashSet());

		foreach (var period in ByPeriod(_rawRows, layer))
		{
			var ordered = period.OrderBy(row => row.Timestamp).ToArray();

			thinned[period.Key].Should().Contain(BenchRows.Identity(ordered[0]));
			thinned[period.Key].Should().Contain(BenchRows.Identity(ordered[^1]));
		}
	}

	// Computing each layer against the raw rows is what makes this hold on its own: a day's extremum is
	// also the extremum of the hour and the minute that contain it.
	[Fact]
	public void LayersNestFromTheDayDownToTheRawRows()
	{
		var raw = _rawRows.Select(BenchRows.Identity).ToHashSet();
		var minute = LayerThinner.Thin(_rawRows, LayerThinner.MinuteLayer).Select(BenchRows.Identity).ToHashSet();
		var hour = LayerThinner.Thin(_rawRows, LayerThinner.HourLayer).Select(BenchRows.Identity).ToHashSet();
		var day = LayerThinner.Thin(_rawRows, LayerThinner.DayLayer).Select(BenchRows.Identity).ToHashSet();

		minute.Should().BeSubsetOf(raw).And.NotBeEquivalentTo(raw);
		hour.Should().BeSubsetOf(minute).And.NotBeEquivalentTo(minute);
		day.Should().BeSubsetOf(hour).And.NotBeEquivalentTo(hour);
	}

	[Fact]
	public void EveryCoarseRowCopiesARawRowExactly()
	{
		var raw = _rawRows.Select(BenchRows.Identity).ToHashSet();

		foreach (var row in LayerThinner.ThinAll(_rawRows))
		{
			raw.Should().Contain(BenchRows.Identity(row));
			LayerThinner.CoarseLayers.Should().Contain(row.Layer);
		}
	}

	[Fact]
	public void ThinAllProducesTheThreeCoarseLayers()
	{
		var byLayer = LayerThinner.ThinAll(_rawRows)
			.GroupBy(row => row.Layer)
			.ToDictionary(layer => layer.Key, layer => layer.Count());

		byLayer.Keys.Order().Should().Equal(LayerThinner.CoarseLayers.Order());
		(byLayer[LayerThinner.MinuteLayer] > byLayer[LayerThinner.HourLayer]).Should().BeTrue();
		(byLayer[LayerThinner.HourLayer] > byLayer[LayerThinner.DayLayer]).Should().BeTrue();
	}

	// A slice falling under this number would move the gated expectations — the index plans, the
	// statement-timeout bound and the minute-layer density test — with nothing here failing first.
	[Fact]
	public void TheStandardSliceFillsFourLayersAtOrAboveTheMeasuredVolume()
	{
		var raw = RawLayerGenerator.Generate(BenchOptions.For());
		var coarse = LayerThinner.ThinAll(raw);
		var total = raw.Count + coarse.Count;

		coarse.Select(row => row.Layer).Distinct().Order().Should().Equal(LayerThinner.CoarseLayers.Order());
		(total >= StandardSliceLayeredRowCount).Should().BeTrue(
			$"the standard slice holds {total} rows across its four layers");
	}

	// A gap boundary has to survive thinning, or a broken line renders as a straight one at any zoom
	// level (docs/architecture/scada-archive.md#quality-and-gaps).
	[Fact]
	public void EveryMarkerRowReachesEveryLayer()
	{
		var markers = _rawRows
			.Where(row => row.Quality != ArchiveRow.OrdinaryQuality)
			.Select(BenchRows.Identity)
			.ToArray();

		markers.Should().NotBeEmpty();

		foreach (var layer in LayerThinner.CoarseLayers)
		{
			var kept = LayerThinner.Thin(_rawRows, layer).Select(BenchRows.Identity).ToHashSet();

			markers.Should().AllSatisfy(marker => kept.Should().Contain(marker));
		}
	}

	[Fact]
	public void PensAreThinnedIndependentlyOfEachOther()
	{
		var rows = new[]
		{
			Row(1000, _base, 5.0),
			Row(1000, _base.AddSeconds(30), 1.0),
			Row(2000, _base.AddSeconds(10), 7.0),
			Row(2000, _base.AddSeconds(40), 9.0)
		};

		var thinned = LayerThinner.Thin(rows, LayerThinner.MinuteLayer);

		thinned.Count.Should().Be(4);
		thinned.Count(row => row.Id == 1000).Should().Be(2);
		thinned.Count(row => row.Id == 2000).Should().Be(2);
	}

	[Fact]
	public void APeriodWithOneRawRowYieldsOneCoarseRow()
	{
		var row = Row(1000, _base.AddSeconds(17), 42.5);

		var thinned = LayerThinner.Thin([row], LayerThinner.MinuteLayer);

		thinned.Should().ContainSingle().Which.Should().Be(row with { Layer = LayerThinner.MinuteLayer });
	}

	[Fact]
	public void APeriodWithNoRawRowsYieldsNothing()
	{
		var rows = new[]
		{
			Row(1000, _base, 5.0),
			Row(1000, _base.AddMinutes(2), 6.0)
		};

		var periods = LayerThinner.Thin(rows, LayerThinner.MinuteLayer)
			.Select(row => LayerThinner.PeriodStart(row.Timestamp, LayerThinner.MinuteLayer))
			.ToHashSet();

		periods.Should().NotContain(_base.AddMinutes(1));
		LayerThinner.Thin([], LayerThinner.MinuteLayer).Should().BeEmpty();
	}

	[Fact]
	public void APeriodHoldingOnlyMarkerRowsKeepsBothOfThem()
	{
		var rows = new[]
		{
			Row(1000, _base, 5.0, ArchiveRow.LastBeforeBreakQuality),
			Row(1000, _base.AddSeconds(30), 6.0, ArchiveRow.FirstAfterBreakQuality)
		};

		var thinned = LayerThinner.Thin(rows, LayerThinner.MinuteLayer);

		thinned.Should().Equal(rows.Select(row => row with { Layer = LayerThinner.MinuteLayer }));
	}

	// Markers are additional to the four, so a period bounding a break can legitimately exceed the
	// budget — which is why the budget assertion counts ordinary rows only.
	[Fact]
	public void MarkerRowsAreKeptOnTopOfTheFourSelectedOnes()
	{
		var rows = new[]
		{
			Row(1000, _base, 5.0),
			Row(1000, _base.AddSeconds(10), 1.0),
			Row(1000, _base.AddSeconds(20), 3.0, ArchiveRow.LastBeforeBreakQuality),
			Row(1000, _base.AddSeconds(30), 4.0, ArchiveRow.FirstAfterBreakQuality),
			Row(1000, _base.AddSeconds(40), 9.0),
			Row(1000, _base.AddSeconds(50), 6.0)
		};

		var thinned = LayerThinner.Thin(rows, LayerThinner.MinuteLayer);

		thinned.Should().Equal(rows.Select(row => row with { Layer = LayerThinner.MinuteLayer }));
		thinned.Count(row => row.Quality == ArchiveRow.OrdinaryQuality).Should().Be(Budget);
	}

	[Fact]
	public void APeriodWhoseFirstRowIsAlsoItsMinimumDeduplicatesThem()
	{
		var rows = new[]
		{
			Row(1000, _base, 1.0),
			Row(1000, _base.AddSeconds(20), 9.0),
			Row(1000, _base.AddSeconds(40), 5.0)
		};

		var thinned = LayerThinner.Thin(rows, LayerThinner.MinuteLayer);

		thinned.Should().Equal(rows.Select(row => row with { Layer = LayerThinner.MinuteLayer }));
	}

	[Fact]
	public void RepeatedExtremesResolveToTheEarliestRow()
	{
		var rows = new[]
		{
			Row(1000, _base, 5.0),
			Row(1000, _base.AddSeconds(10), 1.0),
			Row(1000, _base.AddSeconds(20), 1.0),
			Row(1000, _base.AddSeconds(50), 6.0)
		};

		var thinned = LayerThinner.Thin(rows, LayerThinner.MinuteLayer);

		thinned.Select(row => row.Timestamp).Should().NotContain(_base.AddSeconds(20));
		thinned.Count.Should().Be(3);
	}

	[Fact]
	public void ThinnedRowsAreOrderedByTimestampWithinAPen()
	{
		foreach (var pen in LayerThinner.Thin(_rawRows, LayerThinner.MinuteLayer).GroupBy(row => row.Id))
		{
			var timestamps = pen.Select(row => row.Timestamp).ToArray();

			timestamps.Should().Equal(timestamps.Order());
		}
	}

	[Theory]
	[InlineData(LayerThinner.MinuteLayer, "2026-01-02T10:37:00.0000000")]
	[InlineData(LayerThinner.HourLayer, "2026-01-02T10:00:00.0000000")]
	[InlineData(LayerThinner.DayLayer, "2026-01-02T00:00:00.0000000")]
	public void PeriodStartAlignsToTheCalendar(short layer, string expected)
	{
		var timestamp = new DateTime(2026, 1, 2, 10, 37, 42, 913, DateTimeKind.Unspecified);

		LayerThinner.PeriodStart(timestamp, layer).Should().Be(
			DateTime.Parse(expected, CultureInfo.InvariantCulture, DateTimeStyles.None));
	}

	[Theory]
	[InlineData(ArchiveRow.RawLayer)]
	[InlineData((short)4)]
	public void PeriodStartRejectsALayerThatIsNotCoarse(short layer)
	{
		var act = () => LayerThinner.PeriodStart(_base, layer);

		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	// One pen's one period is the unit the budget and the extremes are stated over.
	private static IEnumerable<IGrouping<(int Id, DateTime Period), ArchiveRow>> ByPeriod(
		IEnumerable<ArchiveRow> rows,
		short layer)
	{
		return rows.GroupBy(row => (row.Id, Period: LayerThinner.PeriodStart(row.Timestamp, layer)));
	}

	private static ArchiveRow Row(int id, DateTime timestamp, double value, int quality = ArchiveRow.OrdinaryQuality)
	{
		return new ArchiveRow(id, ArchiveRow.RawLayer, timestamp, value, quality);
	}

	// Three pens are enough for a rule stated one pen at a time, and keep a whole day of rows cheap.
	private static SeederOptions Options()
	{
		return BenchOptions.For(pens: 3);
	}
}
