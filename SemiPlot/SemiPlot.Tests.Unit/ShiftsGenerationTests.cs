using AwesomeAssertions;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Unit;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ShiftsGenerationTests
{
	private const double IdleShare = 0.05;

	private static readonly long _intervalTicks = TimeSpan.FromSeconds(30).Ticks;

	private static readonly ShiftSchedule _schedule = ShiftSchedule.For(BenchOptions.Shifts);

	private static readonly Lazy<IReadOnlyList<ArchiveRow>> _shiftRows =
		new(() => RawLayerGenerator.Generate(BenchOptions.Shifts));

	private static readonly Dictionary<int, SyntheticPen> _shiftPens =
		RawLayerGenerator.SelectPens(BenchOptions.Shifts.PenCount).ToDictionary(pen => pen.PenId);

	[Fact]
	public void FourteenDaysOfShiftsHoldNoWeekendRow()
	{
		BenchOptions.Shifts.End.DayOfWeek.Should().Be(DayOfWeek.Friday);
		_shiftRows.Value.Should().NotBeEmpty();

		_shiftRows.Value
			.Where(row => row.Timestamp.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
			.Should().BeEmpty();
	}

	// The shape a project stop leaves on every pen (docs/architecture/scada-archive.md#quality-and-gaps).
	[Fact]
	public void EveryInnerSessionEdgeCarriesItsBreakMarkerAndNoRowFallsBetweenSessions()
	{
		var offPeriods = BreakPlan.Create(BenchOptions.Shifts).Breaks;

		offPeriods.Count.Should().BeGreaterThanOrEqualTo(5);

		foreach (var pen in BenchRows.ByPen(_shiftRows.Value))
		{
			pen.Where(row => offPeriods.Any(off => row.Timestamp >= off.Start && row.Timestamp < off.End))
				.Should().BeEmpty();
			pen.Where(row => row.Quality == ArchiveRow.FirstAfterBreakQuality).Select(row => row.Timestamp)
				.Should().Equal(offPeriods.Select(off => off.End));
			pen.Where(row => row.Quality == ArchiveRow.LastBeforeBreakQuality).Select(row => row.Timestamp)
				.Should().Equal(offPeriods.Select(off => pen.Last(row => row.Timestamp < off.Start).Timestamp));
		}
	}

	[Fact]
	public void TheLastSessionRunsPastItsDrawnEndToTheEnd()
	{
		var lastSession = BreakPlan.Create(BenchOptions.Shifts).Runs[^1];

		_schedule.Session(DateOnly.FromDateTime(lastSession.Start))!.Value.End
			.Should().BeBefore(BenchOptions.Shifts.End);

		foreach (var pen in BenchRows.ByPen(_shiftRows.Value))
		{
			var stride = _shiftPens[pen[0].Id].ChangeStride(_intervalTicks);

			pen[^1].Timestamp.Should().BeOnOrAfter(BenchOptions.Shifts.End.AddTicks(-stride * _intervalTicks))
				.And.BeBefore(BenchOptions.Shifts.End);
		}
	}

	// Computed from the walk and the drawn runs, not through the generator's value lookup; the idle level
	// sits 5% of the stored range above its minimum.
	[Fact]
	public void APenWalksDuringARecipeRunAndHoldsItsIdleLevelBetweenRuns()
	{
		var firstDay = DateOnly.FromDateTime(BenchOptions.Shifts.Start);
		var runs = Enumerable.Range(0, BenchOptions.Shifts.Days + 1)
			.SelectMany(offset => _schedule.RecipeRuns(firstDay.AddDays(offset)))
			.ToArray();
		var walking = 0;
		var idling = 0;

		foreach (var row in _shiftRows.Value)
		{
			var pen = _shiftPens[row.Id];
			var held = BenchRows.HeldIndex(row, pen, _intervalTicks);
			var heldTime = new DateTime(held * _intervalTicks);

			if (runs.Any(run => run.Start <= heldTime && heldTime < run.End))
			{
				row.Value.Should().Be(BenchRows.Walk(BenchOptions.Shifts.Seed, pen, held));
				walking++;
			}
			else
			{
				row.Value.Should().Be(pen.MinValue + (IdleShare * (pen.MaxValue - pen.MinValue)));
				idling++;
			}
		}

		walking.Should().BePositive();
		idling.Should().BePositive();
	}

	// The demo writer stays continuous: its value steps off the idle level at the seam by design, so only
	// the keys, the lattice and the seam are pinned.
	[Fact]
	public void AContinuousFollowRunResumesAShiftSeedWithNoRepeatedRowAndNoHole()
	{
		var seeded = _shiftRows.Value;
		var edges = seeded.GroupBy(row => row.Id).ToDictionary(pen => pen.Key, pen => pen.Max(row => row.Timestamp));
		var following = new FollowOptions(BenchOptions.ConnectionString, TimeSpan.FromSeconds(1),
			BenchOptions.Shifts.PenCount, BenchOptions.Shifts.Seed, BenchOptions.Shifts.ChangeSeconds);

		var followed = LiveTailGenerator.Generate(following, edges.Values.Max(), BenchOptions.Shifts.End.AddMinutes(3));

		followed.Should().NotBeEmpty();

		var keys = seeded.Concat(followed).Select(row => (row.Id, row.Layer, row.Timestamp)).ToArray();

		keys.Length.Should().Be(keys.Distinct().Count());
		followed.Select(row => row.Id).Distinct().Order().Should().Equal(edges.Keys.Order());
		followed.Should().AllSatisfy(row => (row.Timestamp.Ticks % _intervalTicks).Should().BeOneOf(
			0L, _intervalTicks - RawLayerGenerator.PollInterval.Ticks));

		foreach (var pen in followed.GroupBy(row => row.Id))
		{
			var stride = _shiftPens[pen.Key].ChangeStride(_intervalTicks);
			var seam = pen.Min(row => row.Timestamp) - edges[pen.Key];

			seam.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(1.0))
				.And.BeLessThanOrEqualTo(TimeSpan.FromTicks(stride * _intervalTicks));
		}
	}
}
