using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data;

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
		Assert.Equal(RawLayerGenerator.Generate(BenchOptions.For()), RawLayerGenerator.Generate(BenchOptions.For()));
	}

	[Fact]
	public void ADifferentSeedProducesDifferentRows()
	{
		var first = RawLayerGenerator.Generate(BenchOptions.For());
		var second = RawLayerGenerator.Generate(BenchOptions.For(seed: 2));

		Assert.NotEqual(first, second);
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

			Assert.True(
				offset == 0L || offset == interval - anchorOffset,
				$"{row.Timestamp:O} sits off the absolute lattice.");
		}
	}

	// A plan with breaks is the no-break lattice with the break windows cut out and nothing else missing.
	// The only other rows a break costs are two anchors: the one ahead of the first change past the break's
	// start, which would repeat a value with no change behind it, and the one ahead of the resume change,
	// which the plant's movement during the stop makes a level of its own. Every pen reads the same, because
	// the project stops as a whole.
	[Fact]
	public void BreaksCutTheirWindowsOutOfTheLatticeAndLeaveNoOtherHole()
	{
		var options = BenchOptions.For();
		var plan = BreakPlan.Create(options);
		var interval = TimeSpan.FromSeconds(options.ChangeSeconds).Ticks;
		var continuous = BenchRows.ByPen(RawLayerGenerator.Generate(options with { BreakCount = 0 }));
		var broken = BenchRows.ByPen(RawLayerGenerator.Generate(options));

		Assert.Equal(continuous.Count, broken.Count);

		for (var pen = 0; pen < continuous.Count; pen++)
		{
			var expected = continuous[pen]
				.Where(row => !plan.Breaks.Any(window => Removes(window, row, interval)))
				.Select(row => (row.Timestamp, row.Value));

			Assert.Equal(expected, broken[pen].Select(row => (row.Timestamp, row.Value)));
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

				Assert.Equal(_pollInterval, pen[index].Timestamp - pen[index - 1].Timestamp);
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
				Assert.True(pen[index].Timestamp > pen[index - 1].Timestamp);
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
			Assert.True(keys.Add((row.Id, row.Layer, row.Timestamp)));
		}
	}

	[Fact]
	public void TimestampsCarryWholeMillisecondsOnly()
	{
		foreach (var row in RawLayerGenerator.Generate(BenchOptions.For()))
		{
			Assert.Equal(0, row.Timestamp.Ticks % TimeSpan.TicksPerMillisecond);
		}
	}

	[Fact]
	public void ALowChangeRateLeavesStretchesLongerThanAMinuteWithNoRows()
	{
		var rows = RawLayerGenerator.Generate(BenchOptions.For(pens: 1, changeSeconds: 300.0));
		var longest = LongestGap(rows);

		Assert.True(longest > TimeSpan.FromMinutes(1), $"longest quiet stretch was {longest}");
	}

	[Fact]
	public void EveryRowFallsInsideTheHalfOpenSpan()
	{
		var options = BenchOptions.For();

		foreach (var row in RawLayerGenerator.Generate(options))
		{
			Assert.InRange(row.Timestamp, options.Start, options.End.AddTicks(-1));
		}
	}

	// The bench emits three quality codes and no others: no bad-quality code was observed in the
	// measured dump, so inventing one would be fiction.
	[Fact]
	public void EveryRowCarriesTheRawLayerAndOneOfTheThreeQualityCodes()
	{
		foreach (var row in RawLayerGenerator.Generate(BenchOptions.For()))
		{
			Assert.Equal(ArchiveRow.RawLayer, row.Layer);
			Assert.Contains(row.Quality, _qualityCodes);
		}
	}

	[Fact]
	public void EveryValueStaysInsideItsPenRange()
	{
		var pens = RawLayerGenerator.SelectPens(8).ToDictionary(pen => pen.PenId);

		foreach (var row in RawLayerGenerator.Generate(BenchOptions.For()))
		{
			var pen = pens[row.Id];

			Assert.InRange(row.Value, pen.MinValue, pen.MaxValue);
		}
	}

	[Fact]
	public void TheStandardSliceSpansMoreThanOneGroupAndMoreThanOneValueRange()
	{
		var pens = RawLayerGenerator.SelectPens(SeederOptions.DefaultPenCount);

		Assert.True(pens.Select(pen => pen.Group).Distinct(StringComparer.Ordinal).Count() > 1);
		Assert.True(pens.Select(pen => (pen.MinValue, pen.MaxValue)).Distinct().Count() > 1);
	}

	[Fact]
	public void PensAreTakenRoundRobinAcrossTheGroupsRatherThanFirstN()
	{
		var pens = RawLayerGenerator.SelectPens(6);

		Assert.Equal(
			new[] { "Heaters", "Dampers", "Gas lines", "Pressures", "Powers", "Heaters" },
			pens.Select(pen => pen.Group));
		Assert.Equal(new[] { 1000, 2000, 3000, 4000, 5000, 1001 }, pens.Select(pen => pen.PenId));
	}

	[Fact]
	public void TheStandardSliceGivesEveryPenItsOwnColour()
	{
		var colours = RawLayerGenerator.SelectPens(SeederOptions.DefaultPenCount).Select(pen => pen.Color).ToArray();

		Assert.Equal(colours.Length, colours.Distinct(StringComparer.Ordinal).Count());
		Assert.All(colours, colour => Assert.Matches("^#[0-9A-F]{6}$", colour));
	}

	[Fact]
	public void ASinglePenProducesRowsForThatPenOnly()
	{
		var rows = RawLayerGenerator.Generate(BenchOptions.For(pens: 1));

		Assert.NotEmpty(rows);
		Assert.All(rows, row => Assert.Equal(1000, row.Id));
	}

	// Both markers of a break go on real change rows, so a run bounded by a break needs two of them. The
	// tight run is the first or the last — 600 s over 20 breaks leaves the last run under one change — while
	// a run between two breaks is at least twice as long and reaches a single change only at 40 breaks and
	// beyond. Both shapes are refused rather than patched.
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

		var rejected = Assert.Throws<ArgumentOutOfRangeException>(() => RawLayerGenerator.Generate(options));

		Assert.Contains("fewer than two changes", rejected.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void SelectPensRejectsMoreThanTheCatalogueHolds()
	{
		var catalogue = SyntheticPenCatalog.Build().Count;

		Assert.Equal(catalogue, RawLayerGenerator.SelectPens(catalogue).Count);
		Assert.Throws<ArgumentOutOfRangeException>(() => RawLayerGenerator.SelectPens(catalogue + 1));
	}

	// An interval as long as the whole span puts the first change past the end, and the span reaching
	// back to the earliest representable timestamp leaves no lattice point ahead of it either. What is
	// left is the anchor of that first change, one poll interval inside the span.
	// --change-seconds may be exactly the span, so this is in range.
	[Fact]
	public void GenerateAcceptsAChangeIntervalAsLongAsTheWholeSpan()
	{
		var span = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified) - DateTime.MinValue;

		var options = BenchOptions.For(
			days: (int)(span.Ticks / TimeSpan.TicksPerDay),
			pens: 1,
			changeSeconds: span.TotalSeconds,
			breaks: 0);

		var only = Assert.Single(RawLayerGenerator.Generate(options));

		Assert.InRange(only.Timestamp, options.Start, options.End.AddTicks(-1));
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
