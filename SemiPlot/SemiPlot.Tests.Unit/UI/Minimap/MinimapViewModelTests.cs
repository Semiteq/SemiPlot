using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

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
		stand.ViewModel.IsArchiveEmpty.Should().BeTrue("the read answered with no rows");
	}

	[AvaloniaFact]
	public async Task AnUnreadOrFailedExtent_DeclaresNoEmptyArchive()
	{
		using var stand = new MinimapStand();
		stand.ViewModel.IsArchiveEmpty.Should().BeFalse("no read has answered yet");
		stand.Provider.FailExtent = true;

		await stand.LoadExtentAsync();

		stand.ViewModel.IsArchiveEmpty.Should().BeFalse("a failed read says nothing about the rows");
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
	public async Task ANewerSampleThanTheExtent_MovesTheRightBound(bool isSticky)
	{
		using var stand = new MinimapStand();
		var navigation = stand.Navigation;
		navigation.SeedFromArchiveExtent(new ArchiveExtent(_extentFirst, _extentLast));
		await stand.LoadExtentAsync();
		navigation.SetSticky(isSticky);
		var newest = _extentLast + TimeSpan.FromMinutes(5.0);

		navigation.OnLiveEdge(newest, newest);

		stand.ViewModel.ExtentLast.Should().Be(newest);
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
	}

	[AvaloniaFact]
	public async Task ASampleNewerThanTheExtentSeenBeforeItLands_IsTheRightBound()
	{
		using var stand = new MinimapStand();
		var newest = _extentLast + TimeSpan.FromMinutes(5.0);
		stand.Navigation.OnLiveEdge(newest, newest);

		await stand.LoadExtentAsync();

		stand.ViewModel.ExtentLast.Should().Be(newest);
	}

	[AvaloniaFact]
	public void ANewestSampleWithNoExtent_LeavesTheStripBlank()
	{
		using var stand = new MinimapStand();

		stand.Navigation.OnLiveEdge(_extentLast + TimeSpan.FromMinutes(5.0), _extentLast + TimeSpan.FromMinutes(5.0));

		stand.ViewModel.HasExtent.Should().BeFalse();
		stand.ViewModel.ExtentLast.Should().Be(default);
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
	public void ShadedRuns_PairTheRecipeMarkers_AndLeaveTheOperatorsOut()
	{
		using var stand = new MinimapStand();
		var start = _extentFirst.AddHours(8.0);

		stand.MarkerChannel.Push(
			new StoredMarker(1, start, "Etch.csv", MarkerKind.RecipeStart, IsLocked: true),
			new StoredMarker(2, start.AddMinutes(5.0), "#1"),
			new StoredMarker(3, start.AddMinutes(20.0), "Etch.csv", MarkerKind.RecipeInterrupted, IsLocked: true));

		stand.ViewModel.ShadedRuns.Should().Equal(new RecipeRun(start, start.AddMinutes(20.0), IsInterrupted: true, "Etch.csv"));
	}

	[AvaloniaFact]
	public void ShadedRuns_LeaveWithHiddenRecipeMarkers_AndComeBackWhenShown()
	{
		using var stand = new MinimapStand();
		var start = _extentFirst.AddHours(8.0);
		stand.MarkerChannel.Push(
			new StoredMarker(1, start, "Etch.csv", MarkerKind.RecipeStart, IsLocked: true),
			new StoredMarker(2, start.AddMinutes(20.0), "Etch.csv", MarkerKind.RecipeEnd, IsLocked: true));

		stand.Chart.Markers.ToggleRecipeMarkers();

		stand.ViewModel.ShadedRuns.Should().BeEmpty("hiding recipe markers hides the shading");

		stand.Chart.Markers.ToggleRecipeMarkers();

		stand.ViewModel.ShadedRuns.Should().ContainSingle();
	}

	[AvaloniaFact]
	public void AnOpenRun_FollowsTheNewestSample_AndAClosedOneKeepsItsList()
	{
		using var stand = new MinimapStand();
		var start = _extentLast.AddHours(-1.0);
		stand.MarkerChannel.Push(new StoredMarker(1, start, "Etch.csv", MarkerKind.RecipeStart, IsLocked: true));
		stand.Navigation.OnLiveEdge(_extentLast, _extentLast);
		stand.ViewModel.ShadedRuns.Should().Equal(new RecipeRun(start, _extentLast, IsInterrupted: false, "Etch.csv"));

		var newest = _extentLast.AddMinutes(5.0);
		stand.Navigation.OnLiveEdge(newest, newest);

		stand.ViewModel.ShadedRuns.Should().Equal(new RecipeRun(start, newest, IsInterrupted: false, "Etch.csv"));

		stand.MarkerChannel.Push(
			new StoredMarker(1, start, "Etch.csv", MarkerKind.RecipeStart, IsLocked: true),
			new StoredMarker(2, newest, "Etch.csv", MarkerKind.RecipeEnd, IsLocked: true));
		var closed = stand.ViewModel.ShadedRuns;
		var later = newest.AddMinutes(5.0);
		stand.Navigation.OnLiveEdge(later, later);

		stand.ViewModel.ShadedRuns.Should().BeSameAs(closed, "a live sample past a closed run changes no run");
	}
}
