using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class RawLayerGeneratorTests
{
	// The bench every later slice develops against: 1 day, 8 pens, seed 1, a fixed exclusive end.
	private const string StandardSliceDigest = "b1291512fa0a3e962bcdf79db65bea46ab07d365fc2f528c2bf9e7b719627428";
	private const int StandardSliceRowCount = 271984;

	private static readonly TimeSpan _pollInterval = RawLayerGenerator.PollInterval;

	private static readonly int[] _qualityCodes =
	[
		ArchiveRow.OrdinaryQuality,
		ArchiveRow.FirstAfterBreakQuality,
		ArchiveRow.LastBeforeBreakQuality
	];

	[Fact]
	public void ADifferentSeedProducesDifferentRows()
	{
		var first = RawLayerGenerator.Generate(BenchOptions.For());
		var second = RawLayerGenerator.Generate(BenchOptions.For(seed: 2));

		Assert.NotEqual(first, second);
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
		var pens = RawLayerGenerator.SelectPens(8).ToDictionary(pen => (int)pen.PenId);

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
		Assert.Equal(new long[] { 1000, 2000, 3000, 4000, 5000, 1001 }, pens.Select(pen => pen.PenId));
	}

	[Fact]
	public void ASinglePenProducesRowsForThatPenOnly()
	{
		var rows = RawLayerGenerator.Generate(BenchOptions.For(pens: 1));

		Assert.NotEmpty(rows);
		Assert.All(rows, row => Assert.Equal(1000, row.Id));
	}

	[Theory]
	[InlineData(40, 3)]
	[InlineData(50, 5)]
	[InlineData(55, 7)]
	[InlineData(60, 15)]
	public void ASingleRowRunBetweenTwoBreaksGetsASynthesisedStopRow(int breaks, int expected)
	{
		var options = BenchOptions.For(pens: 1, changeSeconds: 600.0, breaks: breaks);
		var rows = BenchRows.ByPen(RawLayerGenerator.Generate(options))[0];

		var synthesised = rows
			.Zip(rows.Skip(1))
			.Count(pair => pair.First.Quality == ArchiveRow.FirstAfterBreakQuality
				&& pair.Second.Quality == ArchiveRow.LastBeforeBreakQuality
				&& pair.Second.Value == pair.First.Value
				&& pair.Second.Timestamp - pair.First.Timestamp == _pollInterval);

		Assert.Equal(expected, synthesised);

		// What the added row buys: the marker sequence stays a strict 32, 16 alternation, which is what
		// every reader of a gap boundary relies on.
		var markers = rows.Where(row => row.Quality != ArchiveRow.OrdinaryQuality).ToArray();

		Assert.Equal(breaks * 2, markers.Length);
		Assert.All(
			markers.Index(),
			marker => Assert.Equal(
				marker.Index % 2 == 0 ? ArchiveRow.LastBeforeBreakQuality : ArchiveRow.FirstAfterBreakQuality,
				marker.Item.Quality));
	}

	// The combination the alternation cannot survive: an archiving run holding no lattice point carries
	// neither marker, and the archive is malformed with nothing else to show for it.
	[Theory]
	[InlineData(20)]
	[InlineData(30)]
	[InlineData(72)]
	public void AChangeIntervalThatLeavesARunWithNoChangeIsRejected(int breaks)
	{
		var options = BenchOptions.For(pens: 1, changeSeconds: 600.0, breaks: breaks);

		var rejected = Assert.Throws<ArgumentOutOfRangeException>(() => RawLayerGenerator.Generate(options));

		Assert.Contains("holds no change", rejected.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void GenerateRejectsZeroDays()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => RawLayerGenerator.Generate(BenchOptions.For(days: 0)));
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

	// A hash rather than a sample: an accidental edit anywhere in the generation code would otherwise
	// change the bench for eight later slices without failing anything. A deliberate waveform change
	// updates this constant in the same commit.
	[Fact]
	public void TheStandardSliceMatchesItsGoldenDigest()
	{
		var rows = RawLayerGenerator.Generate(BenchOptions.For());

		Assert.Equal(StandardSliceRowCount, rows.Count);
		Assert.Equal(StandardSliceDigest, Digest(rows));
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

	// Values are rounded before hashing: Math.Sin may differ by one unit in the last place between
	// platforms, and the digest must survive a Linux runner without hiding a real waveform change.
	private static string Digest(IReadOnlyList<ArchiveRow> rows)
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

		foreach (var row in rows)
		{
			var line = string.Create(
				CultureInfo.InvariantCulture,
				$"{row.Id};{row.Layer};{row.Timestamp:yyyy-MM-ddTHH:mm:ss.fff};{row.Value:F6};{row.Quality}\n");

			hash.AppendData(Encoding.UTF8.GetBytes(line));
		}

		return Convert.ToHexStringLower(hash.GetHashAndReset());
	}
}
