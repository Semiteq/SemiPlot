using AwesomeAssertions;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Unit;

// What pins the seeding generator: determinism, the absolute lattice, the break holes and the row-pair
// shape. The waveform itself is not pinned, so a deliberate change to the value walk moves no constant
// here; a change that breaks one of these properties is what the suite is for.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class RawLayerGeneratorTests
{
	private static readonly TimeSpan _pollInterval = RawLayerGenerator.PollInterval;

	private static readonly int[] _qualityCodes =
	[
		ArchiveRow.OrdinaryQuality,
		ArchiveRow.FirstAfterBreakQuality,
		ArchiveRow.LastBeforeBreakQuality
	];

	[Fact]
	public void TheSameOptionsGenerateTheSameRowsTwice()
	{
		RawLayerGenerator.Generate(BenchOptions.For()).Should().Equal(RawLayerGenerator.Generate(BenchOptions.For()));
	}

	[Fact]
	public void ADifferentSeedProducesDifferentRows()
	{
		var first = RawLayerGenerator.Generate(BenchOptions.For());
		var second = RawLayerGenerator.Generate(BenchOptions.For(seed: 2));

		first.Should().NotEqual(second);
	}

	// The lattice is absolute (docs/architecture/bench.md): a change sits at index * interval from tick
	// zero and its anchor one poll interval ahead of that, whatever the span's own start is.
	[Fact]
	public void EveryRowSitsOnTheAbsoluteLattice()
	{
		var options = BenchOptions.For();
		var interval = TimeSpan.FromSeconds(options.ChangeSeconds).Ticks;
		var anchorOffset = _pollInterval.Ticks;

		foreach (var row in RawLayerGenerator.Generate(options))
		{
			var offset = row.Timestamp.Ticks % interval;

			(offset == 0L || offset == interval - anchorOffset).Should().BeTrue(
				$"{row.Timestamp:O} sits off the absolute lattice.");
		}
	}

	// A plan with breaks is the no-break lattice with the break windows cut out and nothing else missing,
	// beyond two anchors a break costs: one repeating a value with no change behind it, one ahead of the
	// resume change. Every pen reads the same, because the project stops as a whole.
	[Fact]
	public void BreaksCutTheirWindowsOutOfTheLatticeAndLeaveNoOtherHole()
	{
		var options = BenchOptions.For();
		var plan = BreakPlan.Create(options);
		var interval = TimeSpan.FromSeconds(options.ChangeSeconds).Ticks;
		var continuous = BenchRows.ByPen(RawLayerGenerator.Generate(options with { BreakCount = 0 }));
		var broken = BenchRows.ByPen(RawLayerGenerator.Generate(options));

		broken.Count.Should().Be(continuous.Count);

		for (var pen = 0; pen < continuous.Count; pen++)
		{
			var expected = continuous[pen]
				.Where(row => !plan.Breaks.Any(window => Removes(window, row, interval)))
				.Select(row => (row.Timestamp, row.Value));

			broken[pen].Select(row => (row.Timestamp, row.Value)).Should().Equal(expected);
		}
	}

	// The pair-local invariant of docs/architecture/scada-archive.md#write-behavior. A row carrying a value
	// its predecessor did not carry is a change row; two are exempt — the run's first row, and the
	// q = 16 row resuming after a break, whose pre-anchor would fall inside the gap the break forbids.
	[Fact]
	public void EveryChangeRowFollowsItsPredecessorByExactlyOnePollInterval()
	{
		foreach (var pen in BenchRows.ByPen(RawLayerGenerator.Generate(BenchOptions.For())))
		{
			for (var index = 1; index < pen.Count; index++)
			{
				if (pen[index].Value == pen[index - 1].Value
					|| pen[index].Quality == ArchiveRow.FirstAfterBreakQuality)
				{
					continue;
				}

				(pen[index].Timestamp - pen[index - 1].Timestamp).Should().Be(_pollInterval);
			}
		}
	}

	[Fact]
	public void TimestampsAreStrictlyAscendingPerPen()
	{
		foreach (var pen in BenchRows.ByPen(RawLayerGenerator.Generate(BenchOptions.For())))
		{
			for (var index = 1; index < pen.Count; index++)
			{
				(pen[index].Timestamp > pen[index - 1].Timestamp).Should().BeTrue();
			}
		}
	}

	[Fact]
	public void NoTwoRowsShareTheSameKeyAfterMillisecondTruncation()
	{
		var rows = RawLayerGenerator.Generate(BenchOptions.For());
		var keys = new HashSet<(int Id, short Layer, DateTime Timestamp)>();

		foreach (var row in rows)
		{
			keys.Add((row.Id, row.Layer, row.Timestamp)).Should().BeTrue();
		}
	}

	[Fact]
	public void TimestampsCarryWholeMillisecondsOnly()
	{
		foreach (var row in RawLayerGenerator.Generate(BenchOptions.For()))
		{
			(row.Timestamp.Ticks % TimeSpan.TicksPerMillisecond).Should().Be(0);
		}
	}

	[Fact]
	public void ALowChangeRateLeavesStretchesLongerThanAMinuteWithNoRows()
	{
		var rows = RawLayerGenerator.Generate(BenchOptions.For(pens: 1, changeSeconds: 300.0));
		var longest = LongestGap(rows);

		(longest > TimeSpan.FromMinutes(1)).Should().BeTrue($"longest quiet stretch was {longest}");
	}

	[Fact]
	public void EveryRowFallsInsideTheHalfOpenSpan()
	{
		var options = BenchOptions.For();

		foreach (var row in RawLayerGenerator.Generate(options))
		{
			row.Timestamp.Should().BeOnOrAfter(options.Start).And.BeOnOrBefore(options.End.AddTicks(-1));
		}
	}

	// The bench emits three quality codes and no others: no bad-quality code was observed in the
	// measured dump, so inventing one would be fiction.
	[Fact]
	public void EveryRowCarriesTheRawLayerAndOneOfTheThreeQualityCodes()
	{
		foreach (var row in RawLayerGenerator.Generate(BenchOptions.For()))
		{
			row.Layer.Should().Be(ArchiveRow.RawLayer);
			_qualityCodes.Should().Contain(row.Quality);
		}
	}

	[Fact]
	public void EveryValueStaysInsideItsPenRange()
	{
		var pens = RawLayerGenerator.SelectPens(8).ToDictionary(pen => pen.PenId);

		foreach (var row in RawLayerGenerator.Generate(BenchOptions.For()))
		{
			var pen = pens[row.Id];

			row.Value.Should().BeInRange(pen.MinValue, pen.MaxValue);
		}
	}

	[Fact]
	public void TheStandardSliceSpansMoreThanOneGroupAndMoreThanOneValueRange()
	{
		var pens = RawLayerGenerator.SelectPens(SeederOptions.DefaultPenCount);

		(pens.Select(pen => pen.Group).Distinct(StringComparer.Ordinal).Count() > 1).Should().BeTrue();
		(pens.Select(pen => (pen.MinValue, pen.MaxValue)).Distinct().Count() > 1).Should().BeTrue();
	}

	[Fact]
	public void PensAreTakenRoundRobinAcrossTheGroupsRatherThanFirstN()
	{
		var pens = RawLayerGenerator.SelectPens(6);

		pens.Select(pen => pen.Group).Should().Equal(
			"Heaters", "Dampers", "Gas lines", "Pressures", "Powers", "Heaters");
		pens.Select(pen => pen.PenId).Should().Equal(1000, 2000, 3000, 4000, 5000, 1001);
	}

	[Fact]
	public void TheStandardSliceGivesEveryPenItsOwnColour()
	{
		var colours = RawLayerGenerator.SelectPens(SeederOptions.DefaultPenCount).Select(pen => pen.Color).ToArray();

		colours.Distinct(StringComparer.Ordinal).Count().Should().Be(colours.Length);
		colours.Should().AllSatisfy(colour => colour.Should().MatchRegex("^#[0-9A-F]{6}$"));
	}

	[Fact]
	public void ASinglePenProducesRowsForThatPenOnly()
	{
		var rows = RawLayerGenerator.Generate(BenchOptions.For(pens: 1));

		rows.Should().NotBeEmpty();
		rows.Should().AllSatisfy(row => row.Id.Should().Be(1000));
	}

	// Both markers of a break go on real change rows, so a run bounded by a break needs two of them; the
	// tightest run is the first or the last one. Both under-provisioned shapes are refused, not patched.
	[Theory]
	[InlineData(20)]
	[InlineData(30)]
	[InlineData(40)]
	[InlineData(50)]
	[InlineData(55)]
	[InlineData(60)]
	[InlineData(72)]
	public void AChangeIntervalThatLeavesARunWithFewerThanTwoChangesIsRejected(int breaks)
	{
		var options = BenchOptions.For(pens: 1, changeSeconds: 600.0, breaks: breaks);

		var act = () => RawLayerGenerator.Generate(options);

		var rejected = act.Should().Throw<ArgumentOutOfRangeException>().Which;

		rejected.Message.Should().Contain("fewer than two changes");
	}

	[Fact]
	public void SelectPensRejectsMoreThanTheCatalogueHolds()
	{
		var catalogue = SyntheticPenCatalog.Build().Count;

		RawLayerGenerator.SelectPens(catalogue).Count.Should().Be(catalogue);

		var act = () => RawLayerGenerator.SelectPens(catalogue + 1);

		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	// An interval as long as the whole span puts the first change past the end, leaving only the anchor
	// of that first change, one poll interval inside the span. --change-seconds may equal the span.
	[Fact]
	public void GenerateAcceptsAChangeIntervalAsLongAsTheWholeSpan()
	{
		var span = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified) - DateTime.MinValue;

		var options = BenchOptions.For(
			days: (int)(span.Ticks / TimeSpan.TicksPerDay),
			pens: 1,
			changeSeconds: span.TotalSeconds,
			breaks: 0);

		var only = RawLayerGenerator.Generate(options).Should().ContainSingle().Which;

		only.Timestamp.Should().BeOnOrAfter(options.Start).And.BeOnOrBefore(options.End.AddTicks(-1));
	}

	// A row of the continuous lattice that a break window removes: one inside the window, the anchor of the
	// first change at or past the window's start, or the anchor of the first change at or past its end.
	private static bool Removes(BreakPlan.Window window, ArchiveRow row, long interval)
	{
		var timestamp = row.Timestamp;

		if (timestamp >= window.Start && timestamp < window.End)
		{
			return true;
		}

		if (timestamp.Ticks % interval != interval - _pollInterval.Ticks)
		{
			return false;
		}

		var change = timestamp + _pollInterval;
		var stranded = change >= window.Start && timestamp < window.Start;
		var resumes = change >= window.End && change.AddTicks(-interval) < window.End;

		return stranded || resumes;
	}

	private static TimeSpan LongestGap(IReadOnlyList<ArchiveRow> rows)
	{
		var longest = TimeSpan.Zero;

		for (var index = 1; index < rows.Count; index++)
		{
			var gap = rows[index].Timestamp - rows[index - 1].Timestamp;

			if (gap > longest)
			{
				longest = gap;
			}
		}

		return longest;
	}
}
