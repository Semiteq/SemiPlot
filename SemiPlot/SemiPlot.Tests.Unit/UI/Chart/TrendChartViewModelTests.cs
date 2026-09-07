using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendChartViewModelTests
{
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33);
	private static readonly TimeSpan _historyDebounceWindow = TimeSpan.FromMilliseconds(150);
	private static readonly TimeSpan _testDeadline = TimeSpan.FromSeconds(10.0);
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
		state.Line.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task History_LoadsCenterValueForKnownPen()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		await LoadInitialHistory(viewModel, scheduler, _from, _to);

		viewModel.FindPen(1)!.CurrentValue.Should().Be(2.0);
		viewModel.FindPen(1)!.Columns.Should().HaveCount(2);
	}

	[AvaloniaFact]
	public async Task Line_ReadsTheSameBufferLoadAndAppendMutate()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		await LoadInitialHistory(viewModel, scheduler, _from, _to);

		// Reading through the plottable rather than the backing field proves it renders the buffer the
		// state mutates: the limits are taken from the very list LoadHistory filled.
		state.Line.GetAxisLimits().Top.Should().Be(2.0);

		// Past the end of the prefetched range, so the point is appended rather than folded into it.
		state.AppendRealtime(_to.AddHours(2.0), 7.0);

		state.Line.GetAxisLimits().Top.Should().Be(7.0);
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
		pen.Columns.Should().NotBeEmpty();
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

		var axis = state.Line.Axes.YAxis;
		axis.Min.Should().Be(10.0);
		axis.Max.Should().Be(90.0);
	}

	[AvaloniaFact]
	public void SameGroupPens_ShareOneYAxis()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var first = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var second = viewModel.AddPen(new Pen(2, "Pen 2", "Group A", "#00ff00"));

		first.Line.Axes.YAxis.Should().BeSameAs(second.Line.Axes.YAxis);
	}

	[AvaloniaFact]
	public void DistinctGroupPens_GetSeparateYAxes()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var first = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var second = viewModel.AddPen(new Pen(2, "Pen 2", "Group B", "#00ff00"));

		first.Line.Axes.YAxis.Should().NotBeSameAs(second.Line.Axes.YAxis);
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
	public async Task FoldRealtime_WidensCurrentColumnInsteadOfAddingAPoint()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from, _to);
		var columnsBefore = state.Columns.Count;

		state.FoldRealtime(99.0);

		state.Columns.Should().HaveCount(columnsBefore);
		state.CurrentValue.Should().Be(99.0);
	}

	[AvaloniaFact]
	public void SteppedPen_ReachesThePlottableAsStepped()
	{
		var (viewModel, _, _, _) = CreateViewModel();

		var state = viewModel.AddPen(new Pen(7, "Damper", "Dampers", "#ff0000", PenLineStyle.Stepped));

		state.Line.PenLineStyle.Should().Be(PenLineStyle.Stepped);
	}

	[AvaloniaFact]
	public void InterpolatedPen_ReachesThePlottableAsInterpolated()
	{
		var (viewModel, _, _, _) = CreateViewModel();

		var state = viewModel.AddPen(new Pen(7, "Heater", "Heaters", "#ff0000"));

		state.Line.PenLineStyle.Should().Be(PenLineStyle.Interpolated);
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

		state.Columns.Should().HaveCount(3);
		double.IsNaN(state.Columns[0].Center).Should().BeFalse();
		double.IsNaN(state.Columns[1].Center).Should().BeTrue();
		double.IsNaN(state.Columns[2].Center).Should().BeFalse();
	}

	[AvaloniaFact]
	public void Realtime_NullSample_AppendsNaNGap()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var timestamp = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

		state.AppendRealtime(timestamp, value: null);

		state.Columns.Should().ContainSingle();
		double.IsNaN(state.Columns[0].Center).Should().BeTrue();
	}

	[AvaloniaFact]
	public void RequestInitialHistory_FiresAHistoryQueryWithoutAnyUserGesture()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		provider.HistoryQueryCount.Should().Be(0);

		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().BeGreaterThan(0);
		provider.LastQueriedPenIds.Should().Contain(1);
	}

	[AvaloniaFact]
	public void RequestInitialHistory_FiresExactlyOneHistoryQuery_NoDoubleLoad()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		provider.HistoryQueryCount.Should().Be(0);

		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		// Seed query loads once; the first-data snap must not trigger a second re-query.
		provider.HistoryQueryCount.Should().Be(1);
	}

	[AvaloniaFact]
	public void RequestInitialHistory_WithNoPens_DoesNotQuery()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();

		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(0);
	}

	[AvaloniaFact]
	public async Task RequestInitialHistory_FailedResult_LeavesThePenUnloadedAndDoesNotThrow()
	{
		// Mirrors the debouncer's silent drop: a failed Result returns without applying, so the pen keeps
		// its unloaded state and no exception escapes.
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		provider.FailHistory = true;

		var act = () => LoadInitialHistory(viewModel, scheduler, _from, _to);

		await act.Should().NotThrowAsync();
		viewModel.FindPen(1)!.CurrentValue.Should().BeNull();
		viewModel.FindPen(1)!.Columns.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void History_LoadsColumnsCarryingMinAndMax()
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

		state.Columns.Should().HaveCount(2);
		state.Columns[0].Min.Should().Be(1.0);
		state.Columns[0].Max.Should().Be(5.0);
		state.Columns[1].Min.Should().Be(3.0);
		state.Columns[1].Max.Should().Be(9.0);
	}

	[AvaloniaFact]
	public void Realtime_LiveEdgeColumnDegeneratesToMinEqualsMaxEqualsValue()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var timestamp = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

		state.AppendRealtime(timestamp, 42.0);

		state.Columns.Should().ContainSingle();
		state.Columns[0].Min.Should().Be(42.0);
		state.Columns[0].Max.Should().Be(42.0);
		state.Columns[0].Center.Should().Be(42.0);
	}

	[AvaloniaFact]
	public void Realtime_SampleAtOrBeforeTheLastPoint_IsIgnored()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
		state.LoadHistory(new PenHistoryEnvelope(1, [t0, t0.AddMinutes(1.0)], [1.0, 3.0], [5.0, 9.0], [2.0, 6.0]));

		state.AppendRealtime(t0.AddMinutes(1.0), 42.0);
		state.AppendRealtime(t0, 42.0);

		state.Columns.Should().HaveCount(2);
		state.CurrentValue.Should().Be(6.0);
	}

	[AvaloniaFact]
	public void Realtime_SampleAfterTheLastPoint_IsAppended()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
		state.LoadHistory(new PenHistoryEnvelope(1, [t0, t0.AddMinutes(1.0)], [1.0, 3.0], [5.0, 9.0], [2.0, 6.0]));

		state.AppendRealtime(t0.AddMinutes(2.0), 42.0);

		state.Columns.Should().HaveCount(3);
		state.Columns[2].Center.Should().Be(42.0);
		state.CurrentValue.Should().Be(42.0);
	}

	[AvaloniaFact]
	public void Realtime_AfterLoadHistoryMovesTheSeamBack_AppendsAgainstTheReloadedSeries()
	{
		// A history re-query replaces the series, so the guard follows the reloaded last point rather than
		// the newest sample ever appended.
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
		state.AppendRealtime(t0.AddMinutes(10.0), 7.0);

		state.LoadHistory(new PenHistoryEnvelope(1, [t0, t0.AddMinutes(1.0)], [1.0, 3.0], [5.0, 9.0], [2.0, 6.0]));
		state.AppendRealtime(t0.AddMinutes(1.5), 42.0);

		state.Columns.Should().HaveCount(3);
		state.Columns[2].Center.Should().Be(42.0);
		state.CurrentValue.Should().Be(42.0);
	}

	[AvaloniaFact]
	public void Realtime_AfterClearHistory_AppendsAnyTimestampAgain()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
		state.AppendRealtime(t0.AddMinutes(10.0), 7.0);

		state.ClearHistory();
		state.AppendRealtime(t0, 42.0);

		state.Columns.Should().ContainSingle();
		state.Columns[0].Center.Should().Be(42.0);
		state.CurrentValue.Should().Be(42.0);
	}

	[AvaloniaFact]
	public void Realtime_PastTheBufferCap_DropsTheOldestColumns()
	{
		const int Cap = 100_000;
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

		for (var index = 0; index < Cap + 10; index++)
		{
			state.AppendRealtime(t0.AddSeconds(index), index);
		}

		state.Columns.Should().HaveCount(Cap);
		state.Columns[0].Center.Should().Be(10.0);
	}

	[AvaloniaFact]
	public void FoldRealtime_WidensTheMinMaxOfTheCurrentColumn()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
		state.LoadHistory(new PenHistoryEnvelope(1, [t0], [1.0], [5.0], [2.0]));

		state.FoldRealtime(9.0);

		state.Columns.Should().ContainSingle();
		state.Columns[0].Max.Should().Be(9.0);
		state.Columns[0].Min.Should().Be(1.0);
		state.Columns[0].Center.Should().Be(9.0);
	}

	[AvaloniaFact]
	public async Task StickyLiveEdgeAdvance_DoesNotReQueryHistory()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from, _to);
		viewModel.Navigation.IsSticky.Should().BeTrue();
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.Navigation.OnLiveEdge(viewModel.Navigation.To.AddMinutes(1.0));

		provider.HistoryQueryCount.Should().Be(queriesBefore);
	}

	[AvaloniaFact]
	public async Task StickyLiveEdgeAdvance_StillShiftsTheScaleWindow()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from, _to);
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
		var columnsBefore = state.Columns.Count;

		coordinator.Start();
		scheduler.AdvanceBy(_batchWindow.Ticks);

		state.Columns.Count.Should().Be(columnsBefore);
	}

	[AvaloniaFact]
	public void Coordinator_RawLayerRealtime_AppendsColumns()
	{
		var (viewModel, scheduler, coordinator, _) = CreateViewModel(
			realtimeInterval: TimeSpan.FromMilliseconds(10));
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);
		var columnsBefore = state.Columns.Count;

		coordinator.Start();
		scheduler.AdvanceBy(_batchWindow.Ticks);

		state.Columns.Count.Should().BeGreaterThan(columnsBefore);
	}

	// The archive is per-variable and change-based, so a buffer window routinely spans timestamps only one
	// pen sampled; a pen absent from a timestamp is left alone, not appended as null, which would draw a
	// break the archive never recorded.
	[AvaloniaFact]
	public void Coordinator_RealtimeTimestampsOnlyOnePenSampled_BreakNeitherPen()
	{
		var (viewModel, scheduler, coordinator, provider) = CreateViewModel(
			realtimeInterval: TimeSpan.FromMilliseconds(10));
		provider.StaggerRealtimeTimestamps = true;
		var first = viewModel.AddPen(provider.Pens[0]);
		var second = viewModel.AddPen(provider.Pens[1]);
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);

		coordinator.Start();
		scheduler.AdvanceBy(_batchWindow.Ticks);

		first.Columns.Should().NotBeEmpty();
		second.Columns.Should().NotBeEmpty();
		first.Columns.Should().NotContain(column => double.IsNaN(column.Center));
		second.Columns.Should().NotContain(column => double.IsNaN(column.Center));
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
			pen.Line.Axes.XAxis.Should().BeSameAs(bottom);
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

		// A first sample well before the window keeps the prefetch margin off its left clamp.
		viewModel.Navigation.TrackDataExtents(_from.AddDays(-30.0), _to);
		viewModel.Navigation.ZoomAt(2.0, viewModel.Navigation.To);
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(20).Ticks);
		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		var lastFrom = viewModel.Navigation.From;
		var lastTo = viewModel.Navigation.To;
		var lastLayer = viewModel.Navigation.ActiveLayer;
		var lastWidth = lastTo - lastFrom;

		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.LastQueriedFromUtc.Should().Be(lastFrom - lastWidth);
		provider.LastQueriedToUtc.Should().Be(lastTo + lastWidth);
		provider.LastQueriedLayer.Should().Be(lastLayer);
	}

	[AvaloniaFact]
	public async Task APanInsideThePrefetchedBandIssuesNoHistoryQuery()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		provider.HistoryQueryCount.Should().Be(1);

		// Half a window width: the margin the first query fetched still holds every column.
		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-30.0));
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(1);
	}

	[AvaloniaFact]
	public async Task AReportedWidthInsideTheDeadbandKeepsThePrefetchedBand()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		viewModel.ReportDataAreaWidth(700.0);
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		provider.HistoryQueryCount.Should().Be(1);

		viewModel.ReportDataAreaWidth(704.0);
		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-30.0));
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		viewModel.Navigation.TargetColumnCount.Should().Be(512);
		provider.HistoryQueryCount.Should().Be(1);
	}

	[AvaloniaFact]
	public async Task APanPastTheBandLeavesTheAxisUntouchedUntilTheQueryLands()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		var revisionBefore = viewModel.ScalesRevision;

		viewModel.Navigation.PanBy(-4 * width);

		viewModel.ScalesRevision.Should().Be(revisionBefore);

		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(2);
		viewModel.ScalesRevision.Should().BeGreaterThan(revisionBefore);
	}

	// A query that never landed filled no band, so the pan that follows has to ask again. The gate opening
	// on the request instead of on the result would leave the chart empty until a zoom or a full-window pan.
	[AvaloniaFact]
	public async Task APanInsideTheBandAfterAFailedQueryAsksAgain()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		provider.FailHistory = true;
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		provider.HistoryQueryCount.Should().Be(1);

		// The same half-window pan APanInsideThePrefetchedBandIssuesNoHistoryQuery answers with no query.
		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-30.0));
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(2);
	}

	// The drag that leaves the fetched band and comes back into it before the far query lands. The gate reads
	// the band as fetched and asks nothing, so the far result arriving is the only thing that can notice the
	// envelopes it replaced no longer draw the window in view.
	[AvaloniaFact]
	public async Task AResultForAWindowTheDragLeftReQueriesTheWindowInView()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		var windowFrom = viewModel.Navigation.From;

		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(2);

		provider.GatedLayer = null;
		viewModel.Navigation.PanBy(4 * width);
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		viewModel.Navigation.From.Should().Be(windowFrom);
		provider.HistoryQueryCount.Should().Be(2);

		await ReleaseAndAwaitResults(viewModel, 1, () => provider.HistoryGate.SetResult(StaleEnvelopes()));
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		await AwaitQueryCount(provider, 3);
		provider.LastQueriedFromUtc.Should().Be(windowFrom - width);
		provider.LastQueriedToUtc.Should().Be(viewModel.Navigation.To + width);
	}

	// The same hand-over, read from the axis: the arrived range holds no column the window in view shows, so
	// sizing the axis from it would be the mid-gesture jump the window bound exists to remove. The re-query
	// AResultForAWindowTheDragLeftReQueriesTheWindowInView pins is what brings the axis back.
	[AvaloniaFact]
	public async Task AResultForAWindowTheDragLeftLeavesTheAxisUntouched()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;

		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.GatedLayer = null;
		viewModel.Navigation.PanBy(4 * width);
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);
		var revisionBefore = viewModel.ScalesRevision;

		await ReleaseAndAwaitResults(viewModel, 1, () => provider.HistoryGate.SetResult(StaleEnvelopes()));

		viewModel.ScalesRevision.Should().Be(revisionBefore);
	}

	// Skipping the axis here freezes it with no path back, since the gate that would ask again opens on a
	// result. APanInsideTheBandAfterAFailedQueryAsksAgain pins the ask.
	[AvaloniaFact]
	public async Task AFailedQueryOutsideTheBandStillAppliesTheAxis()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		var revisionBefore = viewModel.ScalesRevision;
		provider.FailHistory = true;

		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(2);
		viewModel.ScalesRevision.Should().BeGreaterThan(revisionBefore);
	}

	[AvaloniaFact]
	public async Task APanPastTheBandIssuesOneQueryForTheNewRange()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		provider.HistoryQueryCount.Should().Be(1);

		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-60.0));
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(2);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		provider.LastQueriedFromUtc.Should().Be(viewModel.Navigation.From - width);
		provider.LastQueriedToUtc.Should().Be(viewModel.Navigation.To + width);
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
	public async Task PreRenderDataArea_QueriesTheMaximumColumnCountWithItsMargin()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		viewModel.Navigation.TargetColumnCount.Should().Be(HistoryColumnTarget.MaxColumns);
		await LoadInitialHistory(viewModel, scheduler, _from, _to);

		provider.LastQueriedTargetColumnCount.Should()
			.Be(HistoryPrefetch.MarginColumnFactor * HistoryColumnTarget.MaxColumns);
	}

	[AvaloniaFact]
	public async Task ReportedWidth_SetsTheQueryResolutionUnquantized()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		viewModel.ReportDataAreaWidth(700.0);
		viewModel.Navigation.TargetColumnCount.Should().Be(512);

		await LoadInitialHistory(viewModel, scheduler, _from, _to);

		provider.LastQueriedTargetColumnCount.Should().Be(HistoryPrefetch.MarginColumnFactor * 700);
	}

	[AvaloniaFact]
	public async Task CollapsedCanvas_KeepsTheLastReportedWidth()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		viewModel.ReportDataAreaWidth(700.0);

		viewModel.ReportDataAreaWidth(0.0);

		viewModel.Navigation.TargetColumnCount.Should().Be(512);
		await LoadInitialHistory(viewModel, scheduler, _from, _to);
		provider.LastQueriedTargetColumnCount.Should().Be(HistoryPrefetch.MarginColumnFactor * 700);
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
		provider.LastQueriedTargetColumnCount.Should().Be(HistoryPrefetch.MarginColumnFactor * 256);
	}

	[AvaloniaFact]
	public async Task WidthReportedWhileTheInitialQueryIsInFlight_AppliesTheLaterWindowLast()
	{
		// Startup race: the render seam reports a width while the initial query is in flight. One query runs
		// at a time, so the reported window waits behind the held one and reaches the archive only when it
		// lands; it is then the last window applied, at the reported resolution.
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		provider.GatedLayer = AggregationLayer.Raw;

		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);
		provider.HistoryQueryCount.Should().Be(1);

		viewModel.ReportDataAreaWidth(256.0);
		provider.GatedLayer = null;
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(1);

		await ReleaseAndAwaitResults(viewModel, 2, () => provider.HistoryGate.SetResult(StaleEnvelopes()));

		provider.HistoryQueryCount.Should().Be(2);
		provider.LastQueriedTargetColumnCount.Should().Be(HistoryPrefetch.MarginColumnFactor * 256);
		state.Columns.Should().HaveCount(2);
		state.CurrentValue.Should().Be(FakeDataProvider.DefaultCenter);
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
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from, _to);
		viewModel.SetDeltaModeEnabled(true);

		// The fake answers one column per edge of the prefetched range, a window width past the visible end.
		var historyEnd = _to.AddHours(1.0);

		viewModel.PlaceDeltaCursor(_from);
		viewModel.PlaceDeltaCursor(historyEnd);

		viewModel.DeltaFirstCursor.Should().Be(_from);
		viewModel.DeltaSecondCursor.Should().Be(historyEnd);
		viewModel.DeltaReadout.Should().NotBeNull();
		viewModel.DeltaReadout!.DeltaTime.Should().Be(historyEnd - _from);
		viewModel.DeltaReadout.DeltaY.Should().Be(1.0);
		viewModel.DeltaReadoutText.Should().Contain("Δt").And.Contain("Δy");
	}

	[AvaloniaFact]
	public async Task DeltaMode_RoutesLeftButtonToDeltaPlacementInsteadOfPan()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from, _to);
		viewModel.SetDeltaModeEnabled(true);

		viewModel.ActiveLeftButtonTool.Should().Be(LeftButtonTool.DeltaPlacement);
	}

	[AvaloniaFact]
	public async Task ExitingDeltaMode_ReturnsToPanAndClearsCursors()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from, _to);
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
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-1.0), _to);
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
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from, _to);

		viewModel.BeginDrag();
		viewModel.MoveCursor(_from.AddMinutes(30.0));

		viewModel.IsDragging.Should().BeTrue();
		viewModel.CursorTime.Should().BeNull();
		viewModel.CursorValues.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task HoverAfterDragEnds_PublishesTheTraceCursorAgain()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from, _to);

		viewModel.BeginDrag();
		viewModel.EndDrag();
		viewModel.MoveCursor(_from.AddMinutes(30.0));

		viewModel.IsDragging.Should().BeFalse();
		viewModel.CursorTime.Should().Be(_from.AddMinutes(30.0));
	}

	[AvaloniaFact]
	public async Task RequestInitialHistory_LoadsEnvelopes_ThenALaterGestureResultSupersedesIt()
	{
		// One history path: the initial load applies first; a later debounced gesture re-query supersedes it.
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		provider.LayerCenterOverrides[AggregationLayer.Minute] = 5.0;

		await LoadInitialHistory(viewModel, scheduler, _from, _to);
		viewModel.FindPen(1)!.CurrentValue.Should().Be(2.0);

		// A zoom-out gesture re-queries the coarser layer through the debouncer.
		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Minute);
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		viewModel.FindPen(1)!.CurrentValue.Should().Be(5.0);
	}

	[AvaloniaFact]
	public async Task StaleInitialHistory_DoesNotOverwriteANewerDebouncedGestureWindow()
	{
		// The initial query is held in flight while a zoom asks for a coarser window. One query runs at a
		// time, so the held read lands first and the gesture's window runs behind it: the value the stale
		// result carried is overwritten, never the other way round.
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));

		provider.GatedLayer = AggregationLayer.Raw;
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		viewModel.Navigation.ActiveLayer.Should().NotBe(AggregationLayer.Raw);
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(1);

		await ReleaseAndAwaitResults(viewModel, 2, () => provider.HistoryGate.SetResult(StaleEnvelopes()));

		provider.HistoryQueryCount.Should().Be(2);
		viewModel.FindPen(1)!.CurrentValue.Should().Be(2.0);
	}

	[AvaloniaFact]
	public void RequestInitialHistory_ResultArrivingAfterDispose_DoesNotApplyOrThrow()
	{
		// Disposal-safety: the initial query is held in flight, the view model is disposed (disposing the
		// Plot), then the gate is released. Disposal ends the history subscription, so nothing mutates the
		// disposed Plot.
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		provider.GatedLayer = AggregationLayer.Raw;
		var pen = viewModel.FindPen(1)!;

		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		viewModel.Dispose();

		var release = () => provider.HistoryGate.SetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
		[
			new PenHistoryEnvelope(1, [_from, _to], [99.0, 99.0], [99.0, 99.0], [99.0, 99.0])
		]));

		release.Should().NotThrow();
		pen.CurrentValue.Should().BeNull();
	}

	[AvaloniaFact]
	public async Task History_OmittingARequestedPen_ClearsThatPensCurve()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		viewModel.AddPen(new Pen(2, "Pen 2", "Group A", "#00ff00"));

		await LoadInitialHistory(viewModel, scheduler, _from, _to);
		viewModel.FindPen(2)!.Columns.Should().HaveCount(2);

		// The next window holds no row for pen 2, so the provider answers with no envelope for it at all.
		provider.OmittedPenIds.Add(2);
		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		viewModel.FindPen(2)!.Columns.Should().BeEmpty();
		viewModel.FindPen(2)!.CurrentValue.Should().BeNull();
		viewModel.FindPen(1)!.Columns.Should().HaveCount(2);
	}

	[AvaloniaFact]
	public async Task History_PenAddedWhileTheQueryWasInFlight_KeepsItsCurve()
	{
		// What the request asked for, not the current pen dictionary, decides what is cleared: pen 2 is
		// added after the request was issued, so a result omitting it says nothing about it. The gate's
		// continuation runs on the thread pool, so the apply is awaited rather than assumed inline.
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var watch = viewModel.HistoryApplied.Subscribe(_ => applied.TrySetResult());

		provider.GatedLayer = AggregationLayer.Raw;
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		var lateState = viewModel.AddPen(new Pen(2, "Pen 2", "Group A", "#00ff00"));
		lateState.LoadHistory(new PenHistoryEnvelope(2, [_from, _to], [4.0, 4.0], [4.0, 4.0], [4.0, 4.0]));

		provider.HistoryGate.SetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
		[
			new PenHistoryEnvelope(1, [_from, _to], [1.0, 2.0], [1.0, 2.0], [1.0, 2.0])
		]));
		await applied.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		viewModel.FindPen(1)!.CurrentValue.Should().Be(2.0);
		lateState.Columns.Should().HaveCount(2);
		lateState.CurrentValue.Should().Be(4.0);
	}

	// The value a held query carries, distinct from every value the fake answers with, so the assertion says
	// which of the two results the chart ended up holding.
	private static Result<IReadOnlyList<PenHistoryEnvelope>> StaleEnvelopes()
	{
		return Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
		[
			new PenHistoryEnvelope(1, [_from, _to], [99.0, 99.0], [99.0, 99.0], [99.0, 99.0])
		]);
	}

	// A query the pipeline issues behind a released one starts once Rx has unwound the released query on the
	// thread it resumed the task on, so a count read straight after the release can run ahead of it.
	private static async Task AwaitQueryCount(FakeDataProvider provider, int count)
	{
		var deadline = DateTime.UtcNow + _testDeadline;

		while (provider.HistoryQueryCount < count && DateTime.UtcNow < deadline)
		{
			await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
		}

		provider.HistoryQueryCount.Should().Be(count);
	}

	// Releasing a held query resumes the pipeline on the thread Rx resumes the task on, so every result the
	// release sets going lands after the call returns.
	private static async Task ReleaseAndAwaitResults(
		TrendChartViewModel viewModel,
		int results,
		Action release)
	{
		var applied = viewModel.HistoryApplied.Take(results).ToTask(TestContext.Current.CancellationToken);

		release();

		await applied.WaitAsync(_testDeadline, TestContext.Current.CancellationToken);
	}

	// Drives the production initial-load path: snaps the navigation window through the real first-data
	// extents path, requests initial history and advances the test scheduler past the throttle window.
	// firstSample is the pan-backward floor; pass a value before the window start when a test pans into the past.
	private static Task LoadInitialHistory(
		TrendChartViewModel viewModel,
		TestScheduler scheduler,
		DateTime firstSample,
		DateTime to)
	{
		viewModel.Navigation.TrackDataExtents(firstSample, to);
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(_historyDebounceWindow.Ticks + 1);

		return Task.CompletedTask;
	}

	private static (TrendChartViewModel ViewModel, TestScheduler Scheduler, TrendCoordinator Coordinator,
		FakeDataProvider Provider)
		CreateViewModel(TimeSpan? realtimeInterval = null)
	{
		// Realtime stays quiet unless a test asks for it: advancing the scheduler past the history throttle
		// must not also pump realtime samples into a pen whose history the test asserts on.
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, realtimeInterval ?? TimeSpan.FromHours(1));
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
