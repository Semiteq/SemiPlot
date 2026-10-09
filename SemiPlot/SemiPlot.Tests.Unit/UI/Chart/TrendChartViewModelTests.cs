using System.Collections;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.Tests.Unit.UI.Messages;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.ChartTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendChartViewModelTests
{
	private static readonly TimeSpan _testDeadline = TimeSpan.FromSeconds(10.0);
	private static readonly DateTime _from = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _to = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);
	private static readonly TimeSpan _realtimeInterval = TimeSpan.FromMilliseconds(10.0);

	[AvaloniaFact]
	public void SetPenVisibility_TogglesPenAndPlottableState()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(7, "Heater", ["Group A"], "#ff0000"));

		viewModel.SetPenVisibility(7, false);

		state.IsVisible.Should().BeFalse();
		state.Line.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task History_LoadsCenterValueForKnownPen()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));

		await LoadInitialHistory(viewModel, scheduler, _from, _to);

		viewModel.FindPen(1)!.CurrentValue.Should().Be(2.0);
		viewModel.FindPen(1)!.Line.Columns.Should().HaveCount(2);
	}

	[AvaloniaFact]
	public async Task Line_ReadsTheSameBufferLoadAndAppendMutate()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));

		await LoadInitialHistory(viewModel, scheduler, _from, _to);

		// Reading through the plottable rather than the backing field proves it renders the buffer the
		// state mutates: the limits are taken from the very list LoadHistory filled.
		state.Line.GetAxisLimits().Top.Should().Be(2.0);

		// Past the end of the prefetched range, so the point is appended rather than folded into it.
		state.AppendRealtime(_to.AddHours(2.0), 7.0);

		state.Line.GetAxisLimits().Top.Should().Be(7.0);
	}

	[AvaloniaFact]
	public void DisposingTheChart_LeavesTheConnectionFaultsForwarding()
	{
		var (viewModel, _, coordinator, provider) = CreateViewModel();
		var states = new List<ArchiveConnectionState>();
		using var subscription = coordinator.ConnectionFaults.Subscribe(states.Add);

		viewModel.Dispose();
		provider.ReportConnectionState(ArchiveConnectionState.Connected);

		states.Should().Equal(ArchiveConnectionState.Connected);
		coordinator.Dispose();
	}

	[AvaloniaFact]
	public void RealtimeBatch_UpdatesPerPenCurrentValue()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel(realtimeInterval: TimeSpan.FromMilliseconds(10));
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));

		scheduler.AdvanceBy(BatchWindow.Ticks);

		var pen = viewModel.FindPen(1)!;
		pen.CurrentValue.Should().NotBeNull();
		pen.Line.Columns.Should().NotBeEmpty();
	}

	[AvaloniaFact]
	public void SetActivePen_UpdatesActivePenId()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		viewModel.AddPen(new Pen(2, "Pen 2", ["Group B"], "#00ff00"));

		viewModel.SetActivePen(2).Should().BeTrue();

		viewModel.ActivePenId.Should().Be(2);
	}

	[AvaloniaFact]
	public void SetActivePen_WithASwitchedOffPen_KeepsTheVisiblePenActive()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000", EnabledOnStart: false));
		viewModel.AddPen(new Pen(2, "Pen 2", ["Group B"], "#00ff00"));

		viewModel.SetActivePen(1).Should().BeFalse();

		viewModel.ActivePenId.Should().Be(2);
		viewModel.ActivePenAxis!.IsVisible.Should().BeTrue();
	}

	[AvaloniaFact]
	public void AddPen_FirstPenBecomesActive()
	{
		var (viewModel, _, _, _) = CreateViewModel();

		viewModel.AddPen(new Pen(5, "Pen 5", ["Group A"], "#ff0000"));

		viewModel.ActivePenId.Should().Be(5);
	}

	[AvaloniaFact]
	public void SetAxisLimits_SwitchesPenToManualWithFixedRange()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));

		viewModel.SetAxisLimits(1, 10.0, 90.0).Should().BeTrue();

		var settings = viewModel.ScaleSettings[1];
		settings.Mode.Should().Be(ScaleMode.Manual);
		settings.ManualMin.Should().Be(10.0);
		settings.ManualMax.Should().Be(90.0);
	}

	[AvaloniaFact]
	public void SetLogarithmic_OverANonPositiveManualMinimum_IsRefusedAndWritesNothing()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(provider.Pens);
		viewModel.SetAxisLimits(1, -5.0, 100.0);
		var revision = viewModel.ScalesRevision;

		viewModel.SetLogarithmic(1, true).Should().BeFalse();

		viewModel.ScaleSettings[1].IsLogarithmic.Should().BeFalse();
		viewModel.ScalesRevision.Should().Be(revision);
		viewModel.SetLogarithmic(1, false).Should().BeTrue("switching the flag off is never refused");
		viewModel.SetLogarithmic(99, true).Should().BeFalse();
	}

	[AvaloniaFact]
	public void InitialScale_RestoresTheStoredPair()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		var catalogue = provider.Pens;
		viewModel.ApplyCatalogue(catalogue);
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		viewModel.ApplyCatalogue([catalogue[0] with { ScaleMinOnStart = 0.0, ScaleMaxOnStart = 50.0 }, catalogue[1]]);

		viewModel.RestoreInitialScale();

		viewModel.ScaleSettings[1].Should().Be(new PenScaleSettings(1)
		{
			Mode = ScaleMode.Manual,
			ManualMin = 0.0,
			ManualMax = 50.0
		});
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((0.0, 50.0));
	}

	[AvaloniaFact]
	public void InitialScaleWithNoStoredPair_IsAuto()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		var catalogue = provider.Pens;
		viewModel.ApplyCatalogue([catalogue[0] with { ScaleMinOnStart = 0.0, ScaleMaxOnStart = 50.0 }, catalogue[1]]);
		viewModel.ApplyCatalogue([catalogue[0] with { ScaleMinOnStart = null, ScaleMaxOnStart = null }, catalogue[1]]);

		viewModel.RestoreInitialScale();

		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Auto);
	}

	[AvaloniaFact]
	public void TheScaleCommandsWithNoPens_ChangeNothing()
	{
		var (viewModel, _, _, _) = CreateViewModel();

		viewModel.RestoreInitialScale();
		viewModel.AutoscaleActivePen();

		viewModel.ScaleSettings.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void InitialScale_ActsOnTheActivePenOnly()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(
			[.. provider.Pens.Select(pen => pen with { ScaleMinOnStart = 5.0, ScaleMaxOnStart = 50.0 })]);
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		viewModel.SetAxisLimits(2, 20.0, 80.0);
		viewModel.SetActivePen(2);

		viewModel.RestoreInitialScale();

		viewModel.ScaleRangeForPen(2)!.Value.Should().Be((5.0, 50.0));
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
	}

	[AvaloniaFact]
	public void Autoscale_ActsOnTheActivePenOnly()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(provider.Pens);
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		viewModel.SetAxisLimits(2, 20.0, 80.0);
		viewModel.SetActivePen(2);

		viewModel.AutoscaleActivePen();

		viewModel.ScaleSettings[2].Mode.Should().Be(ScaleMode.Auto);
		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Manual);
	}

	[AvaloniaFact]
	public void TheScaleCommands_LeaveAHiddenActivePenAlone()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(
			[.. provider.Pens.Select(pen => pen with { ScaleMinOnStart = 5.0, ScaleMaxOnStart = 50.0 })]);
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		viewModel.SetPenVisibility(2, false);
		viewModel.SetPenVisibility(1, false);
		viewModel.ActivePenId.Should().Be(1);

		viewModel.RestoreInitialScale();
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));

		viewModel.AutoscaleActivePen();
		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Manual);
	}

	[AvaloniaFact]
	public void ActivePenAxis_ResolvesToTheInstanceTheActivePenRendersAgainst()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(provider.Pens);
		viewModel.SetActivePen(2);

		viewModel.ActivePenAxis.Should().BeSameAs(viewModel.FindPen(2)!.Line.Axes.YAxis);
		viewModel.ActivePenAxis.Should().NotBeSameAs(viewModel.FindPen(1)!.Line.Axes.YAxis);
	}

	[AvaloniaFact]
	public void DrawnPenId_FollowsTheActivePenAndItsVisibility()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		var changes = new List<int?>();
		using var subscription = viewModel.WhenAnyValue(chart => chart.DrawnPenId).Subscribe(changes.Add);
		viewModel.DrawnPenId.Should().BeNull("no pen is shown yet");

		viewModel.ApplyCatalogue(provider.Pens);
		viewModel.SetActivePen(2);
		viewModel.SetPenVisibility(2, false);
		viewModel.SetPenVisibility(1, false);
		viewModel.SetPenVisibility(1, true);

		changes.Should().Equal(null, 1, 2, 1, null, 1);
	}

	[AvaloniaFact]
	public void AutoscalePen_ActsOnTheNamedPenWhateverIsActive()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(provider.Pens);
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		viewModel.SetAxisLimits(2, 20.0, 80.0);

		viewModel.AutoscalePen(2).Should().BeTrue();

		viewModel.ScaleSettings[2].Mode.Should().Be(ScaleMode.Auto);
		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Manual);
	}

	[AvaloniaFact]
	public void RestoreInitialScale_ForANamedPen_ActsOnThatPenAndRefusesAHiddenOne()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(
			[.. provider.Pens.Select(pen => pen with { ScaleMinOnStart = 5.0, ScaleMaxOnStart = 50.0 })]);
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		viewModel.SetAxisLimits(2, 20.0, 80.0);

		viewModel.RestoreInitialScale(2).Should().BeTrue();
		viewModel.ScaleRangeForPen(2)!.Value.Should().Be((5.0, 50.0));
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));

		viewModel.SetPenVisibility(2, false);

		viewModel.RestoreInitialScale(2).Should().BeFalse();
		viewModel.AutoscalePen(2).Should().BeFalse();
		viewModel.AutoscalePen(99).Should().BeFalse();
	}

	[AvaloniaFact]
	public void InitialScale_RequestsARedraw()
	{
		var scheduler = new TestScheduler();
		using var viewModel = CreateChart(scheduler);
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000", ScaleMinOnStart: 5.0, ScaleMaxOnStart: 50.0));
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		scheduler.AdvanceBy(BatchWindow.Ticks * 2);
		var redraws = 0;
		using var subscription = viewModel.RedrawRequested.Subscribe(_ => redraws++);

		viewModel.RestoreInitialScale();

		redraws.Should().Be(1);
	}

	[AvaloniaFact]
	public void AnIdleChartRaisesNoRedraw_AndARequestRaisesOneAtOnce()
	{
		var uiScheduler = new QueueReadingScheduler();
		using var viewModel = CreateChartOn(uiScheduler);
		var redraws = 0;
		using var subscription = viewModel.RedrawRequested.Subscribe(_ => redraws++);

		redraws.Should().Be(0, "a chart with nothing to redraw raises nothing");

		viewModel.SetDeltaModeEnabled(true);

		redraws.Should().Be(1, "the request is raised inside the call that made it");
		uiScheduler.HasQueuedWork.Should().BeFalse("the redraw leaves nothing on the UI scheduler");
	}

	[AvaloniaFact]
	public void DisposingTheChartCompletesTheRedrawSignal()
	{
		var viewModel = CreateChartOn(new QueueReadingScheduler());
		var redraws = 0;
		var isCompleted = false;
		using var subscription = viewModel.RedrawRequested.Subscribe(_ => redraws++, () => isCompleted = true);

		viewModel.Dispose();

		isCompleted.Should().BeTrue("the view clears a pending frame when the signal completes");
		redraws.Should().Be(0);
	}

	[AvaloniaFact]
	public void ABurstOfRequestsRaisesOneSignalPerRequest()
	{
		using var viewModel = CreateChartOn(new QueueReadingScheduler());
		var redraws = 0;
		using var subscription = viewModel.RedrawRequested.Subscribe(_ => redraws++);

		for (var request = 0; request <= 10; request++)
		{
			viewModel.SetDeltaModeEnabled(request % 2 == 0);
		}

		redraws.Should().Be(11, "the view, not the view model, joins the requests of one frame");
	}

	[AvaloniaFact]
	public void ManualLimits_DriveTheOwningAxisRange()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));

		viewModel.SetAxisLimits(1, 10.0, 90.0);

		var axis = state.Line.Axes.YAxis;
		axis.Min.Should().Be(10.0);
		axis.Max.Should().Be(90.0);
	}

	[AvaloniaFact]
	public void SameGroupPens_GetSeparateYAxes()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var first = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var second = viewModel.AddPen(new Pen(2, "Pen 2", ["Group A"], "#00ff00"));

		first.Line.Axes.YAxis.Should().NotBeSameAs(second.Line.Axes.YAxis);
		viewModel.AxisCount.Should().Be(2);
	}

	[AvaloniaFact]
	public void StoredScalePair_OpensEachPenOnItsOwnBoundsWithNoPadding()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000", ScaleMinOnStart: 0.0, ScaleMaxOnStart: 50.0));
		viewModel.AddPen(new Pen(2, "Pen 2", ["Group A"], "#00ff00", ScaleMinOnStart: -10.0, ScaleMaxOnStart: 10.0));

		viewModel.AxisCount.Should().Be(2);
		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Manual);
		viewModel.ScaleSettings[2].Mode.Should().Be(ScaleMode.Manual);
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((0.0, 50.0));
		viewModel.ScaleRangeForPen(2)!.Value.Should().Be((-10.0, 10.0));
	}

	[AvaloniaFact]
	public async Task PenWithNoStoredScalePair_AutoscalesOverTheVisibleWindow()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));

		await LoadInitialHistory(viewModel, scheduler, _from, _to);

		// The window holds the envelope's opening column alone, a flat 1.0, which the model pads by half.
		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Auto);
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((0.5, 1.5));
	}

	[AvaloniaFact]
	public void AddPen_DisabledOnStart_SeedsThePenHidden()
	{
		var (viewModel, _, _, _) = CreateViewModel();

		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000", EnabledOnStart: false));

		state.IsVisible.Should().BeFalse();
		state.Line.IsVisible.Should().BeFalse();
	}

	// Only the active pen's axis is drawn and only a visible pen's axis may be, so a catalogue whose
	// first pen is switched off would otherwise open with no Y axis at all.
	[AvaloniaFact]
	public void AddPen_WithTheFirstPenDisabledOnStart_ActivatesTheFirstVisiblePen()
	{
		var (viewModel, _, _, _) = CreateViewModel();

		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000", EnabledOnStart: false));
		viewModel.AddPen(new Pen(2, "Pen 2", ["Group A"], "#00ff00"));

		viewModel.ActivePenId.Should().Be(2);
		viewModel.ActivePenAxis!.IsVisible.Should().BeTrue();
	}

	[AvaloniaFact]
	public void AddPen_WithEveryPenDisabledOnStart_KeepsTheFirstPenActive()
	{
		var (viewModel, _, _, _) = CreateViewModel();

		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000", EnabledOnStart: false));
		viewModel.AddPen(new Pen(2, "Pen 2", ["Group A"], "#00ff00", EnabledOnStart: false));

		viewModel.ActivePenId.Should().Be(1);
	}

	[AvaloniaFact]
	public void SetPenVisibility_SwitchingTheActivePenOff_MovesTheActivePenToAVisibleOne()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		viewModel.AddPen(new Pen(2, "Pen 2", ["Group A"], "#00ff00"));

		viewModel.SetPenVisibility(1, false);

		viewModel.ActivePenId.Should().Be(2);
		viewModel.ActivePenAxis!.IsVisible.Should().BeTrue();
	}

	// The readout is measured for the active pen alone, and the active pen moves on its own when the
	// operator switches the current one off.
	[AvaloniaFact]
	public async Task SwitchingTheActivePen_RemeasuresTheDeltaReadout()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		provider.OmittedPenIds.Add(2);
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		viewModel.AddPen(new Pen(2, "Pen 2", ["Group A"], "#00ff00"));

		await LoadInitialHistory(viewModel, scheduler, _from, _to);

		viewModel.SetDeltaModeEnabled(true);
		viewModel.PlaceDeltaCursor(_from);
		viewModel.PlaceDeltaCursor(_to);
		viewModel.DeltaReadout!.DeltaY.Should().NotBeNull();

		viewModel.SetActivePen(2).Should().BeTrue();

		// Pen 2 carries no envelope, so its delta over the same two cursors reads as no value.
		viewModel.DeltaReadout!.DeltaY.Should().BeNull();
	}

	[AvaloniaFact]
	public void SetPenVisibility_SwitchingTheLastVisiblePenOff_LeavesTheActivePenWhereItIs()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));

		viewModel.SetPenVisibility(1, false);

		viewModel.ActivePenId.Should().Be(1);
		viewModel.ActivePenAxis!.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ZoomOut_DrivesACoarserLayerReQuery()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));

		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Minute);
		provider.LastQueriedLayer.Should().Be(AggregationLayer.Minute);
	}

	[AvaloniaFact]
	public async Task FoldRealtime_WidensCurrentColumnInsteadOfAddingAPoint()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from, _to);
		var columnsBefore = state.Line.Columns.Count;

		state.FoldRealtime(_to, 99.0);

		state.Line.Columns.Should().HaveCount(columnsBefore);
		state.CurrentValue.Should().Be(99.0);
	}

	[AvaloniaFact]
	public void SteppedPen_ReachesThePlottableAsStepped()
	{
		var (viewModel, _, _, _) = CreateViewModel();

		var state = viewModel.AddPen(new Pen(7, "Damper", ["Dampers"], "#ff0000", LineStyle: PenLineStyle.Stepped));

		var (onDiagonal, onHeldStep) = RenderedRise.Draw(viewModel, state, RenderedRise.Red);

		onDiagonal.Should().BeFalse();
		onHeldStep.Should().BeTrue();
	}

	[AvaloniaFact]
	public void InterpolatedPen_ReachesThePlottableAsInterpolated()
	{
		var (viewModel, _, _, _) = CreateViewModel();

		var state = viewModel.AddPen(new Pen(7, "Heater", ["Heaters"], "#ff0000"));

		var (onDiagonal, onHeldStep) = RenderedRise.Draw(viewModel, state, RenderedRise.Red);

		onDiagonal.Should().BeTrue();
		onHeldStep.Should().BeFalse();
	}

	[AvaloniaFact]
	public void History_WithInteriorGap_PlacesNaNBetweenTwoSegments()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
		var envelope = new PenHistoryEnvelope(
			1,
			[t0, t0.AddMinutes(1.0), t0.AddMinutes(2.0)],
			[1.0, double.NaN, 3.0],
			[1.0, double.NaN, 3.0],
			[1.0, double.NaN, 3.0]);

		state.LoadHistory(envelope, t0.AddMinutes(2.0));

		state.Line.Columns.Should().HaveCount(3);
		double.IsNaN(state.Line.Columns[0].Center).Should().BeFalse();
		double.IsNaN(state.Line.Columns[1].Center).Should().BeTrue();
		double.IsNaN(state.Line.Columns[2].Center).Should().BeFalse();
	}

	[AvaloniaFact]
	public void Realtime_NullSample_AppendsNaNGap()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var timestamp = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

		state.AppendRealtime(timestamp, value: null);

		state.Line.Columns.Should().ContainSingle();
		double.IsNaN(state.Line.Columns[0].Center).Should().BeTrue();
	}

	[AvaloniaFact]
	public void RequestInitialHistory_FiresAHistoryQueryWithoutAnyUserGesture()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		provider.HistoryQueryCount.Should().Be(0);

		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().BeGreaterThan(0);
		provider.LastQueriedPenIds.Should().Contain(1);
	}

	[AvaloniaFact]
	public void RequestInitialHistory_FiresExactlyOneHistoryQuery_NoDoubleLoad()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		provider.HistoryQueryCount.Should().Be(0);

		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		// Seed query loads once; the first-data snap must not trigger a second re-query.
		provider.HistoryQueryCount.Should().Be(1);
	}

	// The archive is younger than the default window, so the window opens before the first sample.
	[AvaloniaFact]
	public void AnArchiveYoungerThanTheWindowIsReadOnce()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var firstSample = _to.AddMinutes(-10.0);

		viewModel.Navigation.TrackDataExtents(firstSample, _to);
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(TimeSpan.FromSeconds(5.0).Ticks);

		viewModel.Navigation.From.Should().BeBefore(firstSample);
		provider.HistoryQueryCount.Should().Be(1);
		provider.LastQueriedFromUtc.Should().Be(firstSample);
	}

	[AvaloniaFact]
	public async Task AZoomOutPastTheArchiveFollowedByNowReadsTheWindowOnce()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var firstSample = _from.AddDays(-30.0);
		await LoadInitialHistory(viewModel, scheduler, firstSample, _to);

		viewModel.Navigation.ZoomAt(1000.0, viewModel.Navigation.To);
		viewModel.Navigation.JumpToNow();
		scheduler.AdvanceBy(TimeSpan.FromSeconds(5.0).Ticks);

		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		viewModel.Navigation.From.Should().BeBefore(firstSample);
		provider.HistoryQueryCount.Should().Be(2);
		provider.LastQueriedFromUtc.Should().Be(firstSample);
		provider.LastQueriedToUtc.Should().Be(viewModel.Navigation.To + width);
	}

	[AvaloniaFact]
	public void RequestInitialHistory_WithNoPens_DoesNotQuery()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();

		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(0);
	}

	[AvaloniaFact]
	public async Task RequestInitialHistory_FailedResult_LeavesThePenUnloadedAndDoesNotThrow()
	{
		// Mirrors the debouncer's silent drop: a failed Result returns without applying, so the pen keeps
		// its unloaded state and no exception escapes.
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		provider.FailHistory = true;

		var act = () => LoadInitialHistory(viewModel, scheduler, _from, _to);

		await act.Should().NotThrowAsync();
		viewModel.FindPen(1)!.CurrentValue.Should().BeNull();
		viewModel.FindPen(1)!.Line.Columns.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void History_LoadsColumnsCarryingMinAndMax()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
		var envelope = new PenHistoryEnvelope(
			1,
			[t0, t0.AddMinutes(1.0)],
			[1.0, 3.0],
			[5.0, 9.0],
			[2.0, 6.0]);

		state.LoadHistory(envelope, t0.AddMinutes(1.0));

		state.Line.Columns.Should().HaveCount(2);
		state.Line.Columns[0].Min.Should().Be(1.0);
		state.Line.Columns[0].Max.Should().Be(5.0);
		state.Line.Columns[1].Min.Should().Be(3.0);
		state.Line.Columns[1].Max.Should().Be(9.0);
	}

	[AvaloniaFact]
	public void Realtime_LiveEdgeColumnDegeneratesToMinEqualsMaxEqualsValue()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var timestamp = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

		state.AppendRealtime(timestamp, 42.0);

		state.Line.Columns.Should().ContainSingle();
		state.Line.Columns[0].Min.Should().Be(42.0);
		state.Line.Columns[0].Max.Should().Be(42.0);
		state.Line.Columns[0].Center.Should().Be(42.0);
	}

	[AvaloniaFact]
	public void Realtime_SampleAtOrBeforeTheLastPoint_IsIgnored()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
		state.LoadHistory(
			new PenHistoryEnvelope(1, [t0, t0.AddMinutes(1.0)], [1.0, 3.0], [5.0, 9.0], [2.0, 6.0]),
			t0.AddHours(1.0));

		state.AppendRealtime(t0.AddMinutes(1.0), 42.0);
		state.AppendRealtime(t0, 42.0);

		state.Line.Columns.Should().HaveCount(2);
		state.CurrentValue.Should().Be(6.0);
	}

	[AvaloniaFact]
	public void Realtime_SampleAfterTheLastPoint_IsAppended()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
		state.LoadHistory(
			new PenHistoryEnvelope(1, [t0, t0.AddMinutes(1.0)], [1.0, 3.0], [5.0, 9.0], [2.0, 6.0]),
			t0.AddHours(1.0));

		state.AppendRealtime(t0.AddMinutes(2.0), 42.0);

		state.Line.Columns.Should().HaveCount(3);
		state.Line.Columns[2].Center.Should().Be(42.0);
		state.CurrentValue.Should().Be(42.0);
	}

	[AvaloniaFact]
	public void Realtime_AfterAReloadEndingBeforeALiveColumn_KeepsItAndRejectsAppendsAtOrBeforeIt()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
		state.AppendRealtime(t0.AddMinutes(10.0), 7.0);

		state.LoadHistory(
			new PenHistoryEnvelope(1, [t0, t0.AddMinutes(1.0)], [1.0, 3.0], [5.0, 9.0], [2.0, 6.0]),
			t0.AddHours(1.0));
		state.AppendRealtime(t0.AddMinutes(1.5), 42.0);
		state.AppendRealtime(t0.AddMinutes(10.0), 43.0);

		state.Line.Columns.Should().HaveCount(3);
		state.Line.Columns[2].Center.Should().Be(7.0);
		state.CurrentValue.Should().Be(7.0);
	}

	[AvaloniaFact]
	public void Realtime_AfterClearHistory_KeepsTheLiveColumnAndRejectsAppendsAtOrBeforeIt()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
		state.AppendRealtime(t0.AddMinutes(10.0), 7.0);

		state.ClearHistory(t0.AddHours(1.0));
		state.AppendRealtime(t0, 42.0);
		state.AppendRealtime(t0.AddMinutes(10.0), 43.0);

		state.Line.Columns.Should().ContainSingle();
		state.Line.Columns[0].Center.Should().Be(7.0);
		state.CurrentValue.Should().Be(7.0);
	}

	[AvaloniaFact]
	public void Realtime_PastTheBufferCap_DropsTheOldestColumns()
	{
		const int Cap = 100_000;
		const int Appended = Cap + 10;
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

		for (var index = 0; index < Appended; index++)
		{
			state.AppendRealtime(t0.AddSeconds(index), index);
		}

		state.Line.Columns.Count.Should().BeLessThanOrEqualTo(Cap);
		state.Line.Columns[0].Center.Should().BeGreaterThan(0.0, "the oldest columns go");
		state.Line.Columns[^1].Center.Should().Be(Appended - 1, "the newest column stays");
	}

	[AvaloniaFact]
	public void FoldRealtime_WidensTheMinMaxOfTheCurrentColumn()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var t0 = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
		state.LoadHistory(new PenHistoryEnvelope(1, [t0], [1.0], [5.0], [2.0]), t0);

		state.FoldRealtime(t0.AddMinutes(1.0), 9.0);

		state.Line.Columns.Should().ContainSingle();
		state.Line.Columns[0].Max.Should().Be(9.0);
		state.Line.Columns[0].Min.Should().Be(1.0);
		state.Line.Columns[0].Center.Should().Be(9.0);
	}

	[AvaloniaFact]
	public async Task StickyLiveEdgeAdvance_DoesNotReQueryHistory()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
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
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from, _to);
		var toBefore = viewModel.Navigation.To;

		viewModel.Navigation.OnLiveEdge(toBefore.AddMinutes(1.0));

		viewModel.Navigation.To.Should().Be(toBefore.AddMinutes(1.0));
	}

	[AvaloniaFact]
	public void Coordinator_CoarseLayerRealtime_FoldsInsteadOfGrowingColumns()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel(
			realtimeInterval: TimeSpan.FromMilliseconds(10));
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));

		// A coarse (non-Raw) layer folds realtime into the current column instead of appending.
		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		viewModel.Navigation.ActiveLayer.Should().NotBe(AggregationLayer.Raw);
		var columnsBefore = state.Line.Columns.Count;

		scheduler.AdvanceBy(BatchWindow.Ticks);

		state.Line.Columns.Count.Should().Be(columnsBefore);
	}

	[AvaloniaFact]
	public async Task AReturnToRawAfterAHeldCoarseReadReadsRaw()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(_realtimeInterval);
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadRawEndingAtTheRealtimeEpoch(viewModel, scheduler, provider);
		var rawWidth = viewModel.Navigation.To - viewModel.Navigation.From;

		await ZoomOutAndIssueTheCoarseRead(viewModel, scheduler, provider);
		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);
		var queriesBefore = provider.HistoryQueryCount;

		ZoomBackToTheRawWidth(viewModel, scheduler, rawWidth);

		await AwaitQueryCount(provider, queriesBefore + 1);
		provider.LastQueriedLayer.Should().Be(AggregationLayer.Raw);
	}

	[AvaloniaFact]
	public async Task AReturnToRawAfterAFailedCoarseReadReadsRaw()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(_realtimeInterval);
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadRawEndingAtTheRealtimeEpoch(viewModel, scheduler, provider);
		var rawWidth = viewModel.Navigation.To - viewModel.Navigation.From;

		provider.FailHistory = true;
		await ZoomOutAndIssueTheCoarseRead(viewModel, scheduler, provider);
		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);
		provider.FailHistory = false;
		var queriesBefore = provider.HistoryQueryCount;

		ZoomBackToTheRawWidth(viewModel, scheduler, rawWidth);

		await AwaitQueryCount(provider, queriesBefore + 1);
		provider.LastQueriedLayer.Should().Be(AggregationLayer.Raw);
	}

	[AvaloniaFact]
	public async Task AReturnToRawAfterALandedCoarseReadReadsRaw()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(_realtimeInterval);
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadRawEndingAtTheRealtimeEpoch(viewModel, scheduler, provider);
		var rawWidth = viewModel.Navigation.To - viewModel.Navigation.From;

		await ZoomOutAndIssueTheCoarseRead(viewModel, scheduler, provider);
		await ReleaseAndAwaitResults(
			viewModel, 1, () => provider.HistoryGate.SetResult(EnvelopeEndingAtTheRealtimeEpoch()));
		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);
		var queriesBefore = provider.HistoryQueryCount;

		ZoomBackToTheRawWidth(viewModel, scheduler, rawWidth);

		await AwaitQueryCount(provider, queriesBefore + 1);
		provider.LastQueriedLayer.Should().Be(AggregationLayer.Raw);
	}

	[AvaloniaFact]
	public async Task AFoldIntoACoarseEnvelopeKeepsThePrefetchedBand()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(_realtimeInterval);
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		viewModel.Navigation.TrackDataExtents(
			FakeDataProvider.RealtimeEpoch.AddDays(-30.0), FakeDataProvider.RealtimeEpoch);
		await ZoomOutAndIssueTheCoarseRead(viewModel, scheduler, provider);
		await ReleaseAndAwaitResults(
			viewModel, 1, () => provider.HistoryGate.SetResult(EnvelopeEndingAtTheRealtimeEpoch()));
		var columnsBefore = state.Line.Columns.Count;
		var queriesBefore = provider.HistoryQueryCount;

		scheduler.AdvanceBy(BatchWindow.Ticks);

		state.Line.Columns.Count.Should().Be(columnsBefore, "a coarse view folds the batch");
		provider.HistoryQueryCount.Should().Be(queriesBefore);

		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		viewModel.Navigation.PanBy(-width / 4);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(queriesBefore, "the pan stays inside the coarse band");
	}

	[AvaloniaFact]
	public async Task AnAppendOverRawColumnsKeepsTheRawBand()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(_realtimeInterval);
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadRawEndingAtTheRealtimeEpoch(viewModel, scheduler, provider);
		state.Line.Columns[^1].X.Should().BeGreaterThan(
			LocalTimeAxis.ToAxis(FakeDataProvider.RealtimeEpoch), "the batches appended");
		var queriesBefore = provider.HistoryQueryCount;

		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		viewModel.Navigation.PanBy(-width / 4);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(queriesBefore, "the pan stays inside the Raw band");
	}

	[AvaloniaFact]
	public async Task AnApplyKeepsTheLiveColumnsPastItsSnapshot()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(_realtimeInterval);
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		provider.GatedLayer = AggregationLayer.Raw;
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		provider.HistoryQueryCount.Should().Be(1, "the read is held");

		scheduler.AdvanceBy(BatchWindow.Ticks * 3);
		var liveColumns = state.Line.Columns.ToArray();
		liveColumns[^1].X.Should().BeGreaterThan(LocalTimeAxis.ToAxis(FakeDataProvider.RealtimeEpoch));

		await ReleaseAndAwaitResults(
			viewModel, 1, () => provider.HistoryGate.SetResult(EnvelopeEndingAtTheRealtimeEpoch()));

		state.Line.Columns[^1].X.Should().Be(liveColumns[^1].X, "the read's snapshot ends before the live columns");
		state.Line.Columns.Should().HaveCount(
			2 + liveColumns.Count(column => column.X > LocalTimeAxis.ToAxis(FakeDataProvider.RealtimeEpoch)));
	}

	[AvaloniaFact]
	public async Task APanIntoThePastDropsTheLiveColumnsTheReadDidNotReach()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(_realtimeInterval);
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadRawEndingAtTheRealtimeEpoch(viewModel, scheduler, provider);
		state.Line.Columns[^1].X.Should().BeGreaterThan(LocalTimeAxis.ToAxis(FakeDataProvider.RealtimeEpoch));
		var queriesBefore = provider.HistoryQueryCount;

		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		viewModel.Navigation.PanBy(-width * 3);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		await AwaitQueryCount(provider, queriesBefore + 1);
		var bandFrom = provider.LastQueriedFromUtc!.Value;
		var bandTo = provider.LastQueriedToUtc!.Value;
		bandTo.Should().BeBefore(FakeDataProvider.RealtimeEpoch);

		await ReleaseAndAwaitResults(viewModel, 1, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
				[new PenHistoryEnvelope(1, [bandFrom, bandTo], [1.0, 1.0], [1.0, 1.0], [1.0, 1.0])])));

		state.Line.Columns.Select(column => column.X).Should().Equal(
			LocalTimeAxis.ToAxis(bandFrom),
			LocalTimeAxis.ToAxis(bandTo));
	}

	[AvaloniaFact]
	public async Task APenAbsentFromTheResultKeepsItsLiveColumns()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(_realtimeInterval);
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var absent = viewModel.AddPen(new Pen(2, "Pen 2", ["Group A"], "#00ff00"));
		provider.GatedLayer = AggregationLayer.Raw;
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		scheduler.AdvanceBy(BatchWindow.Ticks * 3);
		var liveColumns = absent.Line.Columns.ToArray();
		liveColumns.Should().NotBeEmpty();

		await ReleaseAndAwaitResults(
			viewModel, 1, () => provider.HistoryGate.SetResult(EnvelopeEndingAtTheRealtimeEpoch()));

		absent.Line.Columns.Should().Equal(liveColumns);
		absent.CurrentValue.Should().Be(liveColumns[^1].Center);
	}

	[AvaloniaFact]
	public async Task ACoarseApplyKeepsTheLiveColumnsAndTheNextFoldWidensTheLastOne()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(_realtimeInterval);
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadRawEndingAtTheRealtimeEpoch(viewModel, scheduler, provider);
		await ZoomOutAndIssueTheCoarseRead(viewModel, scheduler, provider);
		var lastLiveX = state.Line.Columns[^1].X;

		await ReleaseAndAwaitResults(
			viewModel, 1, () => provider.HistoryGate.SetResult(EnvelopeEndingAtTheRealtimeEpoch()));

		state.Line.Columns[^1].X.Should().Be(lastLiveX);
		var columnsBefore = state.Line.Columns.Count;
		var keptColumn = state.Line.Columns[^1];

		scheduler.AdvanceBy(BatchWindow.Ticks);

		state.Line.Columns.Should().HaveCount(columnsBefore);
		state.Line.Columns[^1].X.Should().Be(lastLiveX);
		state.Line.Columns[^1].Max.Should().BeGreaterThan(keptColumn.Max);
		state.CurrentValue.Should().Be(state.Line.Columns[^1].Center);
	}

	[AvaloniaFact]
	public async Task AValueAfterACoarseEnvelopeEndingOnAGapTakesTheReading()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(_realtimeInterval);
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadRawEndingAtTheRealtimeEpoch(viewModel, scheduler, provider);
		await ZoomOutAndIssueTheCoarseRead(viewModel, scheduler, provider);
		var gapUtc = LocalTimeAxis.FromAxis(state.Line.Columns[^1].X).AddMilliseconds(1.0);
		var gapX = LocalTimeAxis.ToAxis(gapUtc);

		await ReleaseAndAwaitResults(viewModel, 1, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
				[new PenHistoryEnvelope(
					1,
					[FakeDataProvider.RealtimeEpoch.AddHours(-1.0), gapUtc],
					[1.0, double.NaN],
					[1.0, double.NaN],
					[1.0, double.NaN])])));
		state.Line.Columns[^1].X.Should().Be(gapX, "the envelope ends on a gap past every live column");
		state.CurrentValue.Should().Be(1.0);

		scheduler.AdvanceBy(BatchWindow.Ticks);

		state.CurrentValue.Should().BeGreaterThan(1.0, "the batch's value takes the reading");
		state.CurrentValue.Should().Be(state.Line.Columns[^1].Center);
		state.Line.Columns[^1].X.Should().BeGreaterThan(gapX);
		state.Line.Columns.Should().Contain(column => column.X == gapX && double.IsNaN(column.Center));
	}

	[AvaloniaFact]
	public void Coordinator_RawLayerRealtime_AppendsColumns()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel(
			realtimeInterval: TimeSpan.FromMilliseconds(10));
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);
		var columnsBefore = state.Line.Columns.Count;

		scheduler.AdvanceBy(BatchWindow.Ticks);

		state.Line.Columns.Count.Should().BeGreaterThan(columnsBefore);
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

		scheduler.AdvanceBy(BatchWindow.Ticks);

		first.Line.Columns.Should().NotBeEmpty();
		second.Line.Columns.Should().NotBeEmpty();
		first.Line.Columns.Should().NotContain(column => double.IsNaN(column.Center));
		second.Line.Columns.Should().NotContain(column => double.IsNaN(column.Center));
	}

	[AvaloniaFact]
	public void EveryPlottable_UsesTheSharedBottomXAxis()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		viewModel.AddPen(new Pen(2, "Pen 2", ["Group B"], "#00ff00"));

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
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));

		// Each notch fires within the quiet period, so the throttle must collapse them to one trailing query.
		for (var notch = 0; notch < 5; notch++)
		{
			viewModel.Navigation.ZoomAt(2.0, viewModel.Navigation.To);
			scheduler.AdvanceBy(TimeSpan.FromMilliseconds(20).Ticks);
		}

		provider.HistoryQueryCount.Should().Be(0);

		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(1);
	}

	[AvaloniaFact]
	public void AfterStreamGoesQuiet_TheLastWindowIsQueried()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));

		// A first sample well before the window keeps the prefetch margin off its left clamp.
		viewModel.Navigation.TrackDataExtents(_from.AddDays(-30.0), _to);
		viewModel.Navigation.ZoomAt(2.0, viewModel.Navigation.To);
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(20).Ticks);
		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		var lastFrom = viewModel.Navigation.From;
		var lastTo = viewModel.Navigation.To;
		var lastLayer = viewModel.Navigation.ActiveLayer;
		var lastWidth = lastTo - lastFrom;

		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.LastQueriedFromUtc.Should().Be(lastFrom - lastWidth);
		provider.LastQueriedToUtc.Should().Be(lastTo + lastWidth);
		provider.LastQueriedLayer.Should().Be(lastLayer);
	}

	[AvaloniaFact]
	public async Task APanInsideThePrefetchedBandIssuesNoHistoryQuery()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		provider.HistoryQueryCount.Should().Be(1);

		// Half a window width: the margin the first query fetched still holds every column.
		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-30.0));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(1);
	}

	[AvaloniaFact]
	public async Task AReportedWidthInsideTheDeadbandKeepsThePrefetchedBand()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		viewModel.ReportDataAreaWidth(700.0);
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		provider.HistoryQueryCount.Should().Be(1);

		viewModel.ReportDataAreaWidth(704.0);
		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-30.0));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		viewModel.Navigation.TargetColumnCount.Should().Be(512);
		provider.HistoryQueryCount.Should().Be(1);
	}

	[AvaloniaFact]
	public async Task APanPastTheBandLeavesTheAxisUntouchedUntilTheQueryLands()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		var revisionBefore = viewModel.ScalesRevision;

		viewModel.Navigation.PanBy(-4 * width);

		viewModel.ScalesRevision.Should().Be(revisionBefore);

		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(2);
		viewModel.ScalesRevision.Should().BeGreaterThan(revisionBefore);
	}

	// A query that never landed filled no band, so the pan that follows has to ask again. The gate opening
	// on the request instead of on the result would leave the chart empty until a zoom or a full-window pan.
	[AvaloniaFact]
	public async Task APanInsideTheBandAfterAFailedQueryAsksAgain()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		provider.FailHistory = true;
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		provider.HistoryQueryCount.Should().Be(1);

		// The same half-window pan APanInsideThePrefetchedBandIssuesNoHistoryQuery answers with no query.
		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-30.0));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(2);
	}

	// The drag that leaves the fetched band and comes back into it, still moving when the far query lands.
	[AvaloniaFact]
	public async Task AResultForAWindowTheDragLeftReQueriesTheWindowInView()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		var windowFrom = viewModel.Navigation.From;

		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(2);

		provider.GatedLayer = null;
		viewModel.Navigation.PanBy(4 * width);

		viewModel.Navigation.From.Should().Be(windowFrom);
		provider.HistoryQueryCount.Should().Be(2);

		await ReleaseAndAwaitResults(viewModel, 1, () => provider.HistoryGate.SetResult(StaleEnvelopes()));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

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
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;

		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.GatedLayer = null;
		viewModel.Navigation.PanBy(4 * width);
		var revisionBefore = viewModel.ScalesRevision;

		await ReleaseAndAwaitResults(viewModel, 1, () => provider.HistoryGate.SetResult(StaleEnvelopes()));

		viewModel.ScalesRevision.Should().Be(revisionBefore);
	}

	[AvaloniaFact]
	public async Task AGestureEndingBackInsideTheFetchedBandCancelsTheReadItLeft()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;

		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var farRead = provider.LastQueriedCancellationToken;

		viewModel.Navigation.PanBy(4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		farRead.IsCancellationRequested.Should().BeTrue();
		provider.HistoryQueryCount.Should().Be(2);
	}

	// The apply throws after the band is loaded and the gate open, so the band is drawn though the apply was
	// reported as failed.
	[AvaloniaFact]
	public async Task AGestureEndingInsideABandWhoseApplyThrewAfterTheLoadCancelsTheReadItLeft()
	{
		var (viewModel, scheduler, provider, panel) = CreateViewModelWithPanel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var subscriberThrows = true;
		using var subscriber = viewModel.HistoryApplied.Subscribe(_ =>
		{
			if (subscriberThrows)
			{
				subscriberThrows = false;

				throw new InvalidOperationException("A subscriber rejected the history.");
			}
		});
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		await AwaitPanelEntries(panel, 1);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;

		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var farRead = provider.LastQueriedCancellationToken;

		viewModel.Navigation.PanBy(4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		farRead.IsCancellationRequested.Should().BeTrue();
		provider.HistoryQueryCount.Should().Be(2);
	}

	// The read for pen 1 alone is admitted at 150 ms; pen 2 arrives at 300 ms and the 400 ms sample tick parks
	// its read behind the first, so that read lands with pen 2 already shown. Its range covers the window in
	// view but holds no row of pen 2, and the pan that follows ends inside it while pen 2's read still runs.
	[AvaloniaFact]
	public async Task AReadForAnOlderPenSetLeavesTheNewPensReadRunning()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		viewModel.Navigation.TrackDataExtents(_from.AddDays(-30.0), _to);
		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);

		var penOneRead = provider.HistoryGate;
		provider.HistoryGate = new();
		viewModel.AddPen(new Pen(2, "Pen 2", ["Group A"], "#00ff00"));
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks + 1);

		await ReleaseAndAwaitResults(viewModel, 1, () => penOneRead.SetResult(StaleEnvelopes()));
		await AwaitQueryCount(provider, 2);

		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-30.0));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		await AwaitQueryCount(provider, 3);

		provider.LastQueriedPenIds.Should().BeEquivalentTo([1, 2]);
		provider.LastQueriedCancellationToken.IsCancellationRequested.Should().BeFalse();

		await ReleaseAndAwaitResults(viewModel, 1, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
			[
				new PenHistoryEnvelope(1, [_from, _to], [7.0, 7.0], [7.0, 7.0], [7.0, 7.0]),
				new PenHistoryEnvelope(2, [_from, _to], [7.0, 7.0], [7.0, 7.0], [7.0, 7.0])
			])));

		viewModel.FindPen(2)!.CurrentValue.Should().Be(7.0);
	}

	// Pen 2 comes back into the slot it left, so the read the return asks for names the pens and the window
	// the read before the removal applied, while the read the removal asked for is still held.
	[AvaloniaFact]
	public async Task APenThatComesBackWhileTheRemovalsReadIsHeldIsReadAgain()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		var catalogue = provider.Pens;
		viewModel.ApplyCatalogue(catalogue);
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);

		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.ApplyCatalogue([catalogue[0]]);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		provider.HistoryQueryCount.Should().Be(2);

		viewModel.ApplyCatalogue(catalogue);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		await AwaitQueryCount(provider, 3);

		provider.LastQueriedPenIds.Should().Equal(1, 2);

		await ReleaseAndAwaitResults(viewModel, 1, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
			[
				new PenHistoryEnvelope(1, [_from, _to], [7.0, 7.0], [7.0, 7.0], [7.0, 7.0]),
				new PenHistoryEnvelope(2, [_from, _to], [7.0, 7.0], [7.0, 7.0], [7.0, 7.0])
			])));

		viewModel.FindPen(2)!.CurrentValue.Should().Be(7.0);
	}

	// The same return after the removal's read failed: the failure keeps the window drawn before it, which
	// names the pens the return asks for.
	[AvaloniaFact]
	public async Task APenThatComesBackAfterTheRemovalsReadFailedIsReadAgain()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		var catalogue = provider.Pens;
		viewModel.ApplyCatalogue(catalogue);
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);

		provider.FailHistory = true;
		viewModel.ApplyCatalogue([catalogue[0]]);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		provider.HistoryQueryCount.Should().Be(2);

		provider.FailHistory = false;
		await ReleaseAndAwaitResults(viewModel, 1, () =>
		{
			viewModel.ApplyCatalogue(catalogue);
			scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		});

		provider.HistoryQueryCount.Should().Be(3);
		provider.LastQueriedPenIds.Should().Equal(1, 2);
		viewModel.FindPen(2)!.CurrentValue.Should().Be(FakeDataProvider.DefaultCenter);
	}

	[AvaloniaFact]
	public async Task AWindowWhoseApplyThrewIsReadAgainOnTheNextMove()
	{
		var (viewModel, scheduler, provider, panel) = CreateViewModelWithPanel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		viewModel.Navigation.TrackDataExtents(_from.AddDays(-30.0), _to);
		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.GatedLayer = null;
		provider.HistoryGate.SetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>([UnloadableEnvelope(1)]));
		await AwaitPanelEntries(panel, 1);

		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-1.0));
		await ReleaseAndAwaitResults(viewModel, 1, () => scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1));

		provider.HistoryQueryCount.Should().Be(2);
		state.CurrentValue.Should().Be(FakeDataProvider.DefaultCenter);
	}

	// The far result loads pen 1 and throws on pen 2, so pen 1 holds rows of a band nothing is fetched for.
	[AvaloniaFact]
	public async Task AnApplyThatThrowsPartWayLeavesNoBandFetched()
	{
		var (viewModel, scheduler, provider, panel) = CreateViewModelWithPanel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		viewModel.AddPen(new Pen(2, "Pen 2", ["Group A"], "#00ff00"));
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		var windowFrom = viewModel.Navigation.From;

		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.GatedLayer = null;
		provider.HistoryGate.SetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
			[.. StaleEnvelopes().Value, UnloadableEnvelope(2)]));
		await AwaitPanelEntries(panel, 1);
		viewModel.FindPen(1)!.CurrentValue.Should().Be(99.0);

		await ReleaseAndAwaitResults(viewModel, 1, () =>
		{
			viewModel.Navigation.PanBy(4 * width);
			scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		});

		provider.HistoryQueryCount.Should().Be(3);
		provider.LastQueriedFromUtc.Should().Be(windowFrom - width);
		viewModel.FindPen(1)!.CurrentValue.Should().Be(FakeDataProvider.DefaultCenter);
	}

	// Skipping the axis here freezes it with no path back, since the gate that would ask again opens on a
	// result. APanInsideTheBandAfterAFailedQueryAsksAgain pins the ask.
	[AvaloniaFact]
	public async Task AFailedQueryOutsideTheBandStillAppliesTheAxis()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		var revisionBefore = viewModel.ScalesRevision;
		provider.FailHistory = true;

		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(2);
		viewModel.ScalesRevision.Should().BeGreaterThan(revisionBefore);
	}

	[AvaloniaFact]
	public async Task APanPastTheBandIssuesOneQueryForTheNewRange()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-30.0), _to);
		provider.HistoryQueryCount.Should().Be(1);

		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-60.0));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

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

		// Roughly four hours: 7 s per column at 2048 columns, finer than the minute layer's 15 s spacing,
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
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));

		viewModel.Navigation.TargetColumnCount.Should().Be(HistoryColumnTarget.MaxColumns);
		await LoadInitialHistory(viewModel, scheduler, _from, _to);

		provider.LastQueriedTargetColumnCount.Should()
			.Be(HistoryPrefetch.MarginColumnFactor * HistoryColumnTarget.MaxColumns);
	}

	[AvaloniaFact]
	public async Task ReportedWidth_SetsTheQueryResolutionUnquantized()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));

		viewModel.ReportDataAreaWidth(700.0);
		viewModel.Navigation.TargetColumnCount.Should().Be(512);

		await LoadInitialHistory(viewModel, scheduler, _from, _to);

		provider.LastQueriedTargetColumnCount.Should().Be(HistoryPrefetch.MarginColumnFactor * 700);
	}

	[AvaloniaFact]
	public async Task CollapsedCanvas_KeepsTheLastReportedWidth()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
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
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var currentWidth = viewModel.Navigation.To - viewModel.Navigation.From;

		// Roughly four hours: the raw layer at 2048 columns, the minute layer at 256.
		viewModel.Navigation.ZoomAt(TimeSpan.FromHours(4.0) / currentWidth, viewModel.Navigation.To);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		provider.LastQueriedLayer.Should().Be(AggregationLayer.Raw);

		viewModel.ReportDataAreaWidth(256.0);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.LastQueriedLayer.Should().Be(AggregationLayer.Minute);
		provider.LastQueriedTargetColumnCount.Should().Be(HistoryPrefetch.MarginColumnFactor * 256);
	}

	[AvaloniaFact]
	public async Task WidthReportedWhileTheInitialQueryIsInFlight_CancelsItAndAppliesTheReportedResolution()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		provider.GatedLayer = AggregationLayer.Raw;

		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		provider.HistoryQueryCount.Should().Be(1);
		var initialRead = provider.LastQueriedCancellationToken;

		viewModel.ReportDataAreaWidth(256.0);
		provider.GatedLayer = null;
		await ReleaseAndAwaitResults(viewModel, 1, () => scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1));

		initialRead.IsCancellationRequested.Should().BeTrue();
		provider.HistoryQueryCount.Should().Be(2);
		provider.LastQueriedTargetColumnCount.Should().Be(HistoryPrefetch.MarginColumnFactor * 256);
		state.Line.Columns.Should().HaveCount(2);
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
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
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
		viewModel.DeltaReadoutText.Should()
			.Contain(Resources.DeltaTimeLabel)
			.And.Contain(Resources.DeltaValueLabel);
	}

	[AvaloniaFact]
	public async Task DeltaMode_RoutesLeftButtonToDeltaPlacementInsteadOfPan()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from, _to);
		viewModel.SetDeltaModeEnabled(true);

		viewModel.ActiveLeftButtonTool.Should().Be(LeftButtonTool.DeltaPlacement);
	}

	[AvaloniaFact]
	public async Task ExitingDeltaMode_ReturnsToPanAndClearsCursors()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
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
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		await LoadInitialHistory(viewModel, scheduler, _from.AddDays(-1.0), _to);
		var fromBefore = viewModel.Navigation.From;
		var toBefore = viewModel.Navigation.To;
		var widthBefore = toBefore - fromBefore;

		viewModel.BeginDrag();
		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-10.0));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
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
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
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
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
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
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		provider.LayerCenterOverrides[AggregationLayer.Minute] = 5.0;

		await LoadInitialHistory(viewModel, scheduler, _from, _to);
		viewModel.FindPen(1)!.CurrentValue.Should().Be(2.0);

		// A zoom-out gesture re-queries the coarser layer through the debouncer.
		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Minute);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		viewModel.FindPen(1)!.CurrentValue.Should().Be(5.0);
	}

	[AvaloniaFact]
	public async Task StaleInitialHistory_IsCancelledByAZoomEndingOnAnotherWindow()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));

		provider.GatedLayer = AggregationLayer.Raw;
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var initialRead = provider.LastQueriedCancellationToken;

		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		viewModel.Navigation.ActiveLayer.Should().NotBe(AggregationLayer.Raw);
		await ReleaseAndAwaitResults(viewModel, 1, () => scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1));

		initialRead.IsCancellationRequested.Should().BeTrue();
		provider.HistoryQueryCount.Should().Be(2);
		viewModel.FindPen(1)!.CurrentValue.Should().Be(2.0);
	}

	[AvaloniaFact]
	public void DisposingTheChart_CancelsTheHistoryReadInFlight()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		provider.GatedLayer = AggregationLayer.Raw;

		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var read = provider.LastQueriedCancellationToken;
		read.IsCancellationRequested.Should().BeFalse();

		viewModel.Dispose();

		read.IsCancellationRequested.Should().BeTrue();
	}

	[AvaloniaFact]
	public void RequestInitialHistory_ResultArrivingAfterDispose_DoesNotApplyOrThrow()
	{
		// Disposal-safety: the initial query is held in flight, the view model is disposed (disposing the
		// Plot), then the gate is released. Disposal ends the history subscription, so nothing mutates the
		// disposed Plot.
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		provider.GatedLayer = AggregationLayer.Raw;
		var pen = viewModel.FindPen(1)!;

		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

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
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		viewModel.AddPen(new Pen(2, "Pen 2", ["Group A"], "#00ff00"));

		await LoadInitialHistory(viewModel, scheduler, _from, _to);
		viewModel.FindPen(2)!.Line.Columns.Should().HaveCount(2);

		// The next window holds no row for pen 2, so the provider answers with no envelope for it at all.
		provider.OmittedPenIds.Add(2);
		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		viewModel.FindPen(2)!.Line.Columns.Should().BeEmpty();
		viewModel.FindPen(2)!.CurrentValue.Should().BeNull();
		viewModel.FindPen(1)!.Line.Columns.Should().HaveCount(2);
	}

	[AvaloniaFact]
	public async Task History_PenAddedWhileTheQueryWasInFlight_KeepsItsCurve()
	{
		// What the request asked for, not the current pen dictionary, decides what is cleared: pen 2 is
		// added after the request was issued, so a result omitting it says nothing about it. The gate's
		// continuation runs on the thread pool, so the apply is awaited rather than assumed inline.
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var watch = viewModel.HistoryApplied.Subscribe(_ => applied.TrySetResult());

		provider.GatedLayer = AggregationLayer.Raw;
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		var lateState = viewModel.AddPen(new Pen(2, "Pen 2", ["Group A"], "#00ff00"));
		lateState.LoadHistory(new PenHistoryEnvelope(2, [_from, _to], [4.0, 4.0], [4.0, 4.0], [4.0, 4.0]), _to);

		provider.HistoryGate.SetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
		[
			new PenHistoryEnvelope(1, [_from, _to], [1.0, 2.0], [1.0, 2.0], [1.0, 2.0])
		]));
		await applied.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		viewModel.FindPen(1)!.CurrentValue.Should().Be(2.0);
		lateState.Line.Columns.Should().HaveCount(2);
		lateState.CurrentValue.Should().Be(4.0);
	}

	[AvaloniaFact]
	public void AFailedHistoryQueryReachesTheMessagePanelAndStillRedraws()
	{
		var (viewModel, scheduler, panel) = CreateReportingViewModel(TimeSpan.FromHours(1.0), out var provider);
		using var chart = viewModel;
		using var messagePanel = panel;
		chart.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var redraws = 0;
		using var redrawSubscription = chart.RedrawRequested.Subscribe(_ => redraws++);
		provider.FailHistory = true;

		chart.RequestInitialHistory();
		// Past the debounce window and short of the 400 ms cap interval, which would admit a second query for
		// the same window.
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(200.0).Ticks);

		messagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(new Error("Forced history failure.")));
		redraws.Should().Be(1, "the failure path keeps the deliberate ApplyAxisModel and RequestRedraw recovery");
	}

	[AvaloniaFact]
	public void RealtimeApplyReportsAThrowingConsumerAndKeepsTheSubscription()
	{
		var (viewModel, scheduler, panel) = CreateReportingViewModel(TimeSpan.FromMilliseconds(10.0), out _);
		using var chart = viewModel;
		using var messagePanel = panel;
		var state = chart.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var throwsOnNextWindow = true;
		void FailOnce(object? sender, NavigationWindow window)
		{
			if (!throwsOnNextWindow)
			{
				return;
			}

			throwsOnNextWindow = false;

			throw new InvalidOperationException("Realtime consumer failure.");
		}

		chart.Navigation.WindowChanged += FailOnce;

		// Without the guard the throw leaves ApplyRealtimeBatch, and Rx rethrows it out of this advance.
		scheduler.AdvanceBy(BatchWindow.Ticks + 1);
		var valueWhenTheApplyThrew = state.CurrentValue;
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(200.0).Ticks);

		throwsOnNextWindow.Should().BeFalse("the throwing handler has to have run");
		messagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(
				new ExceptionalError(new InvalidOperationException("Realtime consumer failure."))));
		state.CurrentValue.Should().NotBe(
			valueWhenTheApplyThrew, "the batches after the throw still reach the pen");
	}

	// Rx turns a throwing projection into OnError, which would end the Publish().RefCount() stream for both
	// subscribers and rethrow out of this advance instead of reaching the panel.
	[AvaloniaFact]
	public void AThrowInTheBatchProjectionIsReportedAndTheRealtimeApplyContinues()
	{
		var (viewModel, scheduler, panel) = CreateReportingViewModel(TimeSpan.FromMilliseconds(10.0), out var provider);
		using var chart = viewModel;
		using var messagePanel = panel;
		var state = chart.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		provider.PoisonRealtimeWindow = true;

		scheduler.AdvanceBy(BatchWindow.Ticks + 1);

		messagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(
				new ExceptionalError(new InvalidOperationException("Poisoned realtime window."))));
		var valueWhenTheWindowFailed = state.CurrentValue;

		provider.PoisonRealtimeWindow = false;
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(200.0).Ticks);

		state.CurrentValue.Should().NotBe(
			valueWhenTheWindowFailed, "the batches after the failed window still reach the pen");
	}

	[AvaloniaFact]
	public void AProviderThatEndsTheLiveEdge_IsReportedOnce()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10.0))
		{
			RealtimeStreamFailure = new InvalidOperationException("the provider ended the live edge")
		};
		var coordinator = new TrendCoordinator(provider, provider.Pens, scheduler, scheduler, BatchWindow);
		using var panel = new MessagePanelViewModel();
		using var chart = new TrendChartViewModel(
			coordinator,
			scheduler,
			scheduler,
			panel,
			NullLogger<TrendChartViewModel>.Instance);

		var advance = () => scheduler.AdvanceBy(BatchWindow.Ticks * 2);

		advance.Should().NotThrow();
		panel.Entries.Should().ContainSingle().Which.RepeatCount.Should()
			.Be(1, "the one failure reaches the operator as one occurrence");
		panel.Entries[0].View.Detail.Should().Contain("the provider ended the live edge");
	}

	// The report edits a bound collection, the likeliest thrower on the hop. Without the guard the throw
	// leaves the realtime subscription this method is the observer of and ends the live edge for the session.
	// The log carries both halves: the failure that was being reported, and the panel refusing it.
	[AvaloniaFact]
	public void AThrowOutOfTheMessagePanel_IsLoggedAndTheRealtimeApplyContinues()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10.0));
		var coordinator = new TrendCoordinator(provider, provider.Pens, scheduler, scheduler, BatchWindow);
		var logger = new RecordingLogger<TrendChartViewModel>();
		using var panel = new MessagePanelViewModel();
		using var chart = new TrendChartViewModel(coordinator, scheduler, scheduler, panel, logger);
		var state = chart.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		var poison = ReportingTestDoubles.PoisonEntries(panel);

		provider.PoisonRealtimeWindow = true;

		scheduler.AdvanceBy(BatchWindow.Ticks + 1);

		logger.Failures.Select(entry => entry.Message).Should().Equal(
			"Poisoned realtime window.",
			"the bound list threw");

		poison.Dispose();
		provider.PoisonRealtimeWindow = false;
		var valueWhenTheReportThrew = state.CurrentValue;
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(200.0).Ticks);

		state.CurrentValue.Should().NotBe(
			valueWhenTheReportThrew, "the batches after the failed report still reach the pen");
	}

	// Both schedulers are virtual, unlike CreateViewModel's: these tests drive the realtime stream and the
	// history debouncer by advancing time.
	private static (TrendChartViewModel ViewModel, TestScheduler Scheduler, MessagePanelViewModel Panel)
		CreateReportingViewModel(TimeSpan realtimeInterval, out FakeDataProvider provider)
	{
		var scheduler = new TestScheduler();
		provider = new FakeDataProvider(scheduler, realtimeInterval);
		var coordinator = new TrendCoordinator(
			provider,
			provider.Pens,
			scheduler,
			scheduler,
			BatchWindow);
		var panel = new MessagePanelViewModel();
		var viewModel = new TrendChartViewModel(
			coordinator,
			scheduler,
			scheduler,
			panel,
			NullLogger<TrendChartViewModel>.Instance);

		return (viewModel, scheduler, panel);
	}

	// The UI scheduler serves the chart alone, so whatever it holds queued is the chart's.
	private static TrendChartViewModel CreateChartOn(QueueReadingScheduler uiScheduler)
	{
		var dataScheduler = new TestScheduler();
		var provider = new FakeDataProvider(dataScheduler, TimeSpan.FromHours(1.0));

		return new TrendChartViewModel(
			CreateCoordinator(dataScheduler, provider),
			dataScheduler,
			uiScheduler,
			new MessagePanelViewModel(),
			NullLogger<TrendChartViewModel>.Instance);
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

	private static Result<IReadOnlyList<PenHistoryEnvelope>> EnvelopeEndingAtTheRealtimeEpoch()
	{
		return Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
		[
			new PenHistoryEnvelope(
				1,
				[FakeDataProvider.RealtimeEpoch.AddHours(-1.0), FakeDataProvider.RealtimeEpoch],
				[1.0, 1.0],
				[1.0, 1.0],
				[1.0, 1.0])
		]);
	}

	// One notch puts the width on the zoom ladder, so a zoom out and back by one factor returns to it exactly.
	private static async Task LoadRawEndingAtTheRealtimeEpoch(
		TrendChartViewModel viewModel,
		TestScheduler scheduler,
		FakeDataProvider provider)
	{
		viewModel.Navigation.TrackDataExtents(
			FakeDataProvider.RealtimeEpoch.AddDays(-30.0), FakeDataProvider.RealtimeEpoch);
		provider.GatedLayer = AggregationLayer.Raw;
		viewModel.Navigation.ZoomAt(0.8, viewModel.Navigation.To);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		await ReleaseAndAwaitResults(
			viewModel, 1, () => provider.HistoryGate.SetResult(EnvelopeEndingAtTheRealtimeEpoch()));
		provider.HistoryGate = new();

		scheduler.AdvanceBy(BatchWindow.Ticks * 3);
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);
	}

	// The coarse read waits at the gate, unless FailHistory fails it before the gate.
	private static async Task ZoomOutAndIssueTheCoarseRead(
		TrendChartViewModel viewModel,
		TestScheduler scheduler,
		FakeDataProvider provider)
	{
		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		viewModel.Navigation.ActiveLayer.Should().NotBe(AggregationLayer.Raw);
		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		var queriesBefore = provider.HistoryQueryCount;
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		await AwaitQueryCount(provider, queriesBefore + 1);
		provider.LastQueriedLayer.Should().Be(viewModel.Navigation.ActiveLayer);
	}

	private static void ZoomBackToTheRawWidth(TrendChartViewModel viewModel, TestScheduler scheduler, TimeSpan rawWidth)
	{
		viewModel.Navigation.ZoomAt(1.0 / 48.0, viewModel.Navigation.To);
		(viewModel.Navigation.To - viewModel.Navigation.From).Should().Be(rawWidth);
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
	}

	// Loading it throws inside the apply, as a defect there would; the axis model reads it afterwards unharmed.
	private static PenHistoryEnvelope UnloadableEnvelope(int penId)
	{
		return new PenHistoryEnvelope(penId, [_from, _to], new ThrowsOnFirstRead(99.0, 2), [99.0, 99.0], [99.0, 99.0]);
	}

	private static (TrendChartViewModel ViewModel, TestScheduler Scheduler, FakeDataProvider Provider,
		MessagePanelViewModel Panel) CreateViewModelWithPanel()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromHours(1));
		var panel = new MessagePanelViewModel();
		var viewModel = new TrendChartViewModel(
			CreateCoordinator(scheduler, provider),
			scheduler,
			ImmediateScheduler.Instance,
			panel,
			NullLogger<TrendChartViewModel>.Instance);

		return (viewModel, scheduler, provider, panel);
	}

	// The apply runs on the thread Rx resumes the released query on, so its report lands after the release.
	private static async Task AwaitPanelEntries(MessagePanelViewModel panel, int count)
	{
		var deadline = DateTime.UtcNow + _testDeadline;

		while (panel.Entries.Count < count && DateTime.UtcNow < deadline)
		{
			await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
		}

		panel.Entries.Should().HaveCount(count);
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

	private sealed class ThrowsOnFirstRead(double value, int count) : IReadOnlyList<double>
	{
		private int _reads;

		public int Count => count;

		public double this[int index] => Interlocked.Increment(ref _reads) == 1
			? throw new InvalidOperationException("The envelope could not be loaded.")
			: value;

		public IEnumerator<double> GetEnumerator()
		{
			return Enumerable.Repeat(value, count).GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}

	private sealed class QueueReadingScheduler : TestScheduler
	{
		public bool HasQueuedWork => GetNext() is not null;
	}
}
