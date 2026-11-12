using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Trends;

[Trait("Component", "Core")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class MarkerNavigationTests
{
	private static readonly DateTime _origin = new(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);

	private long _nextId = 1;

	[Fact]
	public void Runs_PairAStartThroughItsEnd_PastAnOperatorMarker()
	{
		var markers = Markers(
			Marker(0, MarkerKind.RecipeStart),
			Marker(5, MarkerKind.User),
			Marker(20, MarkerKind.RecipeEnd));

		var runs = MarkerNavigation.Runs(markers, At(60));

		runs.Should().Equal(new RecipeRun(At(0), At(20), IsInterrupted: false));
	}

	[Fact]
	public void Runs_TakeTheStartMarkersLabel_AndNoneForABlankOne()
	{
		var markers = Markers(
			Marker(0, MarkerKind.RecipeStart) with { Label = "Etch_Si.csv" },
			Marker(20, MarkerKind.RecipeEnd) with { Label = "Closing.csv" },
			Marker(30, MarkerKind.RecipeStart) with { Label = "  " },
			Marker(40, MarkerKind.RecipeEnd));

		var runs = MarkerNavigation.Runs(markers, At(60));

		runs.Select(run => run.Label).Should().Equal("Etch_Si.csv", null);
	}

	[Fact]
	public void Runs_PairAStartThroughItsInterruption_AsOneInterruptedRun()
	{
		var markers = Markers(Marker(0, MarkerKind.RecipeStart), Marker(12, MarkerKind.RecipeInterrupted));

		var runs = MarkerNavigation.Runs(markers, At(60));

		runs.Should().Equal(new RecipeRun(At(0), At(12), IsInterrupted: true));
	}

	[Fact]
	public void Runs_RunAStartWithNoClose_ToTheNextStart()
	{
		var markers = Markers(
			Marker(0, MarkerKind.RecipeStart),
			Marker(30, MarkerKind.RecipeStart),
			Marker(45, MarkerKind.RecipeEnd));

		var runs = MarkerNavigation.Runs(markers, At(60));

		runs.Should().Equal(
			new RecipeRun(At(0), At(30), IsInterrupted: false),
			new RecipeRun(At(30), At(45), IsInterrupted: false));
	}

	[Fact]
	public void Runs_RunTheLastStartWithNoClose_ToTheNewestSample()
	{
		var markers = Markers(
			Marker(0, MarkerKind.RecipeStart),
			Marker(10, MarkerKind.RecipeEnd),
			Marker(30, MarkerKind.RecipeStart));

		var runs = MarkerNavigation.Runs(markers, At(52));

		runs.Should().Equal(
			new RecipeRun(At(0), At(10), IsInterrupted: false),
			new RecipeRun(At(30), At(52), IsInterrupted: false));
	}

	[Theory]
	[InlineData(null)]
	[InlineData(30)]
	[InlineData(25)]
	public void Runs_LeaveOutAnOpenLastStart_WithNoNewestSampleAfterIt(int? newestSampleMinute)
	{
		var markers = Markers(Marker(30, MarkerKind.RecipeStart));
		DateTime? newestSampleUtc = newestSampleMinute is { } minute ? At(minute) : null;

		var runs = MarkerNavigation.Runs(markers, newestSampleUtc);

		runs.Should().BeEmpty();
	}

	[Fact]
	public void Runs_SkipACloseWithNoStart()
	{
		var markers = Markers(
			Marker(0, MarkerKind.RecipeEnd),
			Marker(10, MarkerKind.RecipeStart),
			Marker(20, MarkerKind.RecipeEnd),
			Marker(25, MarkerKind.RecipeInterrupted),
			Marker(40, MarkerKind.RecipeStart),
			Marker(50, MarkerKind.RecipeEnd));

		var runs = MarkerNavigation.Runs(markers, At(60));

		runs.Should().Equal(
			new RecipeRun(At(10), At(20), IsInterrupted: false),
			new RecipeRun(At(40), At(50), IsInterrupted: false));
	}

	[Fact]
	public void Runs_SkipARunOfNoLength()
	{
		var markers = Markers(
			Marker(10, MarkerKind.RecipeStart),
			Marker(10, MarkerKind.RecipeEnd),
			Marker(20, MarkerKind.RecipeStart),
			Marker(30, MarkerKind.RecipeEnd));

		var runs = MarkerNavigation.Runs(markers, At(60));

		runs.Should().Equal(new RecipeRun(At(20), At(30), IsInterrupted: false));
	}

	[Fact]
	public void Runs_OfNoRecipeMarker_AreEmpty()
	{
		var markers = Markers(Marker(10, MarkerKind.User), Marker(20, MarkerKind.User));

		var runs = MarkerNavigation.Runs(markers, At(60));

		runs.Should().BeEmpty();
	}

	[Fact]
	public void RunSteps_FromAWindowCentreBetweenRuns_TakeTheRunsOnEitherSide()
	{
		var runs = ThreeRuns();

		var next = MarkerNavigation.NextRun(runs, At(25));
		var previous = MarkerNavigation.PreviousRun(runs, At(25));

		next.Should().Be(runs[1]);
		previous.Should().Be(runs[0]);
	}

	[Fact]
	public void RunSteps_FromTheCentreOfAFramedRun_TakeItsNeighbours_AndNoneAtEitherEnd()
	{
		var runs = ThreeRuns();

		var forward = new List<RecipeRun?>();
		var backward = new List<RecipeRun?>();
		foreach (var run in runs)
		{
			var centre = Centre(MarkerNavigation.Frame(run));
			forward.Add(MarkerNavigation.NextRun(runs, centre));
			backward.Add(MarkerNavigation.PreviousRun(runs, centre));
		}

		forward.Should().Equal(runs[1], runs[2], null);
		backward.Should().Equal(null, runs[0], runs[1]);
	}

	[Fact]
	public void RunSteps_FromACentreInsideARun_PassOverThatRun()
	{
		var runs = ThreeRuns();

		var next = MarkerNavigation.NextRun(runs, At(32));
		var previous = MarkerNavigation.PreviousRun(runs, At(32));

		next.Should().Be(runs[2]);
		previous.Should().Be(runs[0]);
	}

	[Fact]
	public void RunSteps_PastTheOuterRuns_FindNone()
	{
		var runs = ThreeRuns();

		MarkerNavigation.NextRun(runs, At(75)).Should().BeNull();
		MarkerNavigation.PreviousRun(runs, At(-5)).Should().BeNull();
		MarkerNavigation.NextRun([], At(0)).Should().BeNull();
		MarkerNavigation.PreviousRun([], At(0)).Should().BeNull();
	}

	[Fact]
	public void LatestRun_IsTheNewestRun_OrNoneWithoutRuns()
	{
		var runs = ThreeRuns();

		MarkerNavigation.LatestRun(runs).Should().Be(runs[2]);
		MarkerNavigation.LatestRun([]).Should().BeNull();
	}

	[Fact]
	public void Frame_AddsATenthOfTheRunOnEachSide()
	{
		var run = new RecipeRun(At(10), At(30), IsInterrupted: false);

		var (fromUtc, toUtc) = MarkerNavigation.Frame(run);

		fromUtc.Should().Be(At(8));
		toUtc.Should().Be(At(32));
	}

	[Fact]
	public void MarkerSteps_FromACentreOnAMarker_MoveOffIt()
	{
		var markers = Markers(Marker(0, MarkerKind.User), Marker(10, MarkerKind.User), Marker(20, MarkerKind.User));

		var next = MarkerNavigation.NextMarker(markers, At(10));
		var previous = MarkerNavigation.PreviousMarker(markers, At(10));

		next.Should().Be(markers[2]);
		previous.Should().Be(markers[0]);
	}

	[Fact]
	public void MarkerSteps_PastTheOuterMarkers_FindNone()
	{
		var markers = Markers(Marker(10, MarkerKind.User));

		MarkerNavigation.NextMarker(markers, At(10)).Should().BeNull();
		MarkerNavigation.PreviousMarker(markers, At(10)).Should().BeNull();
		MarkerNavigation.NextMarker([], At(0)).Should().BeNull();
		MarkerNavigation.PreviousMarker([], At(0)).Should().BeNull();
	}

	private static IReadOnlyList<RecipeRun> ThreeRuns()
	{
		return
		[
			new RecipeRun(At(0), At(20), IsInterrupted: false),
			new RecipeRun(At(30), At(40), IsInterrupted: true),
			new RecipeRun(At(50), At(70), IsInterrupted: false)
		];
	}

	private static IReadOnlyList<StoredMarker> Markers(params StoredMarker[] markers)
	{
		return markers;
	}

	private StoredMarker Marker(int minute, MarkerKind kind)
	{
		return new StoredMarker(_nextId++, At(minute), null, kind, IsLocked: kind != MarkerKind.User);
	}

	private static DateTime At(int minute)
	{
		return _origin.AddMinutes(minute);
	}

	private static DateTime Centre((DateTime FromUtc, DateTime ToUtc) window)
	{
		return window.FromUtc + ((window.ToUtc - window.FromUtc) / 2);
	}
}
