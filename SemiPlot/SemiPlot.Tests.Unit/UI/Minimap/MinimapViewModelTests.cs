using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Minimap;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Minimap;

[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class MinimapViewModelTests
{
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33);
	private static readonly DateTime _extentFirst = new(2025, 12, 25, 0, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _extentLast = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	[AvaloniaFact]
	public async Task LoadExtentAsync_ExposesProviderFirstAndLast()
	{
		var (viewModel, _, _) = CreateViewModel();

		await viewModel.LoadExtentAsync();

		viewModel.HasExtent.Should().BeTrue();
		viewModel.ExtentFirst.Should().Be(_extentFirst);
		viewModel.ExtentLast.Should().Be(_extentLast);
	}

	[AvaloniaFact]
	public async Task LoadExtentAsync_WithAnEmptyExtent_LeavesHasExtentFalse()
	{
		var (viewModel, _, provider) = CreateViewModel();
		provider.ArchiveExtentOverride = ArchiveExtent.Empty;

		await viewModel.LoadExtentAsync();

		viewModel.HasExtent.Should().BeFalse();
		viewModel.ExtentFirstLabel.Should().BeEmpty();
		viewModel.ExtentLastLabel.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void ExtentLabels_BeforeExtentLoaded_AreEmpty()
	{
		var (viewModel, _, _) = CreateViewModel();

		viewModel.HasExtent.Should().BeFalse();
		viewModel.ExtentFirstLabel.Should().BeEmpty();
		viewModel.ExtentLastLabel.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task ExtentLabels_AfterExtentLoaded_RenderLocalEndpoints()
	{
		var (viewModel, _, _) = CreateViewModel();

		await viewModel.LoadExtentAsync();

		viewModel.ExtentFirstLabel.Should().Be(_extentFirst.ToLocalTime().ToString("MMM d HH:mm"));
		viewModel.ExtentLastLabel.Should().Be(_extentLast.ToLocalTime().ToString("MMM d HH:mm"));
	}

	[AvaloniaFact]
	public async Task WindowFraction_MapsTheNavigationWindowOverTheExtent()
	{
		var (viewModel, navigation, _) = CreateViewModel();
		await viewModel.LoadExtentAsync();

		// Seeds the window to [last - width, last], a known sub-span of the extent.
		navigation.TrackDataExtents(_extentFirst, _extentLast);

		var (start, width) = MinimapGeometry.WindowFraction(
			_extentFirst, _extentLast, navigation.From, navigation.To);

		viewModel.WindowStartFraction.Should().BeApproximately(start, 1e-9);
		viewModel.WindowWidthFraction.Should().BeApproximately(width, 1e-9);
		viewModel.WindowWidthFraction.Should().BeGreaterThan(0.0);
	}

	[AvaloniaFact]
	public async Task NavigateToFraction_RecentersTheNavigationWindowAtTheMappedTime()
	{
		var (viewModel, navigation, _) = CreateViewModel();
		await viewModel.LoadExtentAsync();
		navigation.TrackDataExtents(_extentFirst, _extentLast);

		viewModel.NavigateToFraction(0.5);

		var center = navigation.From + ((navigation.To - navigation.From) / 2.0);
		var expectedMidpoint = MinimapGeometry.TimeAtFraction(_extentFirst, _extentLast, 0.5);
		(center - expectedMidpoint).Duration().Should().BeLessThan(TimeSpan.FromSeconds(1.0));
	}

	[AvaloniaFact]
	public void NavigateToFraction_BeforeExtentLoaded_DoesNotMoveTheWindow()
	{
		var (viewModel, navigation, _) = CreateViewModel();
		var fromBefore = navigation.From;
		var toBefore = navigation.To;

		viewModel.NavigateToFraction(0.5);

		navigation.From.Should().Be(fromBefore);
		navigation.To.Should().Be(toBefore);
	}

	private static (MinimapViewModel ViewModel, ChartNavigationController Navigation, FakeDataProvider Provider)
		CreateViewModel()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10))
		{
			ArchiveFirstUtc = _extentFirst,
			ArchiveLastUtc = _extentLast
		};
		var coordinator = new TrendCoordinator(
			provider,
			provider.Pens,
			scheduler,
			ImmediateScheduler.Instance,
			_batchWindow);
		var navigation = new ChartNavigationController();
		var viewModel = new MinimapViewModel(
			coordinator,
			navigation,
			ImmediateScheduler.Instance,
			NullLogger<MinimapViewModel>.Instance);

		return (viewModel, navigation, provider);
	}
}
