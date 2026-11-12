using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Unit;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ShiftMarkerWriterTests
{
	private static readonly ShiftSchedule _schedule = ShiftSchedule.For(BenchOptions.Shifts);

	private static readonly DateOnly _firstDay = new(2026, 1, 5);

	// The six-month stand's span shape; the plans generate no row, so the length costs nothing.
	private static readonly SeederOptions _sixMonths = BenchOptions.Shifts with
	{
		End = new DateTime(2026, 11, 10, 15, 0, 0, DateTimeKind.Unspecified),
		Days = 183
	};

	[Fact]
	public void EveryRecipeRunOfTheSpanHasOneStartAndOneClosingMarker()
	{
		var firstDay = DateOnly.FromDateTime(BenchOptions.Shifts.Start);
		var runs = Enumerable.Range(0, BenchOptions.Shifts.Days)
			.SelectMany(offset => _schedule.RecipeRuns(firstDay.AddDays(offset)))
			.ToArray();

		var markers = ShiftMarkerWriter.RecipePlan(BenchOptions.Shifts);

		runs.Should().NotBeEmpty();
		markers.Should().HaveCount(2 * runs.Length);

		for (var index = 0; index < runs.Length; index++)
		{
			var (start, closing) = (markers[2 * index], markers[(2 * index) + 1]);

			start.Should().Be(new SeededMarker(runs[index].Start, start.Label, MarkerKind.RecipeStart, IsLocked: true));
			closing.Should().Be(new SeededMarker(
				runs[index].End,
				start.Label,
				runs[index].IsInterrupted ? MarkerKind.RecipeInterrupted : MarkerKind.RecipeEnd,
				IsLocked: true));
		}

		markers.Select(marker => marker.Kind).Should()
			.Contain(MarkerKind.RecipeInterrupted).And.Contain(MarkerKind.RecipeEnd);
	}

	[Fact]
	public void ARunStillOpenAtTheEndGetsNoMarker()
	{
		var day = Enumerable.Range(14, 14).Select(_firstDay.AddDays).First(_schedule.IsOn);
		var runs = _schedule.RecipeRuns(day);
		var open = runs[2];
		var options = BenchOptions.Shifts with { End = open.Start + ((open.End - open.Start) / 2) };

		var markers = ShiftMarkerWriter.RecipePlan(options);

		markers[^1].Time.Should().Be(runs[1].End);
		markers.Should().NotContain(marker => marker.Time >= open.Start);
	}

	[Fact]
	public void AContinuousSeedPlansNoMarker()
	{
		var continuous = BenchOptions.Shifts with { Schedule = ArchiveSchedule.Continuous };

		ShiftMarkerWriter.RecipePlan(continuous).Should().BeEmpty();
		ShiftMarkerWriter.OperatorPlan(continuous).Should().BeEmpty();
	}

	[Fact]
	public void AWorkingWeekCarriesTwoOrThreeOperatorMarkers_OnDaysOfTheirOwn()
	{
		var markers = ShiftMarkerWriter.OperatorPlan(_sixMonths);
		var firstMonday = Monday(DateOnly.FromDateTime(_sixMonths.Start)).AddDays(7);
		var lastMonday = Monday(DateOnly.FromDateTime(_sixMonths.End)).AddDays(-7);
		var fullWeeks = 0;

		for (var monday = firstMonday; monday <= lastMonday; monday = monday.AddDays(7))
		{
			var sessions = Enumerable.Range(0, 7).Select(monday.AddDays).Count(_schedule.IsOn);
			var inWeek = markers.Where(marker => Monday(DateOnly.FromDateTime(marker.Time)) == monday).ToArray();

			inWeek.Length.Should().BeInRange(Math.Min(sessions, 2), Math.Min(sessions, 3), $"the week of {monday}");
			inWeek.Select(marker => DateOnly.FromDateTime(marker.Time)).Should().OnlyHaveUniqueItems();
			fullWeeks++;
		}

		fullWeeks.Should().BeGreaterThanOrEqualTo(24);
	}

	[Fact]
	public void EveryOperatorMarkerSitsInsideASession_OnAWholeSecond()
	{
		var sessions = BreakPlan.Create(_sixMonths).Runs;

		var markers = ShiftMarkerWriter.OperatorPlan(_sixMonths);

		markers.Should().NotBeEmpty();
		markers.Should().AllSatisfy(marker =>
		{
			sessions.Should().Contain(session => session.Start <= marker.Time && marker.Time < session.End);
			(marker.Time.Ticks % TimeSpan.TicksPerSecond).Should().Be(0);
		});
		markers.Select(marker => marker.Time).Should().BeInAscendingOrder();
	}

	[Fact]
	public void OperatorMarkersAreUnlockedUserMarkers_SomeWithoutALabel()
	{
		var markers = ShiftMarkerWriter.OperatorPlan(_sixMonths);

		markers.Should().OnlyContain(marker => marker.Kind == MarkerKind.User && !marker.IsLocked);
		markers.Should().Contain(marker => marker.Label == null);
		markers.Where(marker => marker.Label != null).Should()
			.OnlyContain(marker => marker.Label!.Length >= 1 && marker.Label.Length <= 80);
		markers.Select(marker => marker.Label).OfType<string>().Distinct().Should().HaveCountGreaterThan(3);
	}

	[Fact]
	public void TheOperatorPlanIsPureInTheSeed_AndALaterEndMovesNoMarker()
	{
		var markers = ShiftMarkerWriter.OperatorPlan(_sixMonths);
		var later = ShiftMarkerWriter.OperatorPlan(_sixMonths with { End = _sixMonths.End.AddDays(14), Days = 197 });
		var otherSeed = ShiftMarkerWriter.OperatorPlan(_sixMonths with { Seed = _sixMonths.Seed + 1 });

		ShiftMarkerWriter.OperatorPlan(_sixMonths).Should().Equal(markers);
		later.Take(markers.Count).Should().Equal(markers);
		later.Should().HaveCountGreaterThan(markers.Count);
		otherSeed.Should().NotEqual(markers);
	}

	private static DateOnly Monday(DateOnly day)
	{
		return DateOnly.FromDayNumber(day.DayNumber / 7 * 7);
	}
}
