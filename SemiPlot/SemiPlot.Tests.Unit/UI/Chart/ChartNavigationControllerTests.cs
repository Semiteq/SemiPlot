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
	public void Zoom_RaisesWindowChangeRequiringReQuery()
	{
		var controller = Loaded();
		NavigationWindow? raised = null;
		controller.WindowChanged += (_, window) => raised = window;
		var widthBefore = controller.To - controller.From;

		controller.ZoomAt(4.0, _last);

		(controller.To - controller.From).Should().BeGreaterThan(widthBefore);
		raised.Should().NotBeNull();
		raised!.RequiresHistoryRequery.Should().BeTrue();
	}

	[Fact]
	public void Pan_RaisesWindowChangeRequiringReQuery()
	{
		var controller = Loaded();
		NavigationWindow? raised = null;
		controller.WindowChanged += (_, window) => raised = window;

		controller.PanBy(TimeSpan.FromMinutes(-5.0));

		raised.Should().NotBeNull();
		raised!.RequiresHistoryRequery.Should().BeTrue();
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
	public void SetStickyAndJumpToNow_ReattachToTheLiveEdge()
	{
		var controller = Loaded();
		controller.SetSticky(false);
		controller.IsSticky.Should().BeFalse();

		controller.SetSticky(true);

		controller.IsSticky.Should().BeTrue();
		controller.To.Should().Be(_last);

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

	[Fact]
	public void NewestSample_OnAFreshController_IsNull()
	{
		var controller = new ChartNavigationController();

		controller.NewestSample.Should().BeNull("the constructor's wall clock is no sample");
	}

	[Fact]
	public void TrackDataExtents_SetsTheNewestSampleAndRaisesItsMove()
	{
		var controller = new ChartNavigationController();
		var moves = new List<DateTime>();
		controller.NewestSampleMoved += (_, sample) => moves.Add(sample);

		controller.TrackDataExtents(_first, _last);

		controller.NewestSample.Should().Be(_last);
		moves.Should().Equal(_last);
	}

	[Fact]
	public void OnLiveEdge_SetsTheNewestSampleAndRaisesEachMove_StickyOrNot()
	{
		var controller = Loaded();
		var moves = new List<DateTime>();
		controller.NewestSampleMoved += (_, sample) => moves.Add(sample);
		var whileSticky = _last.AddMinutes(1.0);
		var whileDetached = _last.AddMinutes(2.0);

		controller.OnLiveEdge(whileSticky);
		controller.SetSticky(false);
		controller.OnLiveEdge(whileDetached);

		controller.NewestSample.Should().Be(whileDetached);
		moves.Should().Equal(whileSticky, whileDetached);
	}

	[Fact]
	public void NewestSampleMoved_SeesTheMovedWindowAndPrecedesWindowChanged()
	{
		var controller = new ChartNavigationController();
		var raised = new List<(string Event, DateTime To)>();
		controller.NewestSampleMoved += (_, _) => raised.Add((nameof(controller.NewestSampleMoved), controller.To));
		controller.WindowChanged += (_, window) => raised.Add((nameof(controller.WindowChanged), window.To));
		var advanced = _last.AddMinutes(2.0);

		controller.TrackDataExtents(_first, _last);
		controller.OnLiveEdge(advanced);

		raised.Should().Equal(
			(nameof(controller.NewestSampleMoved), _last),
			(nameof(controller.WindowChanged), _last),
			(nameof(controller.NewestSampleMoved), advanced),
			(nameof(controller.WindowChanged), advanced));
	}

	[Fact]
	public void OnLiveEdge_WithASampleOlderThanTheNewest_LeavesItAndRaisesNothing()
	{
		var controller = Loaded();
		var moves = 0;
		controller.NewestSampleMoved += (_, _) => moves++;

		controller.OnLiveEdge(_last.AddMinutes(-1.0));

		controller.NewestSample.Should().Be(_last);
		moves.Should().Be(0);
	}

	// Half a layer's ceiling sits well inside it, clear of the 25% zoom quantisation step and the 10%
	// hysteresis band. The Hour row's 1.2x minute ceiling, 12.8 days at 1024 columns, stays inside the 365-day
	// maximum window, so the answer comes from the ladder, not from TrendNavigationModel truncating the width.
	[Theory]
	[InlineData(AggregationLayer.Raw, 0.5, AggregationLayer.Raw)]
	[InlineData(AggregationLayer.Minute, 0.5, AggregationLayer.Minute)]
	[InlineData(AggregationLayer.Minute, 1.2, AggregationLayer.Hour)]
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
				Ceiling(AggregationLayer.Minute, columnCount), AggregationLayer.Raw, columnCount, 1)
			.Should().Be(AggregationLayer.Minute);

		ChartNavigationController.LayerForWidth(
				Ceiling(AggregationLayer.Raw, columnCount), AggregationLayer.Minute, columnCount, 1)
			.Should().Be(AggregationLayer.Raw);
	}

	// A pixel-by-pixel drag across the whole width range crosses the three quantisation steps of the
	// 256..2048 range and the one layer boundary a four-hour window has inside it.
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

	[Theory]
	[InlineData(AggregationLayer.Raw, AggregationLayer.Minute)]
	[InlineData(AggregationLayer.Minute, AggregationLayer.Hour)]
	public void HysteresisHoldsTheCurrentLayerJustPastItsCeiling(
		AggregationLayer layer,
		AggregationLayer nextCoarser)
	{
		var ceiling = Ceiling(layer, TestColumnCount);

		ChartNavigationController.LayerForWidth(ceiling * 1.05, layer, TestColumnCount, 1).Should().Be(layer);
		ChartNavigationController.LayerForWidth(ceiling * 1.15, layer, TestColumnCount, 1)
			.Should().Be(nextCoarser);
	}

	// Hour has no ceiling of its own to widen.
	[Theory]
	[InlineData(256)]
	[InlineData(2048)]
	public void FromTheHourLayerTheMinuteCeilingCarriesNoHysteresis(int columnCount)
	{
		var minuteCeiling = Ceiling(AggregationLayer.Minute, columnCount);

		ChartNavigationController.LayerForWidth(minuteCeiling, AggregationLayer.Hour, columnCount, 1)
			.Should().Be(AggregationLayer.Minute);
		ChartNavigationController.LayerForWidth(minuteCeiling * 1.05, AggregationLayer.Hour, columnCount, 1)
			.Should().Be(AggregationLayer.Hour);
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
	[InlineData(8760.0, 256, AggregationLayer.Hour)]
	public void AcceptanceWindowWidths_SelectTheRequiredLayer(
		double windowHours,
		int columnCount,
		AggregationLayer expectedLayer)
	{
		ChartNavigationController
			.LayerForWidth(TimeSpan.FromHours(windowHours), AggregationLayer.Raw, columnCount, 1)
			.Should().Be(expectedLayer);
	}

	[Fact]
	public void SeedFromArchiveExtent_OpensTheWindowOnAnArchiveWhollyInThePast()
	{
		var controller = new ChartNavigationController();

		controller.SeedFromArchiveExtent(new ArchiveExtent(_first, _last));

		controller.To.Should().Be(_last);
		controller.From.Should().Be(_last - TimeSpan.FromHours(1.0));

		controller.FirstSample.Should().Be(_first);
	}

	[Fact]
	public void SeedFromArchiveExtent_WithAnEmptyExtent_LeavesTheWindowOnTheWallClock()
	{
		var controller = new ChartNavigationController();
		var constructedTo = controller.To;
		var raised = 0;
		controller.WindowChanged += (_, _) => raised++;

		controller.SeedFromArchiveExtent(ArchiveExtent.Empty);

		raised.Should().Be(0);
		controller.To.Should().Be(constructedTo);

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

	[Fact]
	public void WidenToArchiveExtent_WithAnEarlierFirstSample_MovesThePanFloorAndNotTheWindow()
	{
		var controller = new ChartNavigationController();
		controller.SeedFromArchiveExtent(new ArchiveExtent(_first, _last));
		var raised = 0;
		controller.WindowChanged += (_, _) => raised++;
		var earlier = _first - TimeSpan.FromDays(10.0);

		controller.WidenToArchiveExtent(new ArchiveExtent(earlier, _last));

		raised.Should().Be(0);
		controller.To.Should().Be(_last);
		controller.IsSticky.Should().BeTrue();
		controller.FirstSample.Should().Be(earlier);

		controller.PanBy(TimeSpan.FromDays(-30.0));

		controller.From.Should().Be(earlier);
	}

	[Fact]
	public void WidenToArchiveExtent_WithALaterOrAnEmptyExtent_KeepsThePanFloor()
	{
		var controller = new ChartNavigationController();
		controller.SeedFromArchiveExtent(new ArchiveExtent(_first, _last));

		controller.WidenToArchiveExtent(new ArchiveExtent(_first.AddHours(1.0), _last));
		controller.WidenToArchiveExtent(ArchiveExtent.Empty);

		controller.FirstSample.Should().Be(_first);
	}

	// Each ceiling is probed from the other side of the ladder, so no hysteresis band widens it.
	[Theory]
	[InlineData(256, 0)]
	[InlineData(256, 1)]
	[InlineData(256, ChartNavigationController.ReferencePenCount)]
	[InlineData(2048, 0)]
	[InlineData(2048, 1)]
	[InlineData(2048, ChartNavigationController.ReferencePenCount)]
	public void AtTheReferencePenCountOrFewer_TheCeilingsAreUnshrunk(int columnCount, int visiblePenCount)
	{
		AssertCeilings(
			columnCount,
			visiblePenCount,
			Ceiling(AggregationLayer.Raw, columnCount),
			Ceiling(AggregationLayer.Minute, columnCount));
	}

	[Theory]
	[InlineData(256)]
	[InlineData(2048)]
	public void AtTwiceTheReferencePenCount_EachCeilingIsHalved(int columnCount)
	{
		AssertCeilings(
			columnCount,
			2 * ChartNavigationController.ReferencePenCount,
			Ceiling(AggregationLayer.Raw, columnCount) / 2.0,
			Ceiling(AggregationLayer.Minute, columnCount) / 2.0);
	}

	[Theory]
	[InlineData(AggregationLayer.Raw, AggregationLayer.Minute)]
	[InlineData(AggregationLayer.Minute, AggregationLayer.Hour)]
	public void AtTwiceTheReferencePenCount_HysteresisWidensOnlyTheLayerInForce(
		AggregationLayer layer,
		AggregationLayer nextCoarser)
	{
		const int VisiblePenCount = 2 * ChartNavigationController.ReferencePenCount;
		var halvedCeiling = Ceiling(layer, TestColumnCount) / 2.0;

		ChartNavigationController.LayerForWidth(halvedCeiling * 1.05, layer, TestColumnCount, VisiblePenCount)
			.Should().Be(layer);
		ChartNavigationController.LayerForWidth(halvedCeiling * 1.15, layer, TestColumnCount, VisiblePenCount)
			.Should().Be(nextCoarser);
		ChartNavigationController.LayerForWidth(halvedCeiling * 1.05, nextCoarser, TestColumnCount, VisiblePenCount)
			.Should().Be(nextCoarser);
	}

	[Fact]
	public void SetVisiblePenCount_RaisesAWindowChangeOnlyWhenTheLayerMoves()
	{
		var controller = Loaded();
		controller.SetTargetColumnCount(TestColumnCount);
		var pastTheHalvedRawCeiling = Ceiling(AggregationLayer.Raw, TestColumnCount) * 0.75;
		controller.ZoomAt(pastTheHalvedRawCeiling / (controller.To - controller.From), controller.To);
		controller.ActiveLayer.Should().Be(AggregationLayer.Raw);
		var raised = new List<NavigationWindow>();
		controller.WindowChanged += (_, window) => raised.Add(window);

		controller.SetVisiblePenCount(ChartNavigationController.ReferencePenCount);
		raised.Should().BeEmpty();

		controller.SetVisiblePenCount(2 * ChartNavigationController.ReferencePenCount);

		controller.ActiveLayer.Should().Be(AggregationLayer.Minute);
		raised.Should().ContainSingle().Which.Should().Match<NavigationWindow>(window =>
			window.Layer == AggregationLayer.Minute && window.RequiresHistoryRequery);
	}

	// The width sits under the unshrunk Raw ceiling, which carries no hysteresis while Minute is in force.
	[Fact]
	public void SetVisiblePenCount_DroppingBackToOnePen_ReturnsToTheFinerLayer()
	{
		var controller = Loaded();
		controller.SetTargetColumnCount(TestColumnCount);
		var rawCeiling = Ceiling(AggregationLayer.Raw, TestColumnCount);
		controller.ZoomAt(rawCeiling * 0.75 / (controller.To - controller.From), controller.To);
		controller.SetVisiblePenCount(2 * ChartNavigationController.ReferencePenCount);
		controller.ActiveLayer.Should().Be(AggregationLayer.Minute);
		(controller.To - controller.From).Should().BeLessThan(rawCeiling);
		var raised = new List<NavigationWindow>();
		controller.WindowChanged += (_, window) => raised.Add(window);

		controller.SetVisiblePenCount(1);

		controller.ActiveLayer.Should().Be(AggregationLayer.Raw);
		raised.Should().ContainSingle().Which.Should().Match<NavigationWindow>(window =>
			window.Layer == AggregationLayer.Raw && window.RequiresHistoryRequery);
	}

	private static void AssertCeilings(
		int columnCount,
		int visiblePenCount,
		TimeSpan rawCeiling,
		TimeSpan minuteCeiling)
	{
		var justPast = TimeSpan.FromTicks(1);

		ChartNavigationController.LayerForWidth(rawCeiling, AggregationLayer.Minute, columnCount, visiblePenCount)
			.Should().Be(AggregationLayer.Raw);
		ChartNavigationController
			.LayerForWidth(rawCeiling + justPast, AggregationLayer.Minute, columnCount, visiblePenCount)
			.Should().Be(AggregationLayer.Minute);
		ChartNavigationController.LayerForWidth(minuteCeiling, AggregationLayer.Raw, columnCount, visiblePenCount)
			.Should().Be(AggregationLayer.Minute);
		ChartNavigationController
			.LayerForWidth(minuteCeiling + justPast, AggregationLayer.Raw, columnCount, visiblePenCount)
			.Should().Be(AggregationLayer.Hour);
	}

	// ceiling(layer) = nextCoarser(layer).ToPointSpacing() * columns. The spacings are spelled out rather
	// than read from ToPointSpacing, so a wrong spacing fails these tests instead of moving with them.
	private static TimeSpan Ceiling(AggregationLayer layer, int columnCount)
	{
		var nextCoarserSpacing = layer switch
		{
			AggregationLayer.Raw => TimeSpan.FromSeconds(15.0),
			AggregationLayer.Minute => TimeSpan.FromMinutes(15.0),
			_ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "The hour layer has no ceiling.")
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
