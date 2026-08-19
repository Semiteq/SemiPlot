using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using ScottPlot;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendChartViewModelTests
{
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33);
	private static readonly TimeSpan _historyDebounceWindow = TimeSpan.FromMilliseconds(150);
	private static readonly DateTime _from = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _to = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

	[AvaloniaFact]
	public void AddPen_RegistersPenStateOnce()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var pen = new Pen(7, "Heater", "Group A", "#ff0000");

		var first = viewModel.AddPen(pen);
		var second = viewModel.AddPen(pen);

		viewModel.Pens.Should().ContainSingle();
		viewModel.FindPen(7).Should().BeSameAs(first);
		second.Should().BeSameAs(first);
	}

	[AvaloniaFact]
	public void RemovePen_DropsThePenState()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(7, "Heater", "Group A", "#ff0000"));

		var removed = viewModel.RemovePen(7);

		removed.Should().BeTrue();
		viewModel.Pens.Should().BeEmpty();
		viewModel.FindPen(7).Should().BeNull();
	}

	[AvaloniaFact]
	public void RemovePen_UnknownPen_ReturnsFalse()
	{
		var (viewModel, _, _, _) = CreateViewModel();

		viewModel.RemovePen(99).Should().BeFalse();
	}

	[AvaloniaFact]
	public void SetPenVisibility_TogglesPenAndPlottableState()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(7, "Heater", "Group A", "#ff0000"));

		viewModel.SetPenVisibility(7, false).Should().BeTrue();

		state.IsVisible.Should().BeFalse();
		state.CenterLine.IsVisible.Should().BeFalse();
		state.Band.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task History_LoadsCenterValueForKnownPen()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		await LoadInitialHistory(viewModel, _from, _to);

		viewModel.FindPen(1)!.CurrentValue.Should().Be(2.0);
		viewModel.FindPen(1)!.CenterPoints.Should().HaveCount(2);
	}

	[AvaloniaFact]
	public async Task CenterLine_ScatterDataSourceReflectsLoadedAndAppendedPoints()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		await LoadInitialHistory(viewModel, _from, _to);

		// Reading the plottable's own data source (not the backing field) proves the center line renders
		// the same buffer LoadHistory mutates.
		var loadedPoints = state.CenterLine.Data.GetScatterPoints();
		loadedPoints.Should().HaveCount(2);
		loadedPoints[1].Y.Should().Be(2.0);

		state.AppendRealtime(_to.AddMinutes(1.0), 7.0);

		var appendedPoints = state.CenterLine.Data.GetScatterPoints();
		appendedPoints.Should().HaveCount(3);
		appendedPoints[2].Y.Should().Be(7.0);
	}

	[AvaloniaFact]
	public void RealtimeBatch_UpdatesPerPenCurrentValue()
	{
		var (viewModel, scheduler, coordinator, _) = CreateViewModel(realtimeInterval: TimeSpan.FromMilliseconds(10));
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		coordinator.Start();
		scheduler.AdvanceBy(_batchWindow.Ticks);

		var pen = viewModel.FindPen(1)!;
		pen.CurrentValue.Should().NotBeNull();
		pen.CenterPoints.Should().NotBeEmpty();
	}

	[AvaloniaFact]
	public void SetActivePen_UpdatesActivePenId()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		viewModel.AddPen(new Pen(2, "Pen 2", "Group B", "#00ff00"));

		viewModel.SetActivePen(2).Should().BeTrue();

		viewModel.ActivePenId.Should().Be(2);
	}

	[AvaloniaFact]
	public void AddPen_FirstPenBecomesActive()
	{
		var (viewModel, _, _, _) = CreateViewModel();

		viewModel.AddPen(new Pen(5, "Pen 5", "Group A", "#ff0000"));

		viewModel.ActivePenId.Should().Be(5);
	}

	[AvaloniaFact]
	public void SetAxisLimits_SwitchesPenToManualWithFixedRange()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		viewModel.SetAxisLimits(1, 10.0, 90.0).Should().BeTrue();

		var settings = viewModel.ScaleSettings[1];
		settings.Mode.Should().Be(ScaleMode.Manual);
		settings.ManualMin.Should().Be(10.0);
		settings.ManualMax.Should().Be(90.0);
	}

	[AvaloniaFact]
	public void AutoscaleAxis_RevertsPenToAutoMode()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		viewModel.SetAxisLimits(1, 10.0, 90.0);

		viewModel.AutoscaleAxis(1).Should().BeTrue();

		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Auto);
	}

	[AvaloniaFact]
	public void ManualLimits_DriveTheOwningAxisRange()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		viewModel.SetAxisLimits(1, 10.0, 90.0);

		var axis = state.CenterLine.Axes.YAxis;
		axis.Min.Should().Be(10.0);
		axis.Max.Should().Be(90.0);
	}

	[AvaloniaFact]
	public void SameGroupPens_ShareOneYAxis()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var first = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var second = viewModel.AddPen(new Pen(2, "Pen 2", "Group A", "#00ff00"));

		first.CenterLine.Axes.YAxis.Should().BeSameAs(second.CenterLine.Axes.YAxis);
	}

	[AvaloniaFact]
	public void DistinctGroupPens_GetSeparateYAxes()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var first = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var second = viewModel.AddPen(new Pen(2, "Pen 2", "Group B", "#00ff00"));

		first.CenterLine.Axes.YAxis.Should().NotBeSameAs(second.CenterLine.Axes.YAxis);
	}

	[AvaloniaFact]
	public void ZoomOut_DrivesACoarserLayerReQuery()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Minute);
		provider.LastQueriedLayer.Should().Be(AggregationLayer.Minute);
	}

	[AvaloniaFact]
	public async Task PanBackward_ReQueriesShiftedWindow()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, _from.AddDays(-1.0), _to);
		var beforeFrom = viewModel.Navigation.From;

		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-10.0));
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		viewModel.Navigation.From.Should().BeBefore(beforeFrom);
		provider.LastQueriedFromUtc.Should().Be(viewModel.Navigation.From);
	}

	[AvaloniaFact]
	public async Task FoldRealtime_WidensCurrentColumnInsteadOfAddingAPoint()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, _from, _to);
		var columnsBefore = state.CenterPoints.Count;

		state.FoldRealtime(99.0);

		state.CenterPoints.Should().HaveCount(columnsBefore);
		state.CurrentValue.Should().Be(99.0);
	}

	[AvaloniaFact]
	public void SteppedPen_MapsToStepHorizontalConnectStyle()
	{
		var (viewModel, _, _, _) = CreateViewModel();

		var state = viewModel.AddPen(new Pen(7, "Damper", "Dampers", "#ff0000", PenLineStyle.Stepped));

		state.CenterLine.ConnectStyle.Should().Be(ConnectStyle.StepHorizontal);
	}

	[AvaloniaFact]
	public void InterpolatedPen_MapsToStraightConnectStyle()
	{
		var (viewModel, _, _, _) = CreateViewModel();

		var state = viewModel.AddPen(new Pen(7, "Heater", "Heaters", "#ff0000", PenLineStyle.Interpolated));

		state.CenterLine.ConnectStyle.Should().Be(ConnectStyle.Straight);
	}

	[AvaloniaFact]
	public void History_WithInteriorGap_PlacesNaNBetweenTwoSegments()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
		var envelope = new PenHistoryEnvelope(
			1,
			[t0, t0.AddMinutes(1.0), t0.AddMinutes(2.0)],
			[1.0, double.NaN, 3.0],
			[1.0, double.NaN, 3.0],
			[1.0, double.NaN, 3.0]);

		state.LoadHistory(envelope);

		state.CenterPoints.Should().HaveCount(3);
		double.IsNaN(state.CenterPoints[0].Y).Should().BeFalse();
		double.IsNaN(state.CenterPoints[1].Y).Should().BeTrue();
		double.IsNaN(state.CenterPoints[2].Y).Should().BeFalse();
	}

	[AvaloniaFact]
	public void Realtime_NullSample_AppendsNaNGap()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var timestamp = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

		state.AppendRealtime(timestamp, value: null);

		state.CenterPoints.Should().ContainSingle();
		double.IsNaN(state.CenterPoints[0].Y).Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task RequestInitialHistory_FiresAHistoryQueryWithoutAnyUserGesture()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		provider.HistoryQueryCount.Should().Be(0);

		await viewModel.RequestInitialHistory();

		provider.HistoryQueryCount.Should().BeGreaterThan(0);
		provider.LastQueriedPenIds.Should().Contain(1);
	}

	[AvaloniaFact]
	public async Task RequestInitialHistory_FiresExactlyOneHistoryQuery_NoDoubleLoad()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		provider.HistoryQueryCount.Should().Be(0);

		await viewModel.RequestInitialHistory();

		// Seed query loads once; the first-data snap must not trigger a second re-query.
		provider.HistoryQueryCount.Should().Be(1);
	}

	[AvaloniaFact]
	public async Task RequestInitialHistory_WithNoPens_DoesNotQuery()
	{
		var (viewModel, _, _, provider) = CreateViewModel();

		await viewModel.RequestInitialHistory();

		provider.HistoryQueryCount.Should().Be(0);
	}

	[AvaloniaFact]
	public async Task RequestInitialHistory_FailedResult_LeavesThePenUnloadedAndDoesNotThrow()
	{
		// Mirrors the debouncer's silent drop (plan Failure-path decision): a failed Result returns
		// without applying, so the pen keeps its unloaded state and no exception escapes.
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		provider.FailHistory = true;

		var act = () => LoadInitialHistory(viewModel, _from, _to);

		await act.Should().NotThrowAsync();
		viewModel.FindPen(1)!.CurrentValue.Should().BeNull();
		viewModel.FindPen(1)!.CenterPoints.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void History_LoadsBandWithTopMaxAndBottomMin()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
		var envelope = new PenHistoryEnvelope(
			1,
			[t0, t0.AddMinutes(1.0)],
			[1.0, 3.0],
			[5.0, 9.0],
			[2.0, 6.0]);

		state.LoadHistory(envelope);

		state.BandPoints.Should().HaveCount(2);
		state.BandPoints[0].Top.Should().Be(5.0);
		state.BandPoints[0].Bottom.Should().Be(1.0);
		state.BandPoints[1].Top.Should().Be(9.0);
		state.BandPoints[1].Bottom.Should().Be(3.0);
	}

	[AvaloniaFact]
	public void Realtime_LiveEdgeBandDegeneratesToMinEqualsMaxEqualsValue()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var timestamp = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

		state.AppendRealtime(timestamp, 42.0);

		state.BandPoints.Should().ContainSingle();
		state.BandPoints[0].Top.Should().Be(42.0);
		state.BandPoints[0].Bottom.Should().Be(42.0);
	}

	[AvaloniaFact]
	public void FoldRealtime_WidensTheBandOfTheCurrentColumn()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
		state.LoadHistory(new PenHistoryEnvelope(1, [t0], [1.0], [5.0], [2.0]));

		state.FoldRealtime(9.0);

		state.BandPoints.Should().ContainSingle();
		state.BandPoints[0].Top.Should().Be(9.0);
		state.BandPoints[0].Bottom.Should().Be(1.0);
		state.CenterPoints[0].Y.Should().Be(9.0);
	}

	[AvaloniaFact]
	public async Task StickyLiveEdgeAdvance_DoesNotReQueryHistory()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, _from, _to);
		viewModel.Navigation.IsSticky.Should().BeTrue();
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.Navigation.OnLiveEdge(viewModel.Navigation.To.AddMinutes(1.0));

		provider.HistoryQueryCount.Should().Be(queriesBefore);
	}

	[AvaloniaFact]
	public async Task StickyLiveEdgeAdvance_StillShiftsTheScaleWindow()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, _from, _to);
		var toBefore = viewModel.Navigation.To;

		viewModel.Navigation.OnLiveEdge(toBefore.AddMinutes(1.0));

		viewModel.Navigation.To.Should().Be(toBefore.AddMinutes(1.0));
	}

	[AvaloniaFact]
	public void Coordinator_CoarseLayerRealtime_FoldsInsteadOfGrowingColumns()
	{
		var (viewModel, scheduler, coordinator, _) = CreateViewModel(
			realtimeInterval: TimeSpan.FromMilliseconds(10));
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		// A coarse (non-Raw) layer folds realtime into the current column instead of appending.
		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		viewModel.Navigation.ActiveLayer.Should().NotBe(AggregationLayer.Raw);
		var columnsBefore = state.CenterPoints.Count;

		coordinator.Start();
		scheduler.AdvanceBy(_batchWindow.Ticks);

		state.CenterPoints.Count.Should().Be(columnsBefore);
	}

	[AvaloniaFact]
	public void Coordinator_RawLayerRealtime_AppendsColumns()
	{
		var (viewModel, scheduler, coordinator, _) = CreateViewModel(
			realtimeInterval: TimeSpan.FromMilliseconds(10));
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);
		var columnsBefore = state.CenterPoints.Count;

		coordinator.Start();
		scheduler.AdvanceBy(_batchWindow.Ticks);

		state.CenterPoints.Count.Should().BeGreaterThan(columnsBefore);
	}

	[AvaloniaFact]
	public void EveryPlottable_UsesTheSharedBottomXAxis()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		viewModel.AddPen(new Pen(2, "Pen 2", "Group B", "#00ff00"));

		var bottom = viewModel.Plot.Axes.Bottom;
		foreach (var pen in viewModel.Pens)
		{
			pen.CenterLine.Axes.XAxis.Should().BeSameAs(bottom);
			pen.Band.Axes.XAxis.Should().BeSameAs(bottom);
		}
	}

	[AvaloniaFact]
	public void RapidZoom_EmitsExactlyOneTrailingHistoryRequest()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		// Each notch fires within the quiet period, so the throttle must collapse them to one trailing query.
		for (var notch = 0; notch < 5; notch++)
		{
			viewModel.Navigation.ZoomAt(2.0, viewModel.Navigation.To);
			scheduler.AdvanceBy(TimeSpan.FromMilliseconds(20).Ticks);
		}

		provider.HistoryQueryCount.Should().Be(0);

		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(1);
	}

	[AvaloniaFact]
	public void AfterStreamGoesQuiet_TheLastWindowIsQueried()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		viewModel.Navigation.ZoomAt(2.0, viewModel.Navigation.To);
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(20).Ticks);
		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		var lastFrom = viewModel.Navigation.From;
		var lastTo = viewModel.Navigation.To;
		var lastLayer = viewModel.Navigation.ActiveLayer;

		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.LastQueriedFromUtc.Should().Be(lastFrom);
		provider.LastQueriedToUtc.Should().Be(lastTo);
		provider.LastQueriedLayer.Should().Be(lastLayer);
	}

	[AvaloniaFact]
	public void ReportedDataAreaWidth_ChangesTheLayerOfAnUnchangedWindow()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var currentWidth = viewModel.Navigation.To - viewModel.Navigation.From;

		// Roughly four hours: 7 s per column at 2048 columns — finer than the minute layer's 15 s spacing —
		// but 56 s per column at 256, where the minute layer fills every column.
		viewModel.Navigation.ZoomAt(TimeSpan.FromHours(4.0) / currentWidth, viewModel.Navigation.To);
		viewModel.ReportDataAreaWidth(2048.0);
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);
		var fromBefore = viewModel.Navigation.From;
		var toBefore = viewModel.Navigation.To;

		viewModel.ReportDataAreaWidth(256.0);

		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Minute);
		viewModel.Navigation.From.Should().Be(fromBefore);
		viewModel.Navigation.To.Should().Be(toBefore);
	}

	[AvaloniaFact]
	public async Task PreRenderDataArea_QueriesAtTheMaximumColumnCount()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		viewModel.Navigation.TargetColumnCount.Should().Be(HistoryColumnTarget.MaxColumns);
		await LoadInitialHistory(viewModel, _from, _to);

		provider.LastQueriedTargetColumnCount.Should().Be(HistoryColumnTarget.MaxColumns);
	}

	[AvaloniaFact]
	public async Task ReportedWidth_SetsTheQueryResolutionUnquantized()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		viewModel.ReportDataAreaWidth(700.0);
		viewModel.Navigation.TargetColumnCount.Should().Be(512);

		await LoadInitialHistory(viewModel, _from, _to);

		provider.LastQueriedTargetColumnCount.Should().Be(700);
	}

	[AvaloniaFact]
	public async Task CollapsedCanvas_KeepsTheLastReportedWidth()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		viewModel.ReportDataAreaWidth(700.0);

		viewModel.ReportDataAreaWidth(0.0);

		viewModel.Navigation.TargetColumnCount.Should().Be(512);
		await LoadInitialHistory(viewModel, _from, _to);
		provider.LastQueriedTargetColumnCount.Should().Be(700);
	}

	[AvaloniaFact]
	public void ReportedWidthChangingTheLayer_ReQueriesAtTheNewLayerAndColumnCount()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var currentWidth = viewModel.Navigation.To - viewModel.Navigation.From;

		// Roughly four hours: the raw layer at 2048 columns, the minute layer at 256.
		viewModel.Navigation.ZoomAt(TimeSpan.FromHours(4.0) / currentWidth, viewModel.Navigation.To);
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);
		provider.LastQueriedLayer.Should().Be(AggregationLayer.Raw);

		viewModel.ReportDataAreaWidth(256.0);
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.LastQueriedLayer.Should().Be(AggregationLayer.Minute);
		provider.LastQueriedTargetColumnCount.Should().Be(256);
	}

	[AvaloniaFact]
	public void WidthReportedBeforeTheInitialHistoryLands_ReQueriesTheSnappedWindow()
	{
		// Startup race: the seam reports a width while the initial query is in flight; nothing may be queried
		// until the snap.
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		provider.GatedLayer = AggregationLayer.Raw;

		_ = viewModel.RequestInitialHistory();

		viewModel.ReportDataAreaWidth(256.0);
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(1);

		// The archive tail lags wall clock by an hour, so applying the initial result moves the window.
		var archiveTail = DateTime.UtcNow.AddHours(-1.0);
		provider.GatedLayer = null;
		provider.HistoryGate.SetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
		[
			new PenHistoryEnvelope(
				1, [archiveTail.AddHours(-1.0), archiveTail], [1.0, 1.0], [1.0, 1.0], [1.0, 1.0])
		]));

		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(2);
		provider.LastQueriedFromUtc.Should().Be(viewModel.Navigation.From);
		provider.LastQueriedToUtc.Should().Be(viewModel.Navigation.To);
		provider.LastQueriedTargetColumnCount.Should().Be(256);
	}

	[AvaloniaFact]
	public void WidthReportedWhileAnInitialHistoryThatFailsIsInFlight_StillReQueriesOnce()
	{
		// The gate must open on the failure path too: a failed initial query never snaps the window, so the
		// held re-query has to be issued anyway or the canvas keeps the pre-render resolution until the next
		// gesture.
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		provider.GatedLayer = AggregationLayer.Raw;

		_ = viewModel.RequestInitialHistory();
		viewModel.ReportDataAreaWidth(256.0);
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.GatedLayer = null;
		provider.HistoryGate.SetResult(Result.Fail<IReadOnlyList<PenHistoryEnvelope>>("Forced failure."));
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(2);
		provider.LastQueriedTargetColumnCount.Should().Be(256);
	}

	[AvaloniaFact]
	public void DefaultLeftButtonTool_IsPan()
	{
		var (viewModel, _, _, _) = CreateViewModel();

		viewModel.ActiveLeftButtonTool.Should().Be(LeftButtonTool.Pan);
	}

	[AvaloniaFact]
	public void EnteringDeltaMode_SwitchesLeftButtonToolToDeltaPlacement()
	{
		var (viewModel, _, _, _) = CreateViewModel();

		viewModel.SetDeltaModeEnabled(true);

		viewModel.IsDeltaModeEnabled.Should().BeTrue();
		viewModel.ActiveLeftButtonTool.Should().Be(LeftButtonTool.DeltaPlacement);
	}

	[AvaloniaFact]
	public async Task DeltaMode_TwoClicks_PlaceBothCursorsAndSurfaceDeltaTimeAndActivePenDeltaY()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, _from, _to);
		viewModel.SetDeltaModeEnabled(true);

		viewModel.PlaceDeltaCursor(_from);
		viewModel.PlaceDeltaCursor(_to);

		viewModel.DeltaFirstCursor.Should().Be(_from);
		viewModel.DeltaSecondCursor.Should().Be(_to);
		viewModel.DeltaReadout.Should().NotBeNull();
		viewModel.DeltaReadout!.DeltaTime.Should().Be(_to - _from);
		viewModel.DeltaReadout.DeltaY.Should().Be(1.0);
		viewModel.DeltaReadoutText.Should().Contain("Δt").And.Contain("Δy");
	}

	[AvaloniaFact]
	public async Task DeltaMode_RoutesLeftButtonToDeltaPlacementInsteadOfPan()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, _from, _to);
		viewModel.SetDeltaModeEnabled(true);

		viewModel.ActiveLeftButtonTool.Should().Be(LeftButtonTool.DeltaPlacement);
	}

	[AvaloniaFact]
	public async Task ExitingDeltaMode_ReturnsToPanAndClearsCursors()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, _from, _to);
		viewModel.SetDeltaModeEnabled(true);
		viewModel.PlaceDeltaCursor(_from);
		viewModel.PlaceDeltaCursor(_to);

		viewModel.SetDeltaModeEnabled(false);

		viewModel.IsDeltaModeEnabled.Should().BeFalse();
		viewModel.ActiveLeftButtonTool.Should().Be(LeftButtonTool.Pan);
		viewModel.DeltaFirstCursor.Should().BeNull();
		viewModel.DeltaSecondCursor.Should().BeNull();
		viewModel.DeltaReadout.Should().BeNull();
		viewModel.DeltaReadoutText.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task LeftDrag_PansTheNavigationWindow_WithoutPlacingACursorOrZooming()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, _from.AddDays(-1.0), _to);
		var fromBefore = viewModel.Navigation.From;
		var toBefore = viewModel.Navigation.To;
		var widthBefore = toBefore - fromBefore;

		viewModel.BeginDrag();
		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-10.0));
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);
		viewModel.EndDrag();

		viewModel.ActiveLeftButtonTool.Should().Be(LeftButtonTool.Pan);
		viewModel.Navigation.From.Should().BeBefore(fromBefore);
		viewModel.Navigation.To.Should().BeBefore(toBefore);
		(viewModel.Navigation.To - viewModel.Navigation.From).Should().Be(widthBefore);
		viewModel.DeltaFirstCursor.Should().BeNull();
		viewModel.DeltaReadout.Should().BeNull();
	}

	[AvaloniaFact]
	public async Task HoverDuringDrag_DoesNotPublishTheTraceCursor()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, _from, _to);

		viewModel.BeginDrag();
		viewModel.MoveCursor(_from.AddMinutes(30.0));

		viewModel.IsDragging.Should().BeTrue();
		viewModel.CursorTime.Should().BeNull();
		viewModel.CursorValues.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task HoverAfterDragEnds_PublishesTheTraceCursorAgain()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, _from, _to);

		viewModel.BeginDrag();
		viewModel.EndDrag();
		viewModel.MoveCursor(_from.AddMinutes(30.0));

		viewModel.IsDragging.Should().BeFalse();
		viewModel.CursorTime.Should().Be(_from.AddMinutes(30.0));
	}

	[AvaloniaFact]
	public async Task RequestInitialHistory_LoadsEnvelopes_ThenAHigherSequenceGestureResultSupersedesIt()
	{
		// Latest-wins across the unified NextHistorySequence() counter: the initial load applies first
		// (seq 1); a later debounced gesture re-query carries a higher sequence and supersedes it.
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		provider.LayerCenterOverrides[AggregationLayer.Minute] = 5.0;

		await LoadInitialHistory(viewModel, _from, _to);
		viewModel.FindPen(1)!.CurrentValue.Should().Be(2.0);

		// A zoom-out gesture re-queries the coarser layer through the debouncer with a higher sequence.
		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Minute);
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		viewModel.FindPen(1)!.CurrentValue.Should().Be(5.0);
	}

	[AvaloniaFact]
	public void StaleInitialHistory_DoesNotOverwriteANewerDebouncedGestureWindow()
	{
		// Cross-path latest-wins: the initial query (seq 1) is held in flight while a newer gesture query
		// (seq 2) loads its window; the released stale result must be dropped by the sequence guard.
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		provider.GatedLayer = AggregationLayer.Raw;
		_ = viewModel.RequestInitialHistory();

		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		viewModel.Navigation.ActiveLayer.Should().NotBe(AggregationLayer.Raw);
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		viewModel.FindPen(1)!.CurrentValue.Should().Be(2.0);

		// Release the stale initial query with a distinct value; the guard must drop it.
		provider.HistoryGate.SetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
		[
			new PenHistoryEnvelope(1, [_from, _to], [99.0, 99.0], [99.0, 99.0], [99.0, 99.0])
		]));

		viewModel.FindPen(1)!.CurrentValue.Should().Be(2.0);
	}

	[AvaloniaFact]
	public void RequestInitialHistory_ResultArrivingAfterDispose_DoesNotApplyOrThrow()
	{
		// Disposal-safety: the initial query is held in flight, the view model is disposed (disposing the
		// Plot), then the gate is released. The scheduled apply must be cancelled so it never mutates the
		// disposed Plot.
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		provider.GatedLayer = AggregationLayer.Raw;
		var pen = viewModel.FindPen(1)!;

		var request = viewModel.RequestInitialHistory();

		viewModel.Dispose();

		var release = () => provider.HistoryGate.SetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
		[
			new PenHistoryEnvelope(1, [_from, _to], [99.0, 99.0], [99.0, 99.0], [99.0, 99.0])
		]));

		release.Should().NotThrow();
		request.IsCompletedSuccessfully.Should().BeTrue();
		pen.CurrentValue.Should().BeNull();
	}

	// Drives the production initial-load path: snap the navigation window to the test's range through the
	// real first-data extents path, then await the direct QueryHistoryAsync seam. FakeDataProvider returns
	// Task.FromResult on ImmediateScheduler, so the envelopes load synchronously and deterministically.
	// firstSample fixes the earliest stored sample (the pan-backward floor): pass a value before the window
	// start when a test pans into the past.
	private static Task LoadInitialHistory(
		TrendChartViewModel viewModel,
		DateTime firstSample,
		DateTime to)
	{
		viewModel.Navigation.TrackDataExtents(firstSample, to);

		return viewModel.RequestInitialHistory();
	}

	private static (TrendChartViewModel ViewModel, TestScheduler Scheduler, TrendCoordinator Coordinator,
		FakeDataProvider Provider)
		CreateViewModel(TimeSpan? realtimeInterval = null)
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, realtimeInterval ?? TimeSpan.FromMilliseconds(10));
		var coordinator = new TrendCoordinator(
			provider,
			provider.Pens,
			scheduler,
			ImmediateScheduler.Instance,
			_batchWindow);
		var viewModel = new TrendChartViewModel(
			coordinator, scheduler, ImmediateScheduler.Instance, NullLogger<TrendChartViewModel>.Instance);

		return (viewModel, scheduler, coordinator, provider);
	}
}
