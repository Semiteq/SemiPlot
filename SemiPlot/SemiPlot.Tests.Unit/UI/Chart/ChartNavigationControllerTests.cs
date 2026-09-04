using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartNavigationControllerTests
{
	private const int TestColumnCount = 1024;
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

	// Half a layer's ceiling sits well inside it, clear of the 25% zoom quantisation step and the 10%
	// hysteresis band. The Day row's 1.2x hour ceiling stays inside the 365-day maximum window at 1024
	// columns, so the answer comes from the ladder, not from TrendNavigationModel truncating the width.
	[Theory]
	[InlineData(AggregationLayer.Raw, 0.5, AggregationLayer.Raw)]
	[InlineData(AggregationLayer.Minute, 0.5, AggregationLayer.Minute)]
	[InlineData(AggregationLayer.Hour, 0.5, AggregationLayer.Hour)]
	[InlineData(AggregationLayer.Hour, 1.2, AggregationLayer.Day)]
	public void LayerFollowsZoomWidth(
		AggregationLayer ceilingOwner,
		double ceilingFraction,
		AggregationLayer expectedLayer)
	{
		var controller = Loaded();
		controller.SetTargetColumnCount(TestColumnCount);
		var targetWidth = Ceiling(ceilingOwner, TestColumnCount) * ceilingFraction;
		var factor = targetWidth / (controller.To - controller.From);

		controller.ZoomAt(factor, controller.To);

		controller.ActiveLayer.Should().Be(expectedLayer);
	}

	// The hysteresis band widens the ceiling of whichever layer is in force, so each ceiling is probed from a
	// different layer: probing a layer's own ceiling while it is in force would pass under either `<=` or
	// `<`.
	[Theory]
	[InlineData(256)]
	[InlineData(1024)]
	[InlineData(2048)]
	public void LayerBoundaryCeilingsAreInclusive(int columnCount)
	{
		ChartNavigationController.LayerForWidth(
				Ceiling(AggregationLayer.Minute, columnCount), AggregationLayer.Raw, columnCount)
			.Should().Be(AggregationLayer.Minute);

		ChartNavigationController.LayerForWidth(
				Ceiling(AggregationLayer.Hour, columnCount), AggregationLayer.Raw, columnCount)
			.Should().Be(AggregationLayer.Hour);

		ChartNavigationController.LayerForWidth(
				Ceiling(AggregationLayer.Raw, columnCount), AggregationLayer.Minute, columnCount)
			.Should().Be(AggregationLayer.Raw);
	}

	[Fact]
	public void SameWindowWidth_SelectsDifferentLayersAtDifferentColumnCounts()
	{
		var controller = Loaded();
		var currentWidth = controller.To - controller.From;

		controller.ZoomAt(TimeSpan.FromHours(4.0) / currentWidth, controller.To);

		// Four hours is 56 s per column at 256 columns, where the minute layer's 15 s spacing fills every
		// column, but 7 s per column at 2048 — finer than that spacing. Both calls are transitions: the
		// count starts at the 2048 default, so the narrow canvas is applied first.
		controller.SetTargetColumnCount(256);
		controller.ActiveLayer.Should().Be(AggregationLayer.Minute);

		controller.SetTargetColumnCount(2048);
		controller.ActiveLayer.Should().Be(AggregationLayer.Raw);
	}

	// A pixel-by-pixel drag across the whole width range crosses the three quantisation steps of the
	// 256…2048 range and the one layer boundary a four-hour window has inside it.
	[Theory]
	[InlineData(2048, 256)]
	[InlineData(256, 2048)]
	public void MonotonicResizeAcrossABoundary_ChangesTheLayerOncePerDragAndReQueriesOncePerStep(
		int fromColumns,
		int toColumns)
	{
		var controller = Loaded();
		var currentWidth = controller.To - controller.From;
		controller.ZoomAt(TimeSpan.FromHours(4.0) / currentWidth, controller.To);
		controller.SetTargetColumnCount(fromColumns);

		var requeries = 0;
		var layerChanges = 0;
		var previousLayer = controller.ActiveLayer;
		controller.WindowChanged += (_, window) =>
		{
			if (window.RequiresHistoryRequery)
			{
				requeries++;
			}

			if (window.Layer != previousLayer)
			{
				layerChanges++;
				previousLayer = window.Layer;
			}
		};

		var step = fromColumns < toColumns ? 1 : -1;
		for (var columns = fromColumns; columns != toColumns + step; columns += step)
		{
			controller.SetTargetColumnCount(columns);
		}

		layerChanges.Should().Be(1);
		requeries.Should().Be(3);
	}

	[Fact]
	public void UnchangedQuantizedColumnCount_RaisesNoWindowChange()
	{
		var controller = Loaded();
		var raised = 0;
		controller.WindowChanged += (_, _) => raised++;

		// Every pixel from 1317 up to the 2048 clamp stays inside the deadband around 2048, the value
		// already in force.
		controller.SetTargetColumnCount(2048);
		controller.SetTargetColumnCount(1800);
		controller.SetTargetColumnCount(1500);

		raised.Should().Be(0);
		controller.TargetColumnCount.Should().Be(2048);
	}

	[Fact]
	public void SetTargetColumnCount_ClampsToTheCanvasBounds()
	{
		var controller = Loaded();

		controller.TargetColumnCount.Should().Be(HistoryColumnTarget.MaxColumns);

		controller.SetTargetColumnCount(1);
		controller.TargetColumnCount.Should().Be(HistoryColumnTarget.MinColumns);

		controller.SetTargetColumnCount(100_000);
		controller.TargetColumnCount.Should().Be(HistoryColumnTarget.MaxColumns);
	}

	[Fact]
	public void SetTargetColumnCount_QuantizesGeometricallyNotArithmetically()
	{
		var controller = Loaded();

		// 740 px is above the geometric midpoint of 512 and 1024 (724 px) but below their arithmetic one
		// (768 px): geometric quantization answers 1024, arithmetic would answer 512.
		controller.SetTargetColumnCount(740);

		controller.TargetColumnCount.Should().Be(1024);
	}

	// 724 and 725 px sit either side of the naked 512/1024 boundary.
	[Fact]
	public void SetTargetColumnCount_HoldsAcrossPixelJitterAtAQuantizationBoundary()
	{
		var controller = Loaded();
		controller.SetTargetColumnCount(700);
		controller.TargetColumnCount.Should().Be(512);

		controller.SetTargetColumnCount(724);
		controller.TargetColumnCount.Should().Be(512);

		controller.SetTargetColumnCount(725);
		controller.TargetColumnCount.Should().Be(512);

		// Clearing the boundary by the 10% deadband margin (797 px) does move it, one step.
		controller.SetTargetColumnCount(800);
		controller.TargetColumnCount.Should().Be(1024);
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

	[Theory]
	[InlineData(AggregationLayer.Raw, AggregationLayer.Minute)]
	[InlineData(AggregationLayer.Minute, AggregationLayer.Hour)]
	[InlineData(AggregationLayer.Hour, AggregationLayer.Day)]
	public void HysteresisHoldsTheCurrentLayerJustPastItsCeiling(
		AggregationLayer layer,
		AggregationLayer nextCoarser)
	{
		var ceiling = Ceiling(layer, TestColumnCount);

		ChartNavigationController.LayerForWidth(ceiling * 1.05, layer, TestColumnCount).Should().Be(layer);
		ChartNavigationController.LayerForWidth(ceiling * 1.15, layer, TestColumnCount)
			.Should().Be(nextCoarser);
	}

	// The acceptance table for the layer-selection ladder, pinned at its literal widths. Every width here
	// sits clear of the 10% hysteresis band, so the answer does not depend on which layer the ladder was
	// entered from.
	[Theory]
	[InlineData(3.0, 1024, AggregationLayer.Raw)]
	[InlineData(72.0, 1024, AggregationLayer.Minute)]
	[InlineData(2400.0, 1024, AggregationLayer.Hour)]
	[InlineData(4.0, 256, AggregationLayer.Minute)]
	[InlineData(4.0, 2048, AggregationLayer.Raw)]
	public void AcceptanceWindowWidths_SelectTheRequiredLayer(
		double windowHours,
		int columnCount,
		AggregationLayer expectedLayer)
	{
		ChartNavigationController
			.LayerForWidth(TimeSpan.FromHours(windowHours), AggregationLayer.Raw, columnCount)
			.Should().Be(expectedLayer);
	}

	// Asserted through pan clamping, not a new first-sample accessor: the controller exposes none and the
	// model's FirstSample is private to it. Panning 30 days left must stop at the archive's first sample,
	// not at the constructor's startup-minus-one-hour.
	[Fact]
	public void SeedFromArchiveExtent_OpensTheWindowOnAnArchiveWhollyInThePast()
	{
		var controller = new ChartNavigationController();

		controller.SeedFromArchiveExtent(new ArchiveExtent(_first, _last));

		controller.To.Should().Be(_last);
		controller.From.Should().Be(_last - TimeSpan.FromHours(1.0));

		controller.PanBy(TimeSpan.FromDays(-30.0));

		controller.From.Should().Be(_first);
	}

	[Fact]
	public void SeedFromArchiveExtent_WithAnExtentCoveringNow_ClampsPanningAtItsFirstSample()
	{
		var controller = new ChartNavigationController();
		var now = DateTime.UtcNow;
		var extent = new ArchiveExtent(now - TimeSpan.FromDays(2.0), now);

		controller.SeedFromArchiveExtent(extent);

		controller.To.Should().Be(extent.LastUtc);
		controller.IsSticky.Should().BeTrue();

		controller.PanBy(TimeSpan.FromDays(-30.0));

		controller.From.Should().Be(extent.FirstUtc);
	}

	[Fact]
	public void SeedFromArchiveExtent_WithAnEmptyExtent_LeavesTheWindowOnTheWallClock()
	{
		var controller = new ChartNavigationController();
		var raised = 0;
		controller.WindowChanged += (_, _) => raised++;

		controller.SeedFromArchiveExtent(ArchiveExtent.Empty);

		raised.Should().Be(0);
		controller.To.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1.0));

		// The has-data latch stays clear, so the first envelope carrying rows still snaps the window.
		controller.TrackDataExtents(_first, _last);
		controller.To.Should().Be(_last);
	}

	// The seed sets the has-data latch, so the first history envelope takes the no-op branch of
	// TrackDataExtents instead of re-snapping the window and undoing the seed.
	[Fact]
	public void SeedFromArchiveExtent_SurvivesTheFirstHistoryEnvelope()
	{
		var controller = new ChartNavigationController();
		controller.SeedFromArchiveExtent(new ArchiveExtent(_first, _last));

		controller.TrackDataExtents(_last.AddHours(-1.0), _last.AddMinutes(-30.0));

		controller.To.Should().Be(_last);
		controller.From.Should().Be(_last - TimeSpan.FromHours(1.0));
	}

	// ceiling(layer) = nextCoarser(layer).ToPointSpacing() * columns. The spacings are spelled out rather
	// than read back from the production helper, so a wrong spacing or a wrong next-coarser rule fails these
	// tests instead of moving with them.
	private static TimeSpan Ceiling(AggregationLayer layer, int columnCount)
	{
		var nextCoarserSpacing = layer switch
		{
			AggregationLayer.Raw => TimeSpan.FromSeconds(15.0),
			AggregationLayer.Minute => TimeSpan.FromMinutes(15.0),
			AggregationLayer.Hour => TimeSpan.FromHours(6.0),
			_ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "The day layer has no ceiling.")
		};

		return nextCoarserSpacing * columnCount;
	}

	private static ChartNavigationController Loaded()
	{
		var controller = new ChartNavigationController();
		controller.TrackDataExtents(_first, _last);

		return controller;
	}
}
