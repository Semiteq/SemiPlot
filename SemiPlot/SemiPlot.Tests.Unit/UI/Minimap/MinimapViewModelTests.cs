using System.Globalization;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Messages;
using SemiPlot.Tests.Unit.UI.PenEditor;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Minimap;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Minimap;

[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class MinimapViewModelTests
{
	private static readonly DateTime _extentFirst = MinimapStand.ExtentFirst;
	private static readonly DateTime _extentLast = MinimapStand.ExtentLast;

	[AvaloniaFact]
	public async Task LoadExtentAsync_ExposesProviderFirstAndLast()
	{
		using var stand = new MinimapStand();

		await stand.LoadExtentAsync();

		stand.ViewModel.HasExtent.Should().BeTrue();
		stand.ViewModel.ExtentFirst.Should().Be(_extentFirst);
		stand.ViewModel.ExtentLast.Should().Be(_extentLast);
	}

	[AvaloniaFact]
	public async Task LoadExtentAsync_WithAnEmptyExtent_LeavesHasExtentFalse()
	{
		using var stand = new MinimapStand();
		stand.Provider.Pens = [];

		await stand.LoadExtentAsync();

		stand.ViewModel.HasExtent.Should().BeFalse();
		stand.ViewModel.ExtentFirstLabel.Should().BeEmpty();
		stand.ViewModel.ExtentLastLabel.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void ExtentLabels_BeforeExtentLoaded_AreEmpty()
	{
		using var stand = new MinimapStand();

		stand.ViewModel.HasExtent.Should().BeFalse();
		stand.ViewModel.ExtentFirstLabel.Should().BeEmpty();
		stand.ViewModel.ExtentLastLabel.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task ExtentLabels_AfterExtentLoaded_RenderLocalEndpoints()
	{
		using var stand = new MinimapStand();

		await stand.LoadExtentAsync();

		stand.ViewModel.ExtentFirstLabel.Should().Be(Label(_extentFirst));
		stand.ViewModel.ExtentLastLabel.Should().Be(Label(_extentLast));
	}

	[AvaloniaFact]
	public async Task WindowFraction_MapsTheNavigationWindowOverTheExtent()
	{
		using var stand = new MinimapStand();
		var navigation = stand.Navigation;
		await stand.LoadExtentAsync();

		// Seeds the window to [last - width, last], a known sub-span of the extent.
		navigation.TrackDataExtents(_extentFirst, _extentLast);

		var (start, width) = MinimapGeometry.WindowFraction(
			_extentFirst, _extentLast, navigation.From, navigation.To);

		stand.ViewModel.WindowStartFraction.Should().BeApproximately(start, 1e-9);
		stand.ViewModel.WindowWidthFraction.Should().BeApproximately(width, 1e-9);
		stand.ViewModel.WindowWidthFraction.Should().BeGreaterThan(0.0);
	}

	[AvaloniaFact]
	public async Task NavigateToFraction_RecentersTheNavigationWindowAtTheMappedTime()
	{
		using var stand = new MinimapStand();
		var navigation = stand.Navigation;
		await stand.LoadExtentAsync();
		navigation.TrackDataExtents(_extentFirst, _extentLast);

		stand.ViewModel.NavigateToFraction(0.5);

		var center = stand.WindowCenter;
		var expectedMidpoint = MinimapGeometry.TimeAtFraction(_extentFirst, _extentLast, 0.5);
		(center - expectedMidpoint).Duration().Should().BeLessThan(TimeSpan.FromSeconds(1.0));
	}

	[AvaloniaFact]
	public void NavigateToFraction_BeforeExtentLoaded_DoesNotMoveTheWindow()
	{
		using var stand = new MinimapStand();
		var navigation = stand.Navigation;
		var fromBefore = navigation.From;
		var toBefore = navigation.To;

		stand.ViewModel.NavigateToFraction(0.5);

		navigation.From.Should().Be(fromBefore);
		navigation.To.Should().Be(toBefore);
	}

	[AvaloniaFact]
	public async Task AFailedExtentQueryReachesTheMessagePanel()
	{
		using var stand = new MinimapStand();
		stand.Provider.FailExtent = true;

		await stand.LoadExtentAsync();

		stand.ViewModel.HasExtent.Should().BeFalse();
		stand.Panel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(
				FakePenCatalogueEditor.Refusal(ArchiveFault.ReadFailed, "42601")));
	}

	[AvaloniaTheory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ANewerSampleThanTheExtent_MovesTheRightBoundAndItsLabel(bool isSticky)
	{
		using var stand = new MinimapStand();
		var navigation = stand.Navigation;
		navigation.SeedFromArchiveExtent(new ArchiveExtent(_extentFirst, _extentLast));
		await stand.LoadExtentAsync();
		navigation.SetSticky(isSticky);
		var newest = _extentLast + TimeSpan.FromMinutes(5.0);

		navigation.OnLiveEdge(newest, newest);

		stand.ViewModel.ExtentLast.Should().Be(newest);
		stand.ViewModel.ExtentLastLabel.Should().Be(Label(newest));
		var (start, width) = MinimapGeometry.WindowFraction(_extentFirst, newest, navigation.From, navigation.To);
		stand.ViewModel.WindowStartFraction.Should().BeApproximately(start, 1e-9);
		stand.ViewModel.WindowWidthFraction.Should().BeApproximately(width, 1e-9);
	}

	[AvaloniaFact]
	public async Task AnExtentEndingLaterThanTheNewestSample_KeepsItsOwnLastSample()
	{
		using var stand = new MinimapStand();
		var navigation = stand.Navigation;
		navigation.OnLiveEdge(_extentLast - TimeSpan.FromDays(1.0), _extentLast - TimeSpan.FromDays(1.0));

		await stand.LoadExtentAsync();
		navigation.OnLiveEdge(_extentLast - TimeSpan.FromHours(1.0), _extentLast - TimeSpan.FromHours(1.0));

		stand.ViewModel.ExtentLast.Should().Be(_extentLast);
		stand.ViewModel.ExtentLastLabel.Should().Be(Label(_extentLast));
	}

	[AvaloniaFact]
	public async Task ASampleNewerThanTheExtentSeenBeforeItLands_IsTheRightBound()
	{
		using var stand = new MinimapStand();
		var newest = _extentLast + TimeSpan.FromMinutes(5.0);
		stand.Navigation.OnLiveEdge(newest, newest);

		await stand.LoadExtentAsync();

		stand.ViewModel.ExtentLast.Should().Be(newest);
		stand.ViewModel.ExtentLastLabel.Should().Be(Label(newest));
	}

	[AvaloniaFact]
	public void ANewestSampleWithNoExtent_LeavesTheStripBlank()
	{
		using var stand = new MinimapStand();

		stand.Navigation.OnLiveEdge(_extentLast + TimeSpan.FromMinutes(5.0), _extentLast + TimeSpan.FromMinutes(5.0));

		stand.ViewModel.HasExtent.Should().BeFalse();
		stand.ViewModel.ExtentLast.Should().Be(default);
		stand.ViewModel.ExtentFirstLabel.Should().BeEmpty();
		stand.ViewModel.ExtentLastLabel.Should().BeEmpty();
	}

	[AvaloniaTheory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task AMissingExtentAtStart_IsReadOnceMoreOnTheNextNewSample(bool readFails)
	{
		using var stand = new MinimapStand();
		var provider = stand.Provider;
		var pens = provider.Pens;
		provider.FailExtent = readFails;
		provider.Pens = readFails ? pens : [];
		await stand.LoadExtentAsync();
		stand.ViewModel.HasExtent.Should().BeFalse();
		provider.FailExtent = false;
		provider.Pens = pens;
		provider.GateExtent = true;
		var newest = _extentLast + TimeSpan.FromMinutes(5.0);

		stand.Navigation.OnLiveEdge(newest - TimeSpan.FromMinutes(1.0), newest - TimeSpan.FromMinutes(1.0));
		stand.Navigation.OnLiveEdge(newest, newest);

		provider.ExtentQueryCount.Should().Be(2, "one read at start and one more, not one per sample");
		provider.ExtentGate.SetResult(Result.Ok(new ArchiveExtent(_extentFirst, _extentLast)));
		Dispatcher.UIThread.RunJobs();
		stand.Scheduler.AdvanceBy(1);

		stand.ViewModel.HasExtent.Should().BeTrue();
		stand.ViewModel.ExtentLast.Should().Be(newest);
	}

	[AvaloniaFact]
	public async Task ASteadyExtentFailure_RetriesOnceAMinuteAndReportsOncePerOutage()
	{
		var logger = new FakeLogger<MinimapViewModel>();
		using var stand = new MinimapStand(logger);
		var provider = stand.Provider;
		provider.FailExtent = true;
		await stand.LoadExtentAsync();
		stand.Panel.Entries.Should().ContainSingle();
		var warningsAtStart = logger.Levels().Count(level => level == LogLevel.Warning);

		// The catalogue sync's own entry tops the panel, as it does during an outage, so a repeat of the
		// extent's entry would not coalesce with it.
		stand.Panel.Report(ArchiveFailureMapper.Map(new Error("The pen catalogue read failed.")));
		for (var second = 1; second <= 170; second++)
		{
			stand.Scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);
			var advanced = _extentLast + TimeSpan.FromSeconds(second);
			stand.Navigation.OnLiveEdge(advanced, advanced);
		}

		stand.Scheduler.AdvanceBy(1);

		provider.ExtentQueryCount.Should().Be(4, "the read at start, one on the first sample and one a minute after each");
		stand.Panel.Entries.Should().HaveCount(2, "the retries add no entry");
		logger.Levels().Count(level => level == LogLevel.Warning).Should().Be(warningsAtStart + 3);

		provider.FailExtent = false;
		stand.Scheduler.AdvanceBy(TimeSpan.FromMinutes(1.0).Ticks);
		stand.Navigation.OnLiveEdge(_extentLast + TimeSpan.FromMinutes(5.0), _extentLast + TimeSpan.FromMinutes(5.0));
		stand.Scheduler.AdvanceBy(1);
		stand.ViewModel.HasExtent.Should().BeTrue();
		provider.FailExtent = true;
		await stand.LoadExtentAsync();

		stand.Panel.Entries.Should().HaveCount(3, "a success arms the report again");
	}

	[AvaloniaFact]
	public async Task HoverAt_LabelsThePointsTimeAndClearHoverTakesItAway()
	{
		using var stand = new MinimapStand();
		await stand.LoadExtentAsync();

		stand.ViewModel.HoverAt(0.25);

		stand.ViewModel.HoverFraction.Should().Be(0.25);
		stand.ViewModel.HoverLabel.Should().Be(Label(MinimapGeometry.TimeAtFraction(_extentFirst, _extentLast, 0.25)));

		stand.ViewModel.ClearHover();

		stand.ViewModel.HoverFraction.Should().BeNull();
		stand.ViewModel.HoverLabel.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void HoverAt_WithNoExtent_ShowsNothing()
	{
		using var stand = new MinimapStand();

		stand.ViewModel.HoverAt(0.5);

		stand.ViewModel.HoverFraction.Should().BeNull();
		stand.ViewModel.HoverLabel.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task ANewerSample_RelabelsTheHoveredRightEnd()
	{
		using var stand = new MinimapStand();
		await stand.LoadExtentAsync();
		stand.ViewModel.HoverAt(1.0);
		var labels = new List<string>();
		using var subscription = stand.ViewModel
			.WhenAnyValue(model => model.HoverLabel)
			.Subscribe(labels.Add);
		var newest = _extentLast + TimeSpan.FromMinutes(5.0);

		stand.Navigation.OnLiveEdge(newest, newest);

		labels.Should().Equal(Label(_extentLast), Label(newest));
	}

	[AvaloniaFact]
	public async Task ANewFirstSample_RelabelsTheHoveredLeftEnd()
	{
		using var stand = new MinimapStand();
		await stand.LoadExtentAsync();
		stand.ViewModel.HoverAt(0.0);
		var labels = new List<string>();
		using var subscription = stand.ViewModel
			.WhenAnyValue(model => model.HoverLabel)
			.Subscribe(labels.Add);
		var earlier = _extentFirst - TimeSpan.FromDays(1.0);
		stand.Provider.ArchiveFirstUtc = earlier;

		await stand.LoadExtentAsync();

		labels.Should().Equal(Label(_extentFirst), Label(earlier));
	}

	private static string Label(DateTime utc)
	{
		return utc.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.CurrentCulture);
	}

	[AvaloniaFact]
	public async Task TimeMarkerFractions_PlaceEveryMarkerInsideTheExtentAlongTheStrip()
	{
		using var stand = new MinimapStand();
		var span = _extentLast - _extentFirst;
		stand.MarkerChannel.Push(
			new StoredMarker(1, _extentFirst + (span / 4.0), "1"),
			new StoredMarker(2, _extentFirst + (span / 2.0), "2"),
			new StoredMarker(3, _extentFirst.AddDays(-1.0), "3"));
		stand.ViewModel.TimeMarkerFractions.Should().BeEmpty("without an extent the strip has no scale");

		await stand.LoadExtentAsync();

		stand.ViewModel.TimeMarkerFractions.Should().Equal(0.25, 0.5);

		stand.MarkerChannel.Push(new StoredMarker(2, _extentFirst + (span / 2.0), "2"));

		stand.ViewModel.TimeMarkerFractions.Should().Equal([0.5], "a marker read gone leaves the strip");
	}

	[AvaloniaFact]
	public async Task TimeMarkerFractions_LeaveOutHiddenRecipeMarkers_AndTakeThemBackWhenShown()
	{
		using var stand = new MinimapStand();
		var span = _extentLast - _extentFirst;
		stand.MarkerChannel.Push(
			new StoredMarker(1, _extentFirst + (span / 4.0), "Etch.csv", MarkerKind.RecipeStart, IsLocked: true),
			new StoredMarker(2, _extentFirst + (span / 2.0), "#1"));
		await stand.LoadExtentAsync();

		stand.Chart.Markers.ToggleRecipeMarkers();

		stand.ViewModel.TimeMarkerFractions.Should().Equal(0.5);

		stand.Chart.Markers.ToggleRecipeMarkers();

		stand.ViewModel.TimeMarkerFractions.Should().Equal(0.25, 0.5);
	}
}
