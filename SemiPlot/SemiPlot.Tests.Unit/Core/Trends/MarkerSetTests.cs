using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Trends;

[Trait("Component", "Core")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class MarkerSetTests
{
	private static readonly DateTime _origin = new(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);

	private static readonly StoredMarker _etchStart = new(1, _origin, "Etch start");

	private static readonly StoredMarker _unlabelled = new(2, _origin.AddMinutes(10), null);

	private static readonly StoredMarker _etchEnd = new(3, _origin.AddMinutes(20), "Etch end");

	[Fact]
	public void Apply_OfAnUnchangedRead_ReturnsFalse()
	{
		var set = SetOf(_etchStart, _unlabelled);

		var changed = set.Apply([_etchStart with { }, _unlabelled with { }]);

		changed.Should().BeFalse();
		set.ShownMarkers.Should().Equal(_etchStart, _unlabelled);
	}

	[Fact]
	public void Apply_OfAMovedMarker_ReturnsTrueAndSortsByTheNewMoment()
	{
		var set = SetOf(_etchStart, _unlabelled);
		var moved = _etchStart with { TimeUtc = _origin.AddMinutes(15) };

		var changed = set.Apply([moved, _unlabelled]);

		changed.Should().BeTrue();
		set.ShownMarkers.Should().Equal(_unlabelled, moved);
	}

	[Fact]
	public void Apply_OfARelabelledMarker_ReturnsTrueWithTheNewLabel()
	{
		var set = SetOf(_etchStart);
		var relabelled = _etchStart with { Label = "Etch begin" };

		set.Apply([relabelled]).Should().BeTrue();

		set.ShownMarkers.Should().Equal(relabelled);
	}

	[Fact]
	public void Apply_OfAnAddedMarker_ReturnsTrueWithTheMarkerInTimeOrder()
	{
		var set = SetOf(_etchStart, _etchEnd);

		set.Apply([_etchEnd, _unlabelled, _etchStart]).Should().BeTrue();

		set.ShownMarkers.Should().Equal(_etchStart, _unlabelled, _etchEnd);
	}

	[Fact]
	public void Apply_OfARemovedMarker_ReturnsTrueWithoutIt()
	{
		var set = SetOf(_etchStart, _unlabelled);

		set.Apply([_unlabelled]).Should().BeTrue();

		set.ShownMarkers.Should().Equal(_unlabelled);
	}

	[Fact]
	public void Apply_OrdersMarkersOfOneMomentById()
	{
		var set = new MarkerSet();
		var later = new StoredMarker(9, _origin, "Later id");

		set.Apply([later, _etchStart]);

		set.ShownMarkers.Should().Equal(_etchStart, later);
	}

	[Fact]
	public void AHeldMarker_KeepsItsLocalMomentAndLabelThroughApply()
	{
		var set = SetOf(_etchStart);
		var dragged = _etchStart with { TimeUtc = _origin.AddMinutes(5), Label = "Dragged" };
		set.Hold(_etchStart.Id);
		set.Place(dragged);

		set.Apply([_etchStart, _etchEnd]).Should().BeTrue();

		set.ShownMarkers.Should().Equal(dragged, _etchEnd);
	}

	[Fact]
	public void AHeldMarker_StaysWhenAReadOmitsIt()
	{
		var set = SetOf(_etchStart, _unlabelled);
		set.Hold(_etchStart.Id);

		set.Apply([_unlabelled]).Should().BeFalse();

		set.ShownMarkers.Should().Equal(_etchStart, _unlabelled);
	}

	[Fact]
	public void TwoHoldsAndOneRelease_KeepTheMarkerHeld()
	{
		var set = SetOf(_etchStart);
		set.Hold(_etchStart.Id);
		set.Hold(_etchStart.Id);

		set.Release(_etchStart.Id).Should().BeFalse();
		set.Apply([]);

		set.ShownMarkers.Should().Equal(_etchStart);
	}

	[Fact]
	public void TheSecondRelease_ReportsZeroAndLetsTheNextReadRemoveTheMarker()
	{
		var set = SetOf(_etchStart);
		set.Hold(_etchStart.Id);
		set.Hold(_etchStart.Id);
		set.Release(_etchStart.Id);

		set.Release(_etchStart.Id).Should().BeTrue();
		set.Apply([]).Should().BeTrue();

		set.ShownMarkers.Should().BeEmpty();
	}

	[Fact]
	public void Place_OfAnIdApplyAlreadyAdded_LeavesOneMarker()
	{
		var set = SetOf(_etchStart, _unlabelled);

		set.Place(_unlabelled);

		set.ShownMarkers.Should().Equal(_etchStart, _unlabelled);
	}

	[Fact]
	public void Place_OfAMovedMarker_ReplacesItAtItsNewPosition()
	{
		var set = SetOf(_etchStart, _unlabelled, _etchEnd);
		var moved = _etchStart with { TimeUtc = _origin.AddMinutes(30) };

		set.Place(moved);

		set.ShownMarkers.Should().Equal(_unlabelled, _etchEnd, moved);
	}

	[Fact]
	public void Place_AtAnotherMarkersMoment_OrdersTheTwoById()
	{
		var set = SetOf(_etchStart, _etchEnd);
		var lowerId = new StoredMarker(0, _etchEnd.TimeUtc, null);
		var higherId = new StoredMarker(9, _etchStart.TimeUtc, null);

		set.Place(lowerId);
		set.Place(higherId);

		set.ShownMarkers.Should().Equal(_etchStart, higherId, lowerId, _etchEnd);
	}

	[Fact]
	public void Remove_DropsTheMarker()
	{
		var set = SetOf(_etchStart, _unlabelled);

		set.Remove(_etchStart.Id);

		set.ShownMarkers.Should().Equal(_unlabelled);
	}

	[Fact]
	public void ShownMarkers_IsASnapshotThatAChangeDoesNotMutate()
	{
		var set = SetOf(_etchStart);
		var snapshot = set.ShownMarkers;

		set.Place(_etchEnd);

		snapshot.Should().Equal(_etchStart);
	}

	[Fact]
	public void NearestWithin_ReturnsTheCloserOfTwoNeighbours()
	{
		var set = SetOf(_etchStart, _unlabelled, _etchEnd);

		set.NearestWithin(_origin.AddMinutes(13), TimeSpan.FromMinutes(5)).Should().Be(_unlabelled);
		set.NearestWithin(_origin.AddMinutes(17), TimeSpan.FromMinutes(5)).Should().Be(_etchEnd);
	}

	[Fact]
	public void NearestWithin_ReturnsNothingOutsideTheTolerance()
	{
		var set = SetOf(_etchStart, _unlabelled);

		set.NearestWithin(_origin.AddMinutes(5), TimeSpan.FromMinutes(4)).Should().BeNull();
		set.NearestWithin(_origin.AddMinutes(-2), TimeSpan.FromMinutes(1)).Should().BeNull();
		set.NearestWithin(_origin.AddMinutes(12), TimeSpan.FromMinutes(1)).Should().BeNull();
	}

	[Fact]
	public void NearestWithin_AcceptsADistanceEqualToTheTolerance()
	{
		var set = SetOf(_etchStart);

		set.NearestWithin(_origin.AddMinutes(1), TimeSpan.FromMinutes(1)).Should().Be(_etchStart);
	}

	[Fact]
	public void NearestWithin_ReturnsTheLowerIdOfTwoEquallyDistantNeighbours()
	{
		var before = new StoredMarker(7, _origin.AddMinutes(-1), null);
		var after = new StoredMarker(4, _origin.AddMinutes(1), null);
		var set = SetOf(before, after);

		set.NearestWithin(_origin, TimeSpan.FromMinutes(2)).Should().Be(after);
	}

	[Fact]
	public void NearestWithin_ReturnsTheLowerIdOfTwoMarkersAtOneMoment()
	{
		var first = new StoredMarker(3, _origin.AddMinutes(-1), null);
		var second = new StoredMarker(8, _origin.AddMinutes(-1), null);
		var set = SetOf(second, first);

		set.NearestWithin(_origin, TimeSpan.FromMinutes(2)).Should().Be(first);
	}

	[Fact]
	public void NearestWithin_OfAnEmptySet_ReturnsNothing()
	{
		new MarkerSet().NearestWithin(_origin, TimeSpan.FromHours(1)).Should().BeNull();
	}

	[Fact]
	public void HiddenRecipeMarkers_LeaveTheShownMarkersAndTheHitTest_AndStayInAll()
	{
		var recipeStart = new StoredMarker(4, _origin.AddMinutes(5), "Etch.csv", MarkerKind.RecipeStart, true);
		var recipeEnd = new StoredMarker(5, _origin.AddMinutes(25), "Etch.csv", MarkerKind.RecipeEnd, true);
		var set = SetOf(_etchStart, recipeStart, _etchEnd, recipeEnd);

		set.ToggleRecipeMarkers();

		set.ShowsRecipeMarkers.Should().BeFalse();
		set.ShownMarkers.Should().Equal(_etchStart, _etchEnd);
		set.All.Should().Equal(_etchStart, recipeStart, _etchEnd, recipeEnd);
		set.NearestWithin(recipeStart.TimeUtc, TimeSpan.FromMinutes(1)).Should().BeNull();
		set.NearestWithin(recipeEnd.TimeUtc, TimeSpan.FromMinutes(6)).Should().Be(_etchEnd);
	}

	[Fact]
	public void ShowingRecipeMarkersAgain_BringsThemBack()
	{
		var recipeStart = new StoredMarker(4, _origin.AddMinutes(5), "Etch.csv", MarkerKind.RecipeStart, true);
		var set = SetOf(_etchStart, recipeStart);
		set.ToggleRecipeMarkers();

		set.ToggleRecipeMarkers();

		set.ShownMarkers.Should().Equal(_etchStart, recipeStart);
		set.NearestWithin(recipeStart.TimeUtc, TimeSpan.FromMinutes(1)).Should().Be(recipeStart);
	}

	[Fact]
	public void WhileRecipeMarkersAreHidden_AReadAndAPlaceKeepThemOutOfTheShownMarkers()
	{
		var recipeStart = new StoredMarker(4, _origin.AddMinutes(5), "Etch.csv", MarkerKind.RecipeStart, true);
		var recipeEnd = new StoredMarker(5, _origin.AddMinutes(25), "Etch.csv", MarkerKind.RecipeEnd, true);
		var set = SetOf(_etchStart);
		set.ToggleRecipeMarkers();

		set.Apply([_etchStart, recipeStart]).Should().BeTrue();
		set.Place(recipeEnd);

		set.ShownMarkers.Should().Equal(_etchStart);
		set.All.Should().Equal(_etchStart, recipeStart, recipeEnd);
	}

	[Fact]
	public void WhileRecipeMarkersAreHidden_APlaceOfAUserMarker_MovesItAmongTheShownMarkers()
	{
		var recipeStart = new StoredMarker(4, _origin.AddMinutes(5), "Etch.csv", MarkerKind.RecipeStart, true);
		var set = SetOf(_etchStart, recipeStart, _etchEnd);
		set.ToggleRecipeMarkers();
		var moved = _etchStart with { TimeUtc = _origin.AddMinutes(30) };

		set.Place(moved);

		set.ShownMarkers.Should().Equal(_etchEnd, moved);
		set.All.Should().Equal(recipeStart, _etchEnd, moved);
	}

	[Fact]
	public void ShownMarkers_ReturnsTheSameArrayBetweenTwoChanges()
	{
		var recipeStart = new StoredMarker(4, _origin.AddMinutes(5), "Etch.csv", MarkerKind.RecipeStart, true);
		var set = SetOf(_etchStart, recipeStart);
		set.ToggleRecipeMarkers();

		set.ShownMarkers.Should().BeSameAs(set.ShownMarkers);
	}

	private static MarkerSet SetOf(params StoredMarker[] markers)
	{
		var set = new MarkerSet();
		set.Apply(markers);

		return set;
	}
}
