using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.Tests.Unit.UI.Markers;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Messages;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.ChartTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartMarkersTests : IDisposable
{
	private static readonly DateTime _eight = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _nine = _eight.AddHours(1);
	private static readonly DateTime _ten = _eight.AddHours(2);
	private static readonly StoredMarker _etchStart = new(1, _eight, "Etch start");
	private static readonly StoredMarker _etchEnd = new(2, _ten, null);

	private readonly FakeMarkerChannel _channel = new();
	private readonly MessagePanelViewModel _messagePanel = new();
	private readonly TrendChartViewModel _chart;

	public ChartMarkersTests()
	{
		_chart = CreateViewModel(messagePanel: _messagePanel, markerChannel: _channel).ViewModel;
	}

	private ChartMarkers Markers => _chart.Markers;

	public void Dispose()
	{
		_chart.Dispose();
		_messagePanel.Dispose();
	}

	[AvaloniaFact]
	public async Task AnAdd_PlacesTheMarkerWithTheReturnedId()
	{
		_channel.AddResult = Result.Ok(7L);

		await Markers.AddAtAsync(_nine);

		_channel.Writes.Should().Equal(new FakeMarkerWrite.Add(_nine, "#1"));
		Markers.ShownMarkers.Should().Equal(new StoredMarker(7, _nine, "#1"));
	}

	[AvaloniaTheory]
	[InlineData(new[] { "#5", "Etch start" }, "#6")]
	[InlineData(new[] { "#2", "#10", "#3" }, "#11")]
	[InlineData(new[] { "Etch start", "#-4", "#+4", "#4.5", "#", "7", "# 7" }, "#1")]
	[InlineData(new[] { "#007" }, "#8")]
	public async Task AnAdd_LabelsTheMarkerOnePastTheHighestNumberShown(string[] shownLabels, string expected)
	{
		_channel.Push([.. shownLabels.Select((label, index) => new StoredMarker(index + 1, _eight, label))]);
		_channel.AddResult = Result.Ok(100L);

		await Markers.AddAtAsync(_nine);

		_channel.Writes.Should().Equal(new FakeMarkerWrite.Add(_nine, expected));
	}

	[AvaloniaFact]
	public async Task AnAdd_NumbersPastTheUserMarkers_NotPastARecipeMarker()
	{
		_channel.Push(
			new StoredMarker(1, _eight, "#3"),
			new StoredMarker(2, _nine, "#9", MarkerKind.RecipeStart, IsLocked: true));
		_channel.AddResult = Result.Ok(3L);

		await Markers.AddAtAsync(_ten);

		_channel.Writes.Should().Equal(new FakeMarkerWrite.Add(_ten, "#4"));
	}

	[AvaloniaFact]
	public async Task AnAdd_AfterTheHighestNumberWasRenamed_TakesTheNextNumberOfTheRest()
	{
		var second = new StoredMarker(2, _nine, "#2");
		_channel.Push(new StoredMarker(1, _eight, "#1"), second);
		await Markers.RenameAsync(second, "Etch start");
		_channel.AddResult = Result.Ok(3L);

		await Markers.AddAtAsync(_ten);

		_channel.Writes.Last().Should().Be(new FakeMarkerWrite.Add(_ten, "#2"));
	}

	[AvaloniaFact]
	public async Task AFailedAdd_PlacesNothing()
	{
		var changes = CountChanges();
		_channel.AddResult = Result.Fail<long>(FakeDataProvider.UnreachableArchive());

		await Markers.AddAtAsync(_nine);

		Markers.ShownMarkers.Should().BeEmpty();
		changes().Should().Be(0);
	}

	[AvaloniaFact]
	public async Task AReadLandingBeforeTheAddsId_LeavesOneMarker()
	{
		var heldAdd = _channel.HoldNextWrite();
		_channel.AddResult = Result.Ok(7L);
		var added = new StoredMarker(7, _nine, "#1");

		var adding = Markers.AddAtAsync(_nine);
		Markers.ShownMarkers.Should().BeEmpty("a pending add draws nothing until its id lands");
		_channel.Push(added);
		heldAdd.SetResult();
		await adding;

		Markers.ShownMarkers.Should().Equal(added);
	}

	[AvaloniaFact]
	public async Task AFailedMove_RestoresTheStoredMoment()
	{
		_channel.Push(_etchStart);
		_channel.WriteResult = Result.Fail(FakeDataProvider.UnreachableArchive());

		Markers.BeginMove(_etchStart);
		Markers.Move(_nine);
		Markers.ShownMarkers.Should().Equal([_etchStart with { TimeUtc = _nine }], "the line follows the pointer");
		await Markers.EndMoveAsync();

		_channel.Writes.Should().Equal(new FakeMarkerWrite.Move(_etchStart, _nine));
		Markers.ShownMarkers.Should().Equal(_etchStart);
	}

	[AvaloniaFact]
	public async Task AFailedMove_DuringASecondDragOfTheSameMarker_KeepsTheSecondDragsMoment()
	{
		_channel.Push(_etchStart);
		_channel.WriteResult = Result.Fail(FakeDataProvider.UnreachableArchive());
		var heldMove = _channel.HoldNextWrite();

		Markers.BeginMove(_etchStart);
		Markers.Move(_nine);
		var firstMove = Markers.EndMoveAsync();
		Markers.BeginMove(Markers.ShownMarkers[0]);
		Markers.Move(_ten);
		heldMove.SetResult();
		await firstMove;

		Markers.ShownMarkers.Should().Equal(_etchStart with { TimeUtc = _ten });
	}

	[AvaloniaFact]
	public async Task AMoveOfAGoneMarker_RemovesIt()
	{
		_channel.Push(_etchStart, _etchEnd);
		_channel.WriteResult = Result.Fail(FakeDataProvider.BenchArchiveError(ArchiveFault.MarkerGone, "Etch start"));

		Markers.BeginMove(_etchStart);
		Markers.Move(_nine);
		await Markers.EndMoveAsync();

		Markers.ShownMarkers.Should().Equal(_etchEnd);
	}

	[AvaloniaFact]
	public async Task ARenameOfAGoneMarker_RemovesIt()
	{
		_channel.Push(_etchStart, _etchEnd);
		_channel.WriteResult = Result.Fail(FakeDataProvider.BenchArchiveError(ArchiveFault.MarkerGone, "Etch start"));

		await Markers.RenameAsync(_etchStart, "Etch end");

		Markers.ShownMarkers.Should().Equal(_etchEnd);
	}

	[AvaloniaFact]
	public async Task AReadAfterALandedMove_AppliesAgain()
	{
		_channel.Push(_etchStart);
		Markers.BeginMove(_etchStart);
		Markers.Move(_nine);
		await Markers.EndMoveAsync();

		_channel.Push(_etchStart with { TimeUtc = _ten });

		Markers.ShownMarkers.Should().Equal([_etchStart with { TimeUtc = _ten }], "the landed move released its hold");
	}

	[AvaloniaFact]
	public async Task AReadAfterALandedRename_AppliesAgain()
	{
		_channel.Push(_etchStart);
		await Markers.RenameAsync(_etchStart, "Etch end");

		_channel.Push(_etchStart with { Label = "Purge" });

		Markers.ShownMarkers.Should().Equal([_etchStart with { Label = "Purge" }], "the landed rename released its hold");
	}

	[AvaloniaFact]
	public async Task ARename_ShowsTheNewLabel()
	{
		_channel.Push(_etchStart);

		await Markers.RenameAsync(_etchStart, "Etch end");

		_channel.Writes.Should().Equal(new FakeMarkerWrite.Rename(_etchStart, "Etch end"));
		Markers.ShownMarkers.Should().Equal(_etchStart with { Label = "Etch end" });
	}

	[AvaloniaTheory]
	[InlineData("  Etch end  ", "Etch end")]
	[InlineData("   ", null)]
	public async Task ATypedLabel_IsStoredWithoutItsSurroundingBlanks(string typed, string? stored)
	{
		_channel.Push(_etchStart);

		await Markers.RenameAsync(_etchStart, typed);

		_channel.Writes.Should().Equal(new FakeMarkerWrite.Rename(_etchStart, stored));
		Markers.ShownMarkers.Should().Equal(_etchStart with { Label = stored });
	}

	[AvaloniaFact]
	public async Task AFailedRename_RestoresTheLabel()
	{
		_channel.Push(_etchStart);
		_channel.WriteResult = Result.Fail(FakeDataProvider.UnreachableArchive());

		await Markers.RenameAsync(_etchStart, "Etch end");

		Markers.ShownMarkers.Should().Equal(_etchStart);
	}

	[AvaloniaFact]
	public async Task ALock_ShowsAtOnce_BeforeTheWriteLands()
	{
		_channel.Push(_etchStart);
		var heldLock = _channel.HoldNextWrite();

		var locking = Markers.ToggleLockAsync(_etchStart);
		Markers.ShownMarkers.Should().Equal(_etchStart with { IsLocked = true });
		heldLock.SetResult();
		await locking;

		_channel.Writes.Should().Equal(new FakeMarkerWrite.Lock(_etchStart, true));
		Markers.ShownMarkers.Should().Equal(_etchStart with { IsLocked = true });
	}

	[AvaloniaFact]
	public async Task AReadDuringALock_DoesNotUndoTheShownLock()
	{
		_channel.Push(_etchStart);
		var heldLock = _channel.HoldNextWrite();

		var locking = Markers.ToggleLockAsync(_etchStart);
		_channel.Push(_etchStart);
		Markers.ShownMarkers.Should().Equal(_etchStart with { IsLocked = true });
		heldLock.SetResult();
		await locking;
	}

	[AvaloniaFact]
	public async Task AFailedUnlock_RestoresTheLock()
	{
		var locked = _etchStart with { IsLocked = true };
		_channel.Push(locked);
		_channel.WriteResult = Result.Fail(FakeDataProvider.UnreachableArchive());

		await Markers.ToggleLockAsync(locked);

		_channel.Writes.Should().Equal(new FakeMarkerWrite.Lock(locked, false));
		Markers.ShownMarkers.Should().Equal(locked);
	}

	[AvaloniaFact]
	public async Task ALockOfAGoneMarker_RemovesIt()
	{
		_channel.Push(_etchStart, _etchEnd);
		_channel.WriteResult = Result.Fail(FakeDataProvider.BenchArchiveError(ArchiveFault.MarkerGone, "Etch start"));

		await Markers.ToggleLockAsync(_etchStart);

		Markers.ShownMarkers.Should().Equal(_etchEnd);
	}

	[AvaloniaFact]
	public async Task ALock_StartsFromTheMarkerTheSetHolds_NotTheOneTheMenuSaw()
	{
		var renamedMeanwhile = _etchStart with { Label = "Purge" };
		_channel.Push(renamedMeanwhile);

		await Markers.ToggleLockAsync(_etchStart);

		_channel.Writes.Should().Equal(new FakeMarkerWrite.Lock(renamedMeanwhile, true));
		Markers.ShownMarkers.Should().Equal(renamedMeanwhile with { IsLocked = true });
	}

	[AvaloniaFact]
	public async Task ADelete_RemovesTheMarker()
	{
		_channel.Push(_etchStart, _etchEnd);

		await Markers.DeleteAsync(_etchStart);

		_channel.Writes.Should().Equal(new FakeMarkerWrite.Delete(_etchStart));
		Markers.ShownMarkers.Should().Equal(_etchEnd);
	}

	[AvaloniaFact]
	public async Task AFailedDelete_KeepsTheMarker()
	{
		_channel.Push(_etchStart);
		_channel.WriteResult = Result.Fail(FakeDataProvider.UnreachableArchive());

		await Markers.DeleteAsync(_etchStart);

		Markers.ShownMarkers.Should().Equal(_etchStart);
	}

	[AvaloniaFact]
	public void AReadDuringADrag_DoesNotMoveTheDraggedMarker()
	{
		_channel.Push(_etchStart);

		Markers.BeginMove(_etchStart);
		Markers.Move(_nine);
		_channel.Push(_etchStart, _etchEnd);

		Markers.ShownMarkers.Should().Equal(_etchStart with { TimeUtc = _nine }, _etchEnd);
	}

	[AvaloniaFact]
	public async Task Changed_FiresOncePerChange()
	{
		var changes = CountChanges();

		_channel.Push(_etchStart);
		changes().Should().Be(1);

		_channel.Push(_etchStart);
		changes().Should().Be(1, "an unchanged read changes nothing");

		_channel.Push(_etchStart with { TimeUtc = _nine });
		changes().Should().Be(2);

		await Markers.RenameAsync(Markers.ShownMarkers[0], "Etch end");
		changes().Should().Be(3, "the rename shows at once, and its success changes nothing more");

		Markers.BeginMove(Markers.ShownMarkers[0]);
		Markers.Move(_ten);
		Markers.Move(_ten);
		await Markers.EndMoveAsync();
		changes().Should().Be(4, "a pointer move to the same moment changes nothing");
	}

	[AvaloniaFact]
	public void HidingRecipeMarkers_LeavesOnlyUserMarkersShown_WithOneChange()
	{
		var recipeStart = new StoredMarker(3, _nine, "Etch.csv", MarkerKind.RecipeStart, IsLocked: true);
		_channel.Push(_etchStart, recipeStart, _etchEnd);
		var changes = CountChanges();

		Markers.ToggleRecipeMarkers();

		changes().Should().Be(1);
		Markers.ShowsRecipeMarkers.Should().BeFalse();
		Markers.ShownMarkers.Should().Equal(_etchStart, _etchEnd);
		Markers.NearestWithin(_nine, TimeSpan.FromMinutes(1)).Should().BeNull();

		Markers.ToggleRecipeMarkers();

		changes().Should().Be(2);
		Markers.ShownMarkers.Should().Equal(_etchStart, recipeStart, _etchEnd);
		Markers.NearestWithin(_nine, TimeSpan.FromMinutes(1)).Should().Be(recipeStart);
	}

	[AvaloniaFact]
	public async Task AFailedRenameOfARecipeMarkerHiddenMeanwhile_RestoresItsLabel()
	{
		var recipeStart = new StoredMarker(3, _nine, "Etch.csv", MarkerKind.RecipeStart, IsLocked: true);
		_channel.Push(_etchStart, recipeStart);
		_channel.WriteResult = Result.Fail(FakeDataProvider.UnreachableArchive());
		var heldRename = _channel.HoldNextWrite();

		var renaming = Markers.RenameAsync(recipeStart, "Purge.csv");
		Markers.ToggleRecipeMarkers();
		heldRename.SetResult();
		await renaming;
		Markers.ToggleRecipeMarkers();

		Markers.ShownMarkers.Should().Equal(_etchStart, recipeStart);
	}

	[AvaloniaFact]
	public void AFailedReadStream_ReachesThePanel()
	{
		_channel.Fail(new InvalidOperationException("marker reads ended"));

		_messagePanel.Entries.Should().ContainSingle();
	}

	private Func<int> CountChanges()
	{
		var changes = 0;
		Markers.Changed.Subscribe(_ => changes++);

		return () => changes;
	}
}
