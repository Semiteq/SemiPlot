using System.Windows.Input;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Tests.Unit.UI.Chart;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Navigation;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Navigation;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class NavigationBarViewModelTests
{
	private static readonly DateTime _day = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

	[AvaloniaFact]
	public void ToggleStickyCommand_FlipsStickyOnNavigation()
	{
		var (chart, bar) = NavigationBarTestBuilder.CreateBar();
		bar.IsSticky.Should().BeTrue();

		bar.ToggleStickyCommand.Execute().Subscribe();

		bar.IsSticky.Should().BeFalse();
		chart.Navigation.IsSticky.Should().BeFalse();
	}

	[AvaloniaFact]
	public void JumpToNowCommand_ReattachesStickyOnNavigation()
	{
		var (chart, bar) = NavigationBarTestBuilder.CreateBar();
		bar.ToggleStickyCommand.Execute().Subscribe();
		bar.IsSticky.Should().BeFalse();

		bar.JumpToNowCommand.Execute().Subscribe();

		bar.IsSticky.Should().BeTrue();
		chart.Navigation.IsSticky.Should().BeTrue();
	}

	[AvaloniaFact]
	public void PanPastLiveEdge_AutoDetachesStickyOnTheBar_JumpToNowReattaches()
	{
		var (chart, bar) = NavigationBarTestBuilder.CreateBar();
		var liveEdge = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
		chart.Navigation.TrackDataExtents(liveEdge - TimeSpan.FromDays(7.0), liveEdge);
		bar.IsSticky.Should().BeTrue();

		chart.Navigation.PanBy(TimeSpan.FromHours(-2.0));

		bar.IsSticky.Should().BeFalse();
		chart.Navigation.IsSticky.Should().BeFalse();

		bar.JumpToNowCommand.Execute().Subscribe();

		bar.IsSticky.Should().BeTrue();
		chart.Navigation.IsSticky.Should().BeTrue();
	}

	[AvaloniaFact]
	public void ToggleDeltaModeCommand_EntersAndExitsDeltaModeOnChart()
	{
		var (chart, bar) = NavigationBarTestBuilder.CreateBar();
		bar.IsDeltaModeEnabled.Should().BeFalse();

		bar.ToggleDeltaModeCommand.Execute().Subscribe();

		bar.IsDeltaModeEnabled.Should().BeTrue();
		chart.IsDeltaModeEnabled.Should().BeTrue();
		chart.ActiveLeftButtonTool.Should().Be(LeftButtonTool.DeltaPlacement);

		bar.ToggleDeltaModeCommand.Execute().Subscribe();

		bar.IsDeltaModeEnabled.Should().BeFalse();
		chart.IsDeltaModeEnabled.Should().BeFalse();
		chart.ActiveLeftButtonTool.Should().Be(LeftButtonTool.Pan);
	}

	[AvaloniaFact]
	public void Dispose_UnsubscribesFromNavigation()
	{
		var (chart, bar) = NavigationBarTestBuilder.CreateBar();
		var liveEdge = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
		chart.Navigation.TrackDataExtents(liveEdge - TimeSpan.FromDays(7.0), liveEdge);

		bar.Dispose();
		chart.Navigation.PanBy(TimeSpan.FromHours(-2.0));

		chart.Navigation.IsSticky.Should().BeFalse();
		bar.IsSticky.Should().BeTrue();
	}

	[AvaloniaFact]
	public void RunSteps_FrameTheRunBeforeAndAfterTheWindowCentre_WithATenthOfItsLengthOnEachSide()
	{
		var (chart, bar, markers) = CreateStand(At(6, 0), At(12, 0));
		markers.Push(ThreeRuns());

		bar.PreviousRunCommand.Execute().Subscribe();

		WindowOf(chart).Should().Be((At(9, 58), At(10, 22)));
		bar.IsSticky.Should().BeFalse();

		bar.PreviousRunCommand.Execute().Subscribe();

		WindowOf(chart).Should().Be((At(8, 57), At(9, 33)));

		bar.NextRunCommand.Execute().Subscribe();

		WindowOf(chart).Should().Be((At(9, 58), At(10, 22)));
	}

	[AvaloniaFact]
	public void RunSteps_AreDisabled_PastTheOuterRuns()
	{
		var (_, bar, markers) = CreateStand(At(6, 0), At(12, 0));
		markers.Push(ThreeRuns());
		CanExecute(bar.NextRunCommand).Should().BeFalse();
		CanExecute(bar.PreviousRunCommand).Should().BeTrue();

		bar.PreviousRunCommand.Execute().Subscribe();
		bar.PreviousRunCommand.Execute().Subscribe();
		bar.PreviousRunCommand.Execute().Subscribe();

		CanExecute(bar.PreviousRunCommand).Should().BeFalse();
		CanExecute(bar.NextRunCommand).Should().BeTrue();
	}

	[AvaloniaFact]
	public void LatestRun_FramesTheNewestRun_FromAnyWindow()
	{
		var (chart, bar, markers) = CreateStand(At(6, 0), At(12, 0));
		markers.Push(ThreeRuns());
		chart.Navigation.SetWindow(At(6, 0), At(7, 0));

		bar.LatestRunCommand.Execute().Subscribe();

		WindowOf(chart).Should().Be((At(9, 58), At(10, 22)));
		CanExecute(bar.NextRunCommand).Should().BeFalse();
	}

	[AvaloniaFact]
	public void LatestRun_FramesAnOpenRunUpToTheNewestSample_AndFollowsTheSample()
	{
		var (chart, bar, markers) = CreateStand(At(6, 0), At(12, 0));
		markers.Push(Marker(1, At(11, 30), MarkerKind.RecipeStart));

		bar.LatestRunCommand.Execute().Subscribe();

		WindowOf(chart).Should().Be((At(11, 27), At(12, 3)));

		chart.Navigation.OnLiveEdge(At(12, 30), At(12, 30));
		bar.LatestRunCommand.Execute().Subscribe();

		WindowOf(chart).Should().Be((At(11, 24), At(12, 36)));
	}

	[AvaloniaFact]
	public void EveryStep_IsDisabled_WithNoMarkers()
	{
		var (chart, bar, _) = CreateStand(At(6, 0), At(12, 0));
		var windowBefore = WindowOf(chart);

		CanExecute(bar.PreviousRunCommand).Should().BeFalse();
		CanExecute(bar.NextRunCommand).Should().BeFalse();
		CanExecute(bar.LatestRunCommand).Should().BeFalse();
		CanExecute(bar.PreviousMarkerCommand).Should().BeFalse();
		CanExecute(bar.NextMarkerCommand).Should().BeFalse();
		WindowOf(chart).Should().Be(windowBefore);
	}

	[AvaloniaFact]
	public void RunSteps_PairHiddenRecipeMarkers()
	{
		var (chart, bar, markers) = CreateStand(At(6, 0), At(12, 0));
		markers.Push(ThreeRuns());
		chart.Markers.ToggleRecipeMarkers();
		chart.Markers.ShowsRecipeMarkers.Should().BeFalse();

		bar.PreviousRunCommand.Execute().Subscribe();

		WindowOf(chart).Should().Be((At(9, 58), At(10, 22)));
		CanExecute(bar.LatestRunCommand).Should().BeTrue();
	}

	[AvaloniaFact]
	public void ARunStartingBeforeTheFirstSample_IsFramedFromIt_AndNothingEarlierSteps()
	{
		var (chart, bar, markers) = CreateStand(At(8, 0), At(12, 0));
		markers.Push(
			Marker(1, At(7, 0), MarkerKind.RecipeStart),
			Marker(2, At(7, 20), MarkerKind.RecipeEnd),
			Marker(3, At(7, 50), MarkerKind.RecipeStart),
			Marker(4, At(8, 10), MarkerKind.RecipeEnd),
			Marker(5, At(9, 0), MarkerKind.RecipeStart),
			Marker(6, At(9, 20), MarkerKind.RecipeEnd));
		bar.PreviousRunCommand.Execute().Subscribe();

		bar.PreviousRunCommand.Execute().Subscribe();

		WindowOf(chart).Should().Be((chart.Navigation.FirstSample, At(8, 12)));
		CanExecute(bar.PreviousRunCommand).Should().BeFalse();

		bar.NextRunCommand.Execute().Subscribe();

		WindowOf(chart).Should().Be((At(8, 58), At(9, 22)));
	}

	[AvaloniaFact]
	public void MarkerSteps_CentreTheOperatorMarkers_KeepTheWidth_AndPassOverRecipeMarkers()
	{
		var (chart, bar, markers) = CreateStand(At(6, 0), At(12, 0));
		markers.Push(
			Marker(1, At(9, 0), MarkerKind.User),
			Marker(2, At(10, 0), MarkerKind.User),
			Marker(3, At(10, 30), MarkerKind.RecipeStart),
			Marker(4, At(10, 45), MarkerKind.RecipeEnd));

		bar.PreviousMarkerCommand.Execute().Subscribe();

		WindowOf(chart).Should().Be((At(9, 30), At(10, 30)));

		bar.PreviousMarkerCommand.Execute().Subscribe();

		WindowOf(chart).Should().Be((At(8, 30), At(9, 30)));
		CanExecute(bar.PreviousMarkerCommand).Should().BeFalse();

		bar.NextMarkerCommand.Execute().Subscribe();

		WindowOf(chart).Should().Be((At(9, 30), At(10, 30)));
		CanExecute(bar.NextMarkerCommand).Should().BeFalse();
	}

	[AvaloniaFact]
	public void AMarkerTheFirstSampleKeepsOffCentre_IsNoPreviousStep()
	{
		var (chart, bar, markers) = CreateStand(At(8, 0), At(12, 0));
		var later = Marker(2, At(8, 20), MarkerKind.User);
		markers.Push(Marker(1, At(8, 10), MarkerKind.User), later);

		bar.MarkerList.GoTo(RowOf(bar, later));

		WindowOf(chart).Should().Be((At(8, 0), At(9, 0)));
		CanExecute(bar.PreviousMarkerCommand).Should().BeFalse();
	}

	[AvaloniaFact]
	public void AMarkerBeforeTheFirstSample_IsNoStep()
	{
		var (chart, bar, markers) = CreateStand(At(8, 0), At(12, 0));
		markers.Push(Marker(1, At(7, 30), MarkerKind.User), Marker(2, At(9, 0), MarkerKind.User));

		bar.PreviousMarkerCommand.Execute().Subscribe();

		WindowOf(chart).Should().Be((At(8, 30), At(9, 30)));
		CanExecute(bar.PreviousMarkerCommand).Should().BeFalse();
	}

	[AvaloniaFact]
	public void GoingToAListedMarker_CentresIt_KeepingTheWidth()
	{
		var (chart, bar, markers) = CreateStand(At(6, 0), At(12, 0));
		var marker = Marker(1, At(9, 0), MarkerKind.User);
		markers.Push(marker);

		bar.MarkerList.GoTo(RowOf(bar, marker));

		WindowOf(chart).Should().Be((At(8, 30), At(9, 30)));
		bar.IsSticky.Should().BeFalse();
	}

	[AvaloniaFact]
	public void OperatorMarkers_ListsTheOperatorMarkersNewestFirst_AndFollowsTheReads()
	{
		var (_, bar, markers) = CreateStand(At(6, 0), At(12, 0));
		var first = Marker(1, At(9, 0), MarkerKind.User);
		var second = Marker(3, At(10, 0), MarkerKind.User);
		markers.Push(first, Marker(2, At(9, 30), MarkerKind.RecipeStart), second);

		bar.OperatorMarkers.Should().Equal(second, first);
		bar.HasOperatorMarkers.Should().BeTrue();

		var third = Marker(4, At(11, 0), MarkerKind.User);
		markers.Push(first, second, third);

		bar.OperatorMarkers.Should().Equal(third, second, first);
	}

	[AvaloniaFact]
	public void Dispose_StopsFollowingTheMarkers()
	{
		var (_, bar, markers) = CreateStand(At(6, 0), At(12, 0));

		bar.Dispose();
		markers.Push(Marker(1, At(9, 0), MarkerKind.User));

		bar.OperatorMarkers.Should().BeEmpty();
		bar.HasOperatorMarkers.Should().BeFalse();
	}

	/// <summary>A bar over a chart whose window is the hour up to the newest sample.</summary>
	private static (TrendChartViewModel Chart, NavigationBarViewModel Bar, FakeMarkerChannel Markers) CreateStand(
		DateTime firstSample,
		DateTime newestSample)
	{
		var markers = new FakeMarkerChannel();
		var (chart, bar) = NavigationBarTestBuilder.CreateBar(markers);
		chart.Navigation.TrackDataExtents(firstSample, newestSample);

		return (chart, bar, markers);
	}

	/// <summary>08:00-08:20 ended, 09:00-09:30 interrupted, 10:00-10:20 ended.</summary>
	private static StoredMarker[] ThreeRuns()
	{
		return
		[
			Marker(1, At(8, 0), MarkerKind.RecipeStart),
			Marker(2, At(8, 20), MarkerKind.RecipeEnd),
			Marker(3, At(9, 0), MarkerKind.RecipeStart),
			Marker(4, At(9, 30), MarkerKind.RecipeInterrupted),
			Marker(5, At(10, 0), MarkerKind.RecipeStart),
			Marker(6, At(10, 20), MarkerKind.RecipeEnd)
		];
	}

	private static NavigationListItem RowOf(NavigationBarViewModel bar, StoredMarker marker)
	{
		return bar.MarkerList.Rows.OfType<NavigationListItem>().Single(row => row.Entry.Key == marker.Id);
	}

	private static StoredMarker Marker(long id, DateTime timeUtc, MarkerKind kind)
	{
		return new StoredMarker(id, timeUtc, "#" + id, kind, IsLocked: kind != MarkerKind.User);
	}

	private static DateTime At(int hour, int minute)
	{
		return _day.AddHours(hour).AddMinutes(minute);
	}

	private static (DateTime From, DateTime To) WindowOf(TrendChartViewModel chart)
	{
		return (chart.Navigation.From, chart.Navigation.To);
	}

	private static bool CanExecute(ICommand command)
	{
		return command.CanExecute(null);
	}
}
