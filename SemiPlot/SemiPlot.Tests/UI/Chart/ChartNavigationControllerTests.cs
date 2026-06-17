using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartNavigationControllerTests
{
	private static readonly DateTime _first = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _last = new(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void ZoomOut_WidensWindowAndRaisesWindowChange()
	{
		var controller = Loaded();
		NavigationWindow? raised = null;
		controller.WindowChanged += (_, window) => raised = window;
		var widthBefore = controller.To - controller.From;

		controller.ZoomAt(4.0, _last);

		raised.Should().NotBeNull();
		(controller.To - controller.From).Should().BeGreaterThan(widthBefore);
	}

	[Fact]
	public void PanBackward_ShiftsWindowIntoThePast()
	{
		var controller = Loaded();
		var fromBefore = controller.From;

		controller.PanBy(TimeSpan.FromMinutes(-5.0));

		controller.From.Should().BeBefore(fromBefore);
	}

	[Fact]
	public void PanForwardPastLiveEdge_DetachesSticky()
	{
		var controller = Loaded();
		controller.IsSticky.Should().BeTrue();

		controller.PanBy(TimeSpan.FromHours(5.0));

		controller.IsSticky.Should().BeFalse();
	}

	[Fact]
	public void SetSticky_TogglesStateAndReanchorsToLiveEdge()
	{
		var controller = Loaded();
		controller.SetSticky(false);
		controller.IsSticky.Should().BeFalse();

		controller.SetSticky(true);

		controller.IsSticky.Should().BeTrue();
		controller.To.Should().Be(_last);
	}

	[Fact]
	public void JumpToNow_ReattachesStickyWithNowAtRightEdge()
	{
		var controller = Loaded();
		controller.SetSticky(false);

		controller.JumpToNow();

		controller.IsSticky.Should().BeTrue();
		controller.To.Should().Be(_last);
	}

	[Fact]
	public void OnLiveEdge_WhenSticky_AdvancesWindow()
	{
		var controller = Loaded();
		var advanced = _last.AddMinutes(2.0);

		controller.OnLiveEdge(advanced);

		controller.To.Should().Be(advanced);
	}

	[Fact]
	public void OnLiveEdge_WhenNotSticky_LeavesWindowUnchanged()
	{
		var controller = Loaded();
		controller.SetSticky(false);
		var toBefore = controller.To;

		controller.OnLiveEdge(_last.AddMinutes(2.0));

		controller.To.Should().Be(toBefore);
	}

	[Theory]
	[InlineData(0.5, AggregationLayer.Raw)]
	[InlineData(24.0, AggregationLayer.Minute)]
	[InlineData(24.0 * 30.0, AggregationLayer.Hour)]
	[InlineData(24.0 * 200.0, AggregationLayer.Day)]
	public void LayerFollowsZoomWidth(double targetWidthHours, AggregationLayer expectedLayer)
	{
		var controller = Loaded();
		var currentWidth = controller.To - controller.From;
		var factor = TimeSpan.FromHours(targetWidthHours) / currentWidth;

		controller.ZoomAt(factor, controller.To);

		controller.ActiveLayer.Should().Be(expectedLayer);
	}

	[Theory]
	[InlineData(1.0, AggregationLayer.Raw)]
	[InlineData(48.0, AggregationLayer.Minute)]
	[InlineData(24.0 * 60.0, AggregationLayer.Hour)]
	public void LayerBoundaryCeilingsAreInclusive(double exactWidthHours, AggregationLayer expectedLayer)
	{
		var controller = Loaded();
		var currentWidth = controller.To - controller.From;
		var factor = TimeSpan.FromHours(exactWidthHours) / currentWidth;

		controller.ZoomAt(factor, controller.To);

		controller.ActiveLayer.Should().Be(expectedLayer);
	}

	[Fact]
	public void OnLiveEdge_WhenSticky_RaisesWindowChangeNotRequiringReQuery()
	{
		var controller = Loaded();
		NavigationWindow? raised = null;
		controller.WindowChanged += (_, window) => raised = window;

		controller.OnLiveEdge(_last.AddMinutes(2.0));

		raised.Should().NotBeNull();
		raised!.RequiresHistoryRequery.Should().BeFalse();
	}

	[Fact]
	public void ZoomOrPan_RaisesWindowChangeRequiringReQuery()
	{
		var controller = Loaded();
		NavigationWindow? raised = null;
		controller.WindowChanged += (_, window) => raised = window;

		controller.PanBy(TimeSpan.FromMinutes(-5.0));

		raised.Should().NotBeNull();
		raised!.RequiresHistoryRequery.Should().BeTrue();
	}

	[Fact]
	public void LayerForWidth_DoesNotFlipFlopAcrossOneHourBoundaryUnderHysteresis()
	{
		var controller = Loaded();

		// Cross just past the 1h Raw/Minute ceiling, then nudge back and forth inside the hysteresis band.
		controller.ZoomAt(1.05, controller.To);
		var layerAfterCrossing = controller.ActiveLayer;
		layerAfterCrossing.Should().Be(AggregationLayer.Raw);

		for (var notch = 0; notch < 6; notch++)
		{
			controller.ZoomAt(1.02, controller.To);
			controller.ZoomAt(0.98, controller.To);
			controller.ActiveLayer.Should().Be(AggregationLayer.Raw);
		}
	}

	private static ChartNavigationController Loaded()
	{
		var controller = new ChartNavigationController();
		controller.TrackDataExtents(_first, _last);
		return controller;
	}
}
