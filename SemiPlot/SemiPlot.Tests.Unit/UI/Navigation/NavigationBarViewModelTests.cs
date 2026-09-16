using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Navigation;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class NavigationBarViewModelTests
{
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
		var liveEdge = DateTime.UtcNow;
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
		var liveEdge = DateTime.UtcNow;
		chart.Navigation.TrackDataExtents(liveEdge - TimeSpan.FromDays(7.0), liveEdge);

		bar.Dispose();
		chart.Navigation.PanBy(TimeSpan.FromHours(-2.0));

		chart.Navigation.IsSticky.Should().BeFalse();
		bar.IsSticky.Should().BeTrue();
	}
}
