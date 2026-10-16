using System.Collections;
using System.Reactive.Linq;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Testing;
using Microsoft.Reactive.Testing;

using ReactiveUI;

using ScottPlot;

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
	public enum RangeGesture
	{
		Shift,
		Scale
	}

	private const int PickWindowSeconds = 60;
	private const double PickFirstValue = 20.0;
	private const double PickSecondValue = 80.0;
	private const double PickClearancePixels = 8.0;
	private const double PickTieTolerancePixels = 1e-3;
	private const double GestureTolerance = 1e-9;

	// Columns the margin read asks for per visible column: the window and one margin window on each side.
	private const int ExpandedColumnFactor = 3;

	private static readonly IReadOnlyList<Pen> _fiftyPens =
	[
		.. Enumerable.Range(1, 50).Select(penId => new Pen(
			penId, $"Pen {penId}", ["Group A"], "#ff0000", EnabledOnStart: penId % 6 == 0))
	];

	private static readonly int[] _switchedOnPenIds = [6, 12, 18, 24, 30, 36, 42, 48];

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
	public void SetActivePen_UpdatesActivePenId()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.AddPen(new Pen(2, "Pen 2", ["Group B"], "#00ff00"));

		viewModel.SetActivePen(2).Should().BeTrue();

		viewModel.ActivePenId.Should().Be(2);
	}

	[AvaloniaFact]
	public void SetActivePen_MovesThatPensLineToTheTopOfThePlot()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		var second = viewModel.AddPen(FakeDataProvider.SecondPen);
		viewModel.AddPen(new Pen(3, "Pen 3", ["Group B"], "#0000ff"));

		viewModel.SetActivePen(second.Pen.PenId);

		viewModel.Plot.PlottableList.Last().Should().BeSameAs(second.Line);
	}

	[AvaloniaFact]
	public void ACatalogueThatAddsAPen_KeepsTheActivePensLineOnTop()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var first = viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.AddPen(FakeDataProvider.SecondPen);

		viewModel.AddPen(new Pen(3, "Pen 3", ["Group B"], "#0000ff"));

		viewModel.ActivePenId.Should().Be(first.Pen.PenId);
		viewModel.Plot.PlottableList.Last().Should().BeSameAs(first.Line);
	}

	[AvaloniaFact]
	public void SwitchingTheActivePenOff_MovesTheLineOfTheNewActivePenToTheTop()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		var second = viewModel.AddPen(FakeDataProvider.SecondPen);
		viewModel.AddPen(new Pen(3, "Pen 3", ["Group B"], "#0000ff"));

		viewModel.SetPenVisibility(FakeDataProvider.FirstPen.PenId, false);

		viewModel.ActivePenId.Should().Be(second.Pen.PenId);
		viewModel.Plot.PlottableList.Last().Should().BeSameAs(second.Line);
	}

	[AvaloniaFact]
	public void ACatalogueThatRemovesTheActivePen_PutsTheLineOfTheNewActivePenOnTop()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var first = viewModel.AddPen(FakeDataProvider.FirstPen);
		var second = viewModel.AddPen(FakeDataProvider.SecondPen);
		var third = viewModel.AddPen(new Pen(3, "Pen 3", ["Group B"], "#0000ff"));

		viewModel.ApplyCatalogue([second.Pen, third.Pen]);

		viewModel.ActivePenId.Should().NotBe(first.Pen.PenId);
		viewModel.Plot.PlottableList.Last().Should().BeSameAs(viewModel.FindPen(viewModel.ActivePenId)!.Line);
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
	public void PickPenAt_APixelOnAnotherPensLine_MakesThatPenActive()
	{
		var (viewModel, dataRect) = RenderTwoFlatPens(PickFirstValue, PickSecondValue);

		viewModel.PickPenAt(PixelOnLine(viewModel, 2, PickSecondValue, dataRect));

		viewModel.ActivePenId.Should().Be(2);
	}

	[AvaloniaFact]
	public void PickPenAt_EmptySpace_KeepsTheActivePenAndDropsItsEmphasis()
	{
		var (viewModel, dataRect) = RenderTwoFlatPens(PickFirstValue, PickSecondValue);
		var between = PixelBetweenTheLines(viewModel, dataRect);
		var redraws = 0;
		using var counter = viewModel.RedrawRequested.Subscribe(_ => redraws++);

		viewModel.PickPenAt(between);

		redraws.Should().Be(1);
		viewModel.ActivePenId.Should().Be(1);
		viewModel.Plot.PlottableList.Last().Should().BeSameAs(viewModel.FindPen(1)!.Line);
		InkRatio(viewModel, StrokeProbe.Red, StrokeProbe.Green).Should().BeLessThan(StrokeProbe.ThinInkRatio);

		viewModel.PickPenAt(between);

		redraws.Should().Be(1, "a second miss has nothing left to thin");
	}

	[AvaloniaFact]
	public void PickPenAt_BelowTheDataArea_DropsTheEmphasis()
	{
		var (viewModel, dataRect) = RenderTwoFlatPens(PickFirstValue, PickSecondValue);

		viewModel.PickPenAt(new Pixel(dataRect.HorizontalCenter, dataRect.Bottom + PickClearancePixels));

		viewModel.ActivePenId.Should().Be(1);
		InkRatio(viewModel, StrokeProbe.Red, StrokeProbe.Green).Should().BeLessThan(StrokeProbe.ThinInkRatio);
	}

	[AvaloniaFact]
	public void SetActivePen_OfTheActivePenAfterAMiss_EmphasizesItAgain()
	{
		var (viewModel, dataRect) = RenderTwoFlatPens(PickFirstValue, PickSecondValue);
		viewModel.PickPenAt(PixelBetweenTheLines(viewModel, dataRect));

		viewModel.SetActivePen(1);

		InkRatio(viewModel, StrokeProbe.Red, StrokeProbe.Green).Should().BeGreaterThan(StrokeProbe.ThickInkRatio);
	}

	[AvaloniaFact]
	public void PickPenAt_TheActivePensLineAfterAMiss_EmphasizesItAgain()
	{
		var (viewModel, dataRect) = RenderTwoFlatPens(PickFirstValue, PickSecondValue);
		viewModel.PickPenAt(PixelBetweenTheLines(viewModel, dataRect));

		viewModel.PickPenAt(PixelOnLine(viewModel, 1, PickFirstValue, dataRect));

		InkRatio(viewModel, StrokeProbe.Red, StrokeProbe.Green).Should().BeGreaterThan(StrokeProbe.ThickInkRatio);
	}

	[AvaloniaFact]
	public void SwitchingTheActivePenOffAfterAMiss_LeavesThePenTakingTheSlotThin()
	{
		var (viewModel, dataRect) = RenderTwoFlatPens(PickFirstValue, PickSecondValue);
		viewModel.PickPenAt(PixelBetweenTheLines(viewModel, dataRect));

		viewModel.SetPenVisibility(1, false);
		viewModel.ActivePenId.Should().Be(2);
		viewModel.SetPenVisibility(1, true);

		viewModel.ActivePenId.Should().Be(2);
		InkRatio(viewModel, StrokeProbe.Green, StrokeProbe.Red).Should().BeLessThan(StrokeProbe.ThinInkRatio);
	}

	[AvaloniaFact]
	public void ACatalogueThatAddsAPenAfterAMiss_LeavesTheActivePenThin()
	{
		var (viewModel, dataRect) = RenderTwoFlatPens(PickFirstValue, PickSecondValue);
		viewModel.PickPenAt(PixelBetweenTheLines(viewModel, dataRect));

		viewModel.AddPen(new Pen(3, "Pen 3", ["Group B"], "#0000ff"));

		viewModel.ActivePenId.Should().Be(1);
		InkRatio(viewModel, StrokeProbe.Red, StrokeProbe.Green).Should().BeLessThan(StrokeProbe.ThinInkRatio);
	}

	[AvaloniaFact]
	public void ACatalogueThatRemovesTheActivePenAfterAMiss_LeavesThePenTakingTheSlotThin()
	{
		const double ThirdValue = 50.0;
		var (viewModel, dataRect) = RenderTwoFlatPens(PickFirstValue, PickSecondValue);
		viewModel.PickPenAt(PixelBetweenTheLines(viewModel, dataRect));
		var thirdPen = new Pen(3, "Pen 3", ["Group B"], "#0000ff");
		AddFlatPen(viewModel, thirdPen, From.AddSeconds(PickWindowSeconds), ThirdValue);

		viewModel.ApplyCatalogue([FakeDataProvider.SecondPen, thirdPen]);

		viewModel.ActivePenId.Should().Be(2);
		InkRatio(viewModel, StrokeProbe.Green, StrokeProbe.Blue).Should().BeLessThan(StrokeProbe.ThinInkRatio);
	}

	[AvaloniaFact]
	public void ACatalogueThatLeavesNoPen_LeavesTheFirstPenSeatedNextThin()
	{
		var (viewModel, _) = RenderTwoFlatPens(PickFirstValue, PickSecondValue);

		viewModel.ApplyCatalogue([]);
		AddTwoFlatPens(
			viewModel,
			(FakeDataProvider.FirstPen, PickFirstValue),
			(FakeDataProvider.SecondPen, PickSecondValue),
			PickWindowSeconds);

		viewModel.ActivePenId.Should().Be(1);
		InkRatio(viewModel, StrokeProbe.Red, StrokeProbe.Green).Should().BeLessThan(StrokeProbe.ThinInkRatio);
	}

	[AvaloniaFact]
	public void PickPenAt_WithEveryPenSwitchedOff_DropsTheEmphasis()
	{
		var (viewModel, _) = RenderTwoFlatPens(PickFirstValue, PickSecondValue);
		viewModel.SetPenVisibility(1, false);
		viewModel.SetPenVisibility(2, false);

		viewModel.PickPenAt(new Pixel(FlatPlotWidth / 2.0, FlatPlotHeight / 2.0));
		viewModel.SetPenVisibility(1, true);
		viewModel.SetPenVisibility(2, true);

		viewModel.ActivePenId.Should().Be(1);
		InkRatio(viewModel, StrokeProbe.Red, StrokeProbe.Green).Should().BeLessThan(StrokeProbe.ThinInkRatio);
	}

	[AvaloniaFact]
	public void PickPenAt_AfterDispose_Throws()
	{
		var (viewModel, dataRect) = RenderTwoFlatPens(PickFirstValue, PickSecondValue);
		var between = PixelBetweenTheLines(viewModel, dataRect);

		viewModel.Dispose();

		viewModel.Invoking(chart => chart.PickPenAt(between)).Should().Throw<ObjectDisposedException>();
	}

	[AvaloniaFact]
	public void TheFirstPensSeated_LeaveTheActivePenThin()
	{
		var (viewModel, _, _, _) = CreateViewModel(RealtimeInterval);

		AddTwoFlatPens(
			viewModel,
			(FakeDataProvider.FirstPen, PickFirstValue),
			(FakeDataProvider.SecondPen, PickSecondValue),
			PickWindowSeconds);
		viewModel.Plot.RenderInMemory(FlatPlotWidth, FlatPlotHeight);

		viewModel.ActivePenId.Should().Be(1);
		InkRatio(viewModel, StrokeProbe.Red, StrokeProbe.Green).Should().BeLessThan(StrokeProbe.ThinInkRatio);
	}

	[AvaloniaFact]
	public void PickPenAt_AHiddenPensLine_IsNeverPicked()
	{
		var (viewModel, dataRect) = RenderTwoFlatPens(PickFirstValue, PickSecondValue);
		var onSecondLine = PixelOnLine(viewModel, 2, PickSecondValue, dataRect);
		viewModel.SetPenVisibility(2, false);

		viewModel.PickPenAt(onSecondLine);

		viewModel.ActivePenId.Should().Be(1);
	}

	[AvaloniaTheory]
	[InlineData(1)]
	[InlineData(2)]
	public void PickPenAt_TwoLinesAtAnEqualDistance_KeepTheActivePen(int activePenId)
	{
		var (viewModel, dataRect) = RenderTwoFlatPens(PickFirstValue, PickFirstValue, activePenId);

		var onBothLines = PixelOnLine(viewModel, 1, PickFirstValue, dataRect);
		DistanceTo(viewModel, 1, onBothLines, dataRect).Should().BeLessThan(PickClearancePixels);
		DistanceTo(viewModel, 2, onBothLines, dataRect).Should()
			.BeApproximately(DistanceTo(viewModel, 1, onBothLines, dataRect), PickTieTolerancePixels);

		viewModel.PickPenAt(onBothLines);

		viewModel.ActivePenId.Should().Be(activePenId);
	}

	[AvaloniaFact]
	public void SetAxisLimits_SwitchesPenToManualAndDrivesTheOwningAxis()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);

		viewModel.SetAxisLimits(1, 10.0, 90.0).Should().BeTrue();

		var settings = viewModel.ScaleSettings[1];
		settings.Mode.Should().Be(ScaleMode.Manual);
		settings.ManualMin.Should().Be(10.0);
		settings.ManualMax.Should().Be(90.0);
		var axis = state.Line.Axes.YAxis;
		axis.Min.Should().Be(10.0);
		axis.Max.Should().Be(90.0);
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
	public void ShiftActivePen_OnAnAutoPen_PinsItToManualAtTheShownLimitsShifted()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From, To);
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((0.5, 1.5));

		viewModel.ShiftActivePen(0.1);

		var settings = viewModel.ScaleSettings[1];
		settings.Mode.Should().Be(ScaleMode.Manual);
		settings.ManualMin.Should().BeApproximately(0.4, GestureTolerance);
		settings.ManualMax.Should().BeApproximately(1.4, GestureTolerance);
	}

	[AvaloniaFact]
	public void ShiftActivePen_OnALogPen_MovesBothLimitsByATenthOfTheDecadeSpan()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen with
		{
			ScaleMinOnStart = 1.0,
			ScaleMaxOnStart = 1000.0,
			LogScaleOnStart = true
		});

		viewModel.ShiftActivePen(-0.1);

		var settings = viewModel.ScaleSettings[1];
		settings.IsLogarithmic.Should().BeTrue();
		Math.Log10(settings.ManualMin).Should().BeApproximately(0.3, GestureTolerance);
		Math.Log10(settings.ManualMax).Should().BeApproximately(3.3, GestureTolerance);
	}

	[AvaloniaFact]
	public void ScaleActivePen_KeepsTheValueAtTheAnchorFraction()
	{
		const double AnchorFraction = 0.25;
		const double Factor = 0.8;
		var viewModel = CreateViewModelWithManualScales();
		var (minBefore, maxBefore) = viewModel.ScaleRangeForPen(1)!.Value;
		var anchorValue = minBefore + (AnchorFraction * (maxBefore - minBefore));

		viewModel.ScaleActivePen(Factor, AnchorFraction);

		var (min, max) = viewModel.ScaleRangeForPen(1)!.Value;
		(max - min).Should().BeApproximately(Factor * (maxBefore - minBefore), GestureTolerance);
		(min + (AnchorFraction * (max - min))).Should().BeApproximately(anchorValue, GestureTolerance);
	}

	[AvaloniaTheory]
	[InlineData(RangeGesture.Shift, 0.1)]
	[InlineData(RangeGesture.Scale, 0.8)]
	public void ARangeGesture_ChangesOnlyTheActivePensScaleSettings(RangeGesture gesture, double fractionOrFactor)
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(provider.Pens);
		viewModel.SetActivePen(2);
		var firstPenSettings = viewModel.ScaleSettings[1];
		var (minBefore, maxBefore) = viewModel.ScaleRangeForPen(2)!.Value;

		ApplyRangeGesture(viewModel, gesture, fractionOrFactor);

		viewModel.ScaleSettings[1].Should().Be(firstPenSettings);
		var activePenSettings = viewModel.ScaleSettings[2];
		activePenSettings.Mode.Should().Be(ScaleMode.Manual);
		activePenSettings.ManualMin.Should().NotBe(minBefore, "the gesture moves the limits it pins");
		activePenSettings.ManualMax.Should().NotBe(maxBefore);
	}

	[AvaloniaTheory]
	[InlineData(RangeGesture.Shift, double.MaxValue)]
	[InlineData(RangeGesture.Scale, 0.0)]
	public void ARangeGestureTheCoreRefuses_ChangesNothing(RangeGesture gesture, double fractionOrFactor)
	{
		var viewModel = CreateViewModelWithManualScales();
		var settingsBefore = viewModel.ScaleSettings.ToDictionary();
		var revision = viewModel.ScalesRevision;

		ApplyRangeGesture(viewModel, gesture, fractionOrFactor);

		viewModel.ScaleSettings.Should().BeEquivalentTo(settingsBefore);
		viewModel.ScalesRevision.Should().Be(revision);
	}

	[AvaloniaTheory]
	[InlineData(RangeGesture.Shift, 0.1)]
	[InlineData(RangeGesture.Scale, 0.8)]
	public void ARangeGestureOnAHiddenActivePen_ChangesNothing(RangeGesture gesture, double fractionOrFactor)
	{
		var viewModel = CreateViewModelWithManualScales();
		viewModel.SetPenVisibility(2, false);
		viewModel.SetPenVisibility(1, false);
		viewModel.ActivePenId.Should().Be(1);
		var settingsBefore = viewModel.ScaleSettings.ToDictionary();
		var revision = viewModel.ScalesRevision;

		ApplyRangeGesture(viewModel, gesture, fractionOrFactor);

		viewModel.ScaleSettings.Should().BeEquivalentTo(settingsBefore);
		viewModel.ScalesRevision.Should().Be(revision);
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
	public void RestoreInitialScale_ActsOnTheActivePen()
	{
		var viewModel = CreateViewModelWithStoredScales();

		viewModel.SetActivePen(2);
		viewModel.RestoreInitialScale();

		viewModel.ScaleRangeForPen(2)!.Value.Should().Be((5.0, 50.0));
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
	}

	[AvaloniaFact]
	public void RestoreInitialScale_ActsOnANamedPen()
	{
		var viewModel = CreateViewModelWithStoredScales();

		viewModel.RestoreInitialScale(2).Should().BeTrue();

		viewModel.ScaleRangeForPen(2)!.Value.Should().Be((5.0, 50.0));
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
	}

	[AvaloniaFact]
	public void RestoreInitialScale_RefusesAHiddenPen()
	{
		var viewModel = CreateViewModelWithStoredScales();

		viewModel.SetPenVisibility(2, false);
		viewModel.SetPenVisibility(1, false);
		viewModel.ActivePenId.Should().Be(1);
		viewModel.RestoreInitialScale();

		viewModel.RestoreInitialScale(2).Should().BeFalse();
		viewModel.ScaleRangeForPen(2)!.Value.Should().Be((20.0, 80.0));
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
	}

	[AvaloniaFact]
	public void Autoscale_ActsOnTheActivePen()
	{
		var viewModel = CreateViewModelWithManualScales();

		viewModel.SetActivePen(2);
		viewModel.AutoscaleActivePen();

		viewModel.ScaleSettings[2].Mode.Should().Be(ScaleMode.Auto);
		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Manual);
	}

	[AvaloniaFact]
	public void Autoscale_ActsOnANamedPen()
	{
		var viewModel = CreateViewModelWithManualScales();

		viewModel.AutoscalePen(2).Should().BeTrue();

		viewModel.ScaleSettings[2].Mode.Should().Be(ScaleMode.Auto);
		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Manual);
	}

	[AvaloniaFact]
	public void Autoscale_RefusesAHiddenPen()
	{
		var viewModel = CreateViewModelWithManualScales();

		viewModel.SetPenVisibility(2, false);
		viewModel.SetPenVisibility(1, false);
		viewModel.ActivePenId.Should().Be(1);
		viewModel.AutoscaleActivePen();

		viewModel.AutoscalePen(2).Should().BeFalse();
		viewModel.AutoscalePen(99).Should().BeFalse();
		viewModel.ScaleSettings[2].Mode.Should().Be(ScaleMode.Manual);
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
	public void InitialScale_RequestsARedraw()
	{
		var scheduler = new TestScheduler();
		using var viewModel = CreateChart(scheduler);
		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000", ScaleMinOnStart: 5.0, ScaleMaxOnStart: 50.0));
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks * 2);
		var redraws = 0;
		using var subscription = viewModel.RedrawRequested.Subscribe(_ => redraws++);

		viewModel.RestoreInitialScale();

		redraws.Should().Be(1);
	}

	[AvaloniaFact]
	public void AnIdleChartRaisesNoRedraw_AndARequestRaisesOneAtOnce()
	{
		var uiScheduler = new QueueReadingScheduler();
		using var viewModel = CreateViewModel(uiScheduler: uiScheduler).ViewModel;
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
		var viewModel = CreateViewModel(uiScheduler: new QueueReadingScheduler()).ViewModel;
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
		using var viewModel = CreateViewModel(uiScheduler: new QueueReadingScheduler()).ViewModel;
		var redraws = 0;
		using var subscription = viewModel.RedrawRequested.Subscribe(_ => redraws++);

		for (var request = 0; request <= 10; request++)
		{
			viewModel.SetDeltaModeEnabled(request % 2 == 0);
		}

		redraws.Should().Be(11, "the view, not the view model, joins the requests of one frame");
	}

	[AvaloniaFact]
	public void SameGroupPens_GetSeparateYAxes()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var first = viewModel.AddPen(FakeDataProvider.FirstPen);
		var second = viewModel.AddPen(FakeDataProvider.SecondPen);

		first.Line.Axes.YAxis.Should().NotBeSameAs(second.Line.Axes.YAxis);
		viewModel.AxisCount.Should().Be(2);
	}

	[AvaloniaFact]
	public void PenWithNoStoredScalePair_AutoscalesOverTheVisibleWindow()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);

		LoadInitialHistory(viewModel, scheduler, From, To);

		// The window holds the envelope's opening column alone, a flat 1.0, which the model pads by half.
		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Auto);
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((0.5, 1.5));
	}

	[AvaloniaFact]
	public void AddPen_WithEveryPenDisabledOnStart_KeepsTheFirstPenActive()
	{
		var (viewModel, _, _, _) = CreateViewModel();

		viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000", EnabledOnStart: false));
		viewModel.AddPen(new Pen(2, "Pen 2", ["Group A"], "#00ff00", EnabledOnStart: false));

		viewModel.ActivePenId.Should().Be(1);
	}

	// The readout is measured for the active pen alone, and the active pen moves on its own when the
	// operator switches the current one off.
	[AvaloniaFact]
	public void SwitchingTheActivePen_RemeasuresTheDeltaReadout()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		provider.OmittedPenIds.Add(2);
		viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.AddPen(FakeDataProvider.SecondPen);

		LoadInitialHistory(viewModel, scheduler, From, To);

		viewModel.SetDeltaModeEnabled(true);
		viewModel.PlaceDeltaCursor(From);
		viewModel.PlaceDeltaCursor(To);
		viewModel.DeltaReadout!.DeltaY.Should().NotBeNull();

		viewModel.SetActivePen(2).Should().BeTrue();

		// Pen 2 carries no envelope, so its delta over the same two cursors reads as no value.
		viewModel.DeltaReadout!.DeltaY.Should().BeNull();
	}

	[AvaloniaTheory]
	[InlineData(PenLineStyle.Stepped, false, true)]
	[InlineData(PenLineStyle.Interpolated, true, false)]
	public void APensLineStyle_ReachesThePlottable(
		PenLineStyle lineStyle,
		bool expectedOnDiagonal,
		bool expectedOnHeldStep)
	{
		var (viewModel, _, _, _) = CreateViewModel();

		var state = viewModel.AddPen(new Pen(7, "Damper", ["Dampers"], "#ff0000", LineStyle: lineStyle));

		var (onDiagonal, onHeldStep) = RenderedRise.Draw(viewModel, state, StrokeProbe.Red);

		onDiagonal.Should().Be(expectedOnDiagonal);
		onHeldStep.Should().Be(expectedOnHeldStep);
	}

	[AvaloniaFact]
	public void RequestInitialHistory_ReadsTheWindowThenItsMargins_NoDoubleLoad()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.Navigation.TrackDataExtents(From.AddDays(-30.0), To);
		provider.HistoryQueryCount.Should().Be(0);

		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(1);
		provider.LastQueriedPenIds.Should().Contain(1);

		scheduler.AdvanceBy(TimeSpan.FromSeconds(5.0).Ticks);

		provider.HistoryQueryCount.Should().Be(2, "the margins are read once, and nothing after them");
		provider.LastQueriedTargetColumnCount.Should()
			.Be(ExpandedColumnFactor * HistoryColumnTarget.MaxColumns);
	}

	[AvaloniaFact]
	public void ADetachedZoomReadsTheVisibleWindowFirstAndItsMarginsAfterIt()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		viewModel.Navigation.SetSticky(false);
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.Navigation.ZoomAt(0.5, From.AddMinutes(30.0));
		scheduler.AdvanceBy(TimeSpan.FromSeconds(5.0).Ticks);

		var from = viewModel.Navigation.From;
		var to = viewModel.Navigation.To;
		var width = to - from;
		to.Should().BeBefore(To, "the zoom left the live edge");
		ReadsSince(provider, queriesBefore).Should().Equal(
				(from, to, HistoryColumnTarget.MaxColumns),
				(from - width, to + width, ExpandedColumnFactor * HistoryColumnTarget.MaxColumns));

		viewModel.Navigation.PanBy(-width / 4);
		scheduler.AdvanceBy(TimeSpan.FromSeconds(5.0).Ticks);

		provider.HistoryQueryCount.Should().Be(queriesBefore + 2, "the pan stays inside the band the margins filled");
	}

	[AvaloniaFact]
	public void AStickyZoomReadsTheVisibleWindowWithItsRightMarginFirst()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.Navigation.ZoomAt(0.5, To);
		scheduler.AdvanceBy(TimeSpan.FromSeconds(5.0).Ticks);

		var from = viewModel.Navigation.From;
		var width = To - from;
		viewModel.Navigation.To.Should().Be(To);
		ReadsSince(provider, queriesBefore).Should().Equal(
				(from, To + width, 2 * HistoryColumnTarget.MaxColumns),
				(from - width, To + width, ExpandedColumnFactor * HistoryColumnTarget.MaxColumns));
	}

	[AvaloniaFact]
	public async Task AGestureEndingWhileTheMarginReadIsInFlightCancelsItAndReadsTheNewWindowFirst()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		viewModel.Navigation.SetSticky(false);
		viewModel.Navigation.ZoomAt(0.5, From.AddMinutes(30.0));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 2);
		var queriesBefore = provider.HistoryQueryCount;
		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 2);
		provider.HistoryQueryCount.Should().Be(queriesBefore + 1);
		provider.LastQueriedTargetColumnCount.Should()
			.Be(ExpandedColumnFactor * HistoryColumnTarget.MaxColumns, "the margin read is held");
		var marginRead = provider.LastQueriedCancellationToken;
		var width = viewModel.Navigation.To - viewModel.Navigation.From;

		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		await AwaitQueryCount(provider, queriesBefore + 2);

		marginRead.IsCancellationRequested.Should().BeTrue();
		provider.LastQueriedFromUtc.Should().Be(viewModel.Navigation.From);
		provider.LastQueriedToUtc.Should().Be(viewModel.Navigation.To);
		provider.LastQueriedTargetColumnCount.Should().Be(HistoryColumnTarget.MaxColumns);
	}

	// The first read ends one margin past the live edge, so the batches appended while it ran lie inside it.
	[AvaloniaFact]
	public async Task ABatchLandingWhileAStickyWindowsFirstReadIsInFlightIsDrawnAfterItLands()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(RealtimeInterval);
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.Navigation.TrackDataExtents(
			FakeDataProvider.RealtimeEpoch.AddDays(-30.0), FakeDataProvider.RealtimeEpoch);
		provider.GatedLayer = AggregationLayer.Raw;
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		provider.HistoryQueryCount.Should().Be(1, "the first read is held");

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks * 3);
		var lastLiveX = state.Line.SnapshotColumns()[^1].X;
		lastLiveX.Should().BeGreaterThan(LocalTimeAxis.ToAxis(FakeDataProvider.RealtimeEpoch));

		await ReleaseAndAwait(
			viewModel, 1, () => provider.HistoryGate.SetResult(EnvelopeEndingAtTheRealtimeEpoch()));

		state.Line.SnapshotColumns()[^1].X.Should().Be(lastLiveX);
	}

	// The archive is younger than the default window, so the window opens before the first sample.
	[AvaloniaFact]
	public void AnArchiveYoungerThanTheWindowIsReadOnce()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		var firstSample = To.AddMinutes(-10.0);

		viewModel.Navigation.TrackDataExtents(firstSample, To);
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(TimeSpan.FromSeconds(5.0).Ticks);

		viewModel.Navigation.From.Should().BeBefore(firstSample);
		provider.HistoryQueryCount.Should().Be(1);
		provider.LastQueriedFromUtc.Should().Be(firstSample);
	}

	[AvaloniaFact]
	public void AZoomOutPastTheArchiveFollowedByNowReadsTheWindowOnce()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		var firstSample = From.AddDays(-30.0);
		LoadInitialHistory(viewModel, scheduler, firstSample, To);
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.Navigation.ZoomAt(1000.0, viewModel.Navigation.To);
		viewModel.Navigation.JumpToNow();
		scheduler.AdvanceBy(TimeSpan.FromSeconds(5.0).Ticks);

		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		viewModel.Navigation.From.Should().BeBefore(firstSample);
		provider.HistoryQueryCount.Should().Be(queriesBefore + 1, "the margins add no time that can hold rows");
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
	public void StickyLiveEdgeAdvance_DoesNotReQueryHistory()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From, To);
		viewModel.Navigation.IsSticky.Should().BeTrue();
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.Navigation.OnLiveEdge(viewModel.Navigation.To.AddMinutes(1.0));

		provider.HistoryQueryCount.Should().Be(queriesBefore);
	}

	[AvaloniaFact]
	public void StickyLiveEdgeAdvance_StillShiftsTheScaleWindow()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From, To);
		var toBefore = viewModel.Navigation.To;
		var revisionBefore = viewModel.ScalesRevision;

		viewModel.Navigation.OnLiveEdge(toBefore.AddMinutes(1.0));

		viewModel.Navigation.To.Should().Be(toBefore.AddMinutes(1.0));
		viewModel.ScalesRevision.Should().BeGreaterThan(revisionBefore);
	}

	[AvaloniaFact]
	public void Coordinator_CoarseLayerRealtime_FoldsInsteadOfGrowingColumns()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel(
			realtimeInterval: RealtimeInterval);
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);

		// A coarse (non-Raw) layer folds realtime into the current column instead of appending.
		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		viewModel.Navigation.ActiveLayer.Should().NotBe(AggregationLayer.Raw);
		var columnsBefore = state.Line.SnapshotColumns().Count;

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);

		state.Line.SnapshotColumns().Count.Should().Be(columnsBefore);
	}

	[AvaloniaFact]
	public async Task AReturnToRawWhileTheCoarseReadIsHeld_ReadsRaw()
	{
		await AssertRawIsReadAfterAReturnFromACoarseRead(failTheCoarseRead: false, land: null);
	}

	[AvaloniaFact]
	public async Task AReturnToRawAfterAFailedCoarseRead_ReadsRaw()
	{
		await AssertRawIsReadAfterAReturnFromACoarseRead(failTheCoarseRead: true, land: null);
	}

	[AvaloniaFact]
	public async Task AReturnToRawAfterALandedCoarseRead_ReadsRaw()
	{
		await AssertRawIsReadAfterAReturnFromACoarseRead(
			failTheCoarseRead: false,
			land: (viewModel, provider) => ReleaseAndAwait(
				viewModel, 1, () => provider.HistoryGate.SetResult(EnvelopeEndingAtTheRealtimeEpoch())));
	}

	[AvaloniaFact]
	public async Task AFoldIntoACoarseEnvelopeKeepsThePrefetchedBand()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(RealtimeInterval);
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.Navigation.TrackDataExtents(
			FakeDataProvider.RealtimeEpoch.AddDays(-30.0), FakeDataProvider.RealtimeEpoch);
		await ZoomOutAndIssueTheCoarseRead(viewModel, scheduler, provider);
		await ReleaseAndAwait(
			viewModel, 1, () => provider.HistoryGate.SetResult(EnvelopeEndingAtTheRealtimeEpoch()));
		LandTheMarginReadFromTheReleasedGate(scheduler);
		var columnsBefore = state.Line.SnapshotColumns().Count;
		var queriesBefore = provider.HistoryQueryCount;

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);

		state.Line.SnapshotColumns().Count.Should().Be(columnsBefore, "a coarse view folds the batch");
		provider.HistoryQueryCount.Should().Be(queriesBefore);

		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		viewModel.Navigation.PanBy(-width / 4);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(queriesBefore, "the pan stays inside the coarse band");
	}

	[AvaloniaFact]
	public async Task AnAppendOverRawColumnsKeepsTheRawBand()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(RealtimeInterval);
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		await LoadRawEndingAtTheRealtimeEpoch(viewModel, scheduler, provider);
		state.Line.SnapshotColumns()[^1].X.Should().BeGreaterThan(
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
		var (viewModel, scheduler, _, provider) = CreateViewModel(RealtimeInterval);
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		provider.GatedLayer = AggregationLayer.Raw;
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		provider.HistoryQueryCount.Should().Be(1, "the read is held");

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks * 3);
		var liveColumns = state.Line.SnapshotColumns().ToArray();
		liveColumns[^1].X.Should().BeGreaterThan(LocalTimeAxis.ToAxis(FakeDataProvider.RealtimeEpoch));

		await ReleaseAndAwait(
			viewModel, 2, () => provider.HistoryGate.SetResult(EnvelopeEndingAtTheRealtimeEpoch()));

		state.Line.SnapshotColumns()[^1].X.Should().Be(
			liveColumns[^1].X, "the read's snapshot ends before the live columns");
		state.Line.SnapshotColumns().Should().HaveCount(
			2 + liveColumns.Count(column => column.X > LocalTimeAxis.ToAxis(FakeDataProvider.RealtimeEpoch)));
	}

	[AvaloniaFact]
	public async Task APanIntoThePastDropsTheLiveColumnsTheReadDidNotReach()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(RealtimeInterval);
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		await LoadRawEndingAtTheRealtimeEpoch(viewModel, scheduler, provider);
		state.Line.SnapshotColumns()[^1].X.Should().BeGreaterThan(LocalTimeAxis.ToAxis(FakeDataProvider.RealtimeEpoch));
		var queriesBefore = provider.HistoryQueryCount;

		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		viewModel.Navigation.PanBy(-width * 3);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		await AwaitQueryCount(provider, queriesBefore + 1);
		var bandFrom = provider.LastQueriedFromUtc!.Value;
		var bandTo = provider.LastQueriedToUtc!.Value;
		bandTo.Should().BeBefore(FakeDataProvider.RealtimeEpoch);

		await ReleaseAndAwait(viewModel, 1, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
				[new PenHistoryEnvelope(1, [bandFrom, bandTo], [1.0, 1.0], [1.0, 1.0], [1.0, 1.0])])));

		state.Line.SnapshotColumns().Select(column => column.X).Should().Equal(
			LocalTimeAxis.ToAxis(bandFrom),
			LocalTimeAxis.ToAxis(bandTo));
	}

	[AvaloniaFact]
	public async Task APenAbsentFromTheResultKeepsItsLiveColumns()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(RealtimeInterval);
		viewModel.AddPen(FakeDataProvider.FirstPen);
		var absent = viewModel.AddPen(FakeDataProvider.SecondPen);
		provider.GatedLayer = AggregationLayer.Raw;
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks * 3);
		var liveColumns = absent.Line.SnapshotColumns().ToArray();
		liveColumns.Should().NotBeEmpty();

		await ReleaseAndAwait(
			viewModel, 2, () => provider.HistoryGate.SetResult(EnvelopeEndingAtTheRealtimeEpoch()));

		absent.Line.SnapshotColumns().Should().Equal(liveColumns);
		absent.CurrentValue.Should().Be(liveColumns[^1].Center);
	}

	[AvaloniaFact]
	public async Task ACoarseApplyKeepsTheLiveColumnsAndTheNextFoldWidensTheLastOne()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(RealtimeInterval);
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		await LoadRawEndingAtTheRealtimeEpoch(viewModel, scheduler, provider);
		await ZoomOutAndIssueTheCoarseRead(viewModel, scheduler, provider);
		var lastLiveX = state.Line.SnapshotColumns()[^1].X;

		await ReleaseAndAwait(
			viewModel, 1, () => provider.HistoryGate.SetResult(EnvelopeEndingAtTheRealtimeEpoch()));

		state.Line.SnapshotColumns()[^1].X.Should().Be(lastLiveX);
		var columnsBefore = state.Line.SnapshotColumns().Count;
		var keptColumn = state.Line.SnapshotColumns()[^1];

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);

		state.Line.SnapshotColumns().Should().HaveCount(columnsBefore);
		state.Line.SnapshotColumns()[^1].X.Should().Be(lastLiveX);
		state.Line.SnapshotColumns()[^1].Max.Should().BeGreaterThan(keptColumn.Max);
		state.CurrentValue.Should().Be(state.Line.SnapshotColumns()[^1].Center);
	}

	[AvaloniaFact]
	public async Task AValueAfterACoarseEnvelopeEndingOnAGapTakesTheReading()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(RealtimeInterval);
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		await LoadRawEndingAtTheRealtimeEpoch(viewModel, scheduler, provider);
		await ZoomOutAndIssueTheCoarseRead(viewModel, scheduler, provider);
		var gapUtc = LocalTimeAxis.FromAxis(state.Line.SnapshotColumns()[^1].X).AddMilliseconds(1.0);
		var gapX = LocalTimeAxis.ToAxis(gapUtc);

		await ReleaseAndAwait(viewModel, 1, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
				[new PenHistoryEnvelope(
					1,
					[FakeDataProvider.RealtimeEpoch.AddHours(-1.0), gapUtc],
					[1.0, double.NaN],
					[1.0, double.NaN],
					[1.0, double.NaN])])));
		state.Line.SnapshotColumns()[^1].X.Should().Be(gapX, "the envelope ends on a gap past every live column");
		state.CurrentValue.Should().Be(1.0);

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);

		state.CurrentValue.Should().BeGreaterThan(1.0, "the batch's value takes the reading");
		state.CurrentValue.Should().Be(state.Line.SnapshotColumns()[^1].Center);
		state.Line.SnapshotColumns()[^1].X.Should().BeGreaterThan(gapX);
		state.Line.SnapshotColumns().Should().Contain(column => column.X == gapX && double.IsNaN(column.Center));
	}

	[AvaloniaFact]
	public void Coordinator_RawLayerRealtime_AppendsColumnsAndUpdatesTheCurrentValue()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel(
			realtimeInterval: RealtimeInterval);
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);
		var columnsBefore = state.Line.SnapshotColumns().Count;

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);

		state.CurrentValue.Should().NotBeNull();
		state.Line.SnapshotColumns().Count.Should().BeGreaterThan(columnsBefore);
	}

	// The archive is per-variable and change-based, so a buffer window routinely spans timestamps only one
	// pen sampled; a pen absent from a timestamp is left alone, not appended as null, which would draw a
	// break the archive never recorded.
	[AvaloniaFact]
	public void Coordinator_RealtimeTimestampsOnlyOnePenSampled_BreakNeitherPen()
	{
		var (viewModel, scheduler, coordinator, provider) = CreateViewModel(
			realtimeInterval: RealtimeInterval);
		provider.StaggerRealtimeTimestamps = true;
		var first = viewModel.AddPen(provider.Pens[0]);
		var second = viewModel.AddPen(provider.Pens[1]);
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);

		first.Line.SnapshotColumns().Should().NotBeEmpty();
		second.Line.SnapshotColumns().Should().NotBeEmpty();
		first.Line.SnapshotColumns().Should().NotContain(column => double.IsNaN(column.Center));
		second.Line.SnapshotColumns().Should().NotContain(column => double.IsNaN(column.Center));
	}

	[AvaloniaFact]
	public void EveryPlottable_UsesTheSharedBottomXAxis()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.AddPen(new Pen(2, "Pen 2", ["Group B"], "#00ff00"));

		var bottom = viewModel.Plot.Axes.Bottom;
		foreach (var pen in viewModel.Pens)
		{
			pen.Line.Axes.XAxis.Should().BeSameAs(bottom);
		}
	}

	[AvaloniaFact]
	public void APanInsideThePrefetchedBandIssuesNoHistoryQuery()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var queriesBefore = provider.HistoryQueryCount;

		// Half a window width: the margin the first query fetched still holds every column.
		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-30.0));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(queriesBefore);
	}

	[AvaloniaFact]
	public void AReportedWidthInsideTheDeadbandKeepsThePrefetchedBand()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.ReportDataAreaWidth(700.0);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.ReportDataAreaWidth(704.0);
		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-30.0));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		viewModel.Navigation.TargetColumnCount.Should().Be(512);
		provider.HistoryQueryCount.Should().Be(queriesBefore);
	}

	[AvaloniaFact]
	public void APanPastTheBandLeavesTheAxisUntouchedUntilTheQueryLands()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		var revisionBefore = viewModel.ScalesRevision;
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.Navigation.PanBy(-4 * width);

		viewModel.ScalesRevision.Should().Be(revisionBefore);

		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().BeGreaterThan(queriesBefore);
		viewModel.ScalesRevision.Should().BeGreaterThan(revisionBefore);
	}

	// A query that never landed filled no band, so the pan that follows has to ask again. The gate opening
	// on the request instead of on the result would leave the chart empty until a zoom or a full-window pan.
	[AvaloniaFact]
	public void APanInsideTheBandAfterAFailedQueryAsksAgain()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		provider.FailHistory = true;
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var queriesBefore = provider.HistoryQueryCount;

		// The same half-window pan APanInsideThePrefetchedBandIssuesNoHistoryQuery answers with no query.
		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-30.0));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().BeGreaterThan(queriesBefore);
	}

	// The drag that leaves the fetched band and comes back into it, still moving when the far query lands.
	// The arrived range holds no column the window in view shows, so sizing the axis from it would be the
	// mid-gesture jump the window bound exists to remove; the re-query is what brings the axis back.
	[AvaloniaFact]
	public async Task AResultForAWindowTheDragLeftLeavesTheAxisAndReQueriesTheWindowInView()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		var windowFrom = viewModel.Navigation.From;

		var queriesBefore = provider.HistoryQueryCount;

		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().BeGreaterThan(queriesBefore);
		var queriesAfterFarRead = provider.HistoryQueryCount;

		provider.GatedLayer = null;
		viewModel.Navigation.PanBy(4 * width);
		var revisionBefore = viewModel.ScalesRevision;

		viewModel.Navigation.From.Should().Be(windowFrom);
		provider.HistoryQueryCount.Should().Be(queriesAfterFarRead);

		await ReleaseAndAwait(viewModel, 1, () => provider.HistoryGate.SetResult(StaleEnvelopes()));

		viewModel.ScalesRevision.Should().Be(revisionBefore);

		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		await AwaitQueryCount(provider, queriesAfterFarRead + 1);
		provider.LastQueriedFromUtc.Should().Be(windowFrom);
		provider.LastQueriedToUtc.Should().Be(viewModel.Navigation.To + width, "the window ends at the newest sample");
	}

	[AvaloniaFact]
	public void AGestureEndingBackInsideTheFetchedBandCancelsTheReadItLeft()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;

		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var farRead = provider.LastQueriedCancellationToken;
		var queriesAfterFarRead = provider.HistoryQueryCount;

		viewModel.Navigation.PanBy(4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		farRead.IsCancellationRequested.Should().BeTrue();
		provider.HistoryQueryCount.Should().Be(queriesAfterFarRead);
	}

	// The apply throws after the band is loaded and the gate open, so the band is drawn though the apply was
	// reported as failed.
	[AvaloniaFact]
	public async Task AGestureEndingInsideABandWhoseApplyThrewAfterTheLoadCancelsTheReadItLeft()
	{
		var panel = new MessagePanelViewModel();
		var (viewModel, scheduler, _, provider) = CreateViewModel(messagePanel: panel);
		viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.Navigation.TrackDataExtents(From.AddDays(-30.0), To);
		viewModel.RequestInitialHistory();
		var subscriberThrows = true;
		using var subscriber = viewModel.RedrawRequested.Subscribe(_ =>
		{
			if (subscriberThrows)
			{
				subscriberThrows = false;

				throw new InvalidOperationException("A subscriber rejected the redraw.");
			}
		});
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		await AwaitPanelEntries(panel, 1);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;

		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var farRead = provider.LastQueriedCancellationToken;
		var queriesAfterFarRead = provider.HistoryQueryCount;

		viewModel.Navigation.PanBy(4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		farRead.IsCancellationRequested.Should().BeTrue();
		provider.HistoryQueryCount.Should().Be(queriesAfterFarRead);
	}

	// The read for pen 1 alone is admitted at 150 ms; pen 2 arrives at 300 ms and the 400 ms sample tick parks
	// its read behind the first, so that read lands with pen 2 already shown. Its range covers the window in
	// view but holds no row of pen 2, and the pan that follows ends inside it while pen 2's read still runs.
	[AvaloniaFact]
	public async Task AReadForAnOlderPenSetLeavesTheNewPensReadRunning()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.Navigation.TrackDataExtents(From.AddDays(-30.0), To);
		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);
		var queriesBefore = provider.HistoryQueryCount;

		var penOneRead = provider.HistoryGate;
		provider.HistoryGate = new();
		viewModel.AddPen(FakeDataProvider.SecondPen);
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks + 1);

		await ReleaseAndAwait(viewModel, 1, () => penOneRead.SetResult(StaleEnvelopes()));
		await AwaitQueryCount(provider, queriesBefore + 1);
		var newPensRead = provider.LastQueriedCancellationToken;
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		newPensRead.IsCancellationRequested.Should().BeFalse("the landed read asks again for the read already running");

		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-30.0));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		await AwaitQueryCount(provider, queriesBefore + 2);

		provider.LastQueriedPenIds.Should().BeEquivalentTo([1, 2]);
		provider.LastQueriedCancellationToken.IsCancellationRequested.Should().BeFalse();

		await ReleaseAndAwait(viewModel, 1, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
			[
				new PenHistoryEnvelope(1, [From, To], [7.0, 7.0], [7.0, 7.0], [7.0, 7.0]),
				new PenHistoryEnvelope(2, [From, To], [7.0, 7.0], [7.0, 7.0], [7.0, 7.0])
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
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);

		var queriesBefore = provider.HistoryQueryCount;
		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.ApplyCatalogue([catalogue[0]]);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		provider.HistoryQueryCount.Should().BeGreaterThan(queriesBefore);
		var removalQueries = provider.HistoryQueryCount;

		viewModel.ApplyCatalogue(catalogue);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		await AwaitQueryCount(provider, removalQueries + 1);

		provider.LastQueriedPenIds.Should().Equal(1, 2);

		await ReleaseAndAwait(viewModel, 1, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
			[
				new PenHistoryEnvelope(1, [From, To], [7.0, 7.0], [7.0, 7.0], [7.0, 7.0]),
				new PenHistoryEnvelope(2, [From, To], [7.0, 7.0], [7.0, 7.0], [7.0, 7.0])
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
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);

		var queriesBefore = provider.HistoryQueryCount;
		provider.FailHistory = true;
		viewModel.ApplyCatalogue([catalogue[0]]);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		provider.HistoryQueryCount.Should().BeGreaterThan(queriesBefore);
		var removalQueries = provider.HistoryQueryCount;

		provider.FailHistory = false;
		await ReleaseAndAwait(viewModel, 2, () =>
		{
			viewModel.ApplyCatalogue(catalogue);
			scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		});

		provider.HistoryQueryCount.Should().BeGreaterThan(removalQueries);
		provider.LastQueriedPenIds.Should().Equal(1, 2);
		viewModel.FindPen(2)!.CurrentValue.Should().Be(FakeDataProvider.DefaultCenter);
	}

	[AvaloniaFact]
	public async Task AWindowWhoseApplyThrewIsReadAgainOnTheNextMove()
	{
		var panel = new MessagePanelViewModel();
		var (viewModel, scheduler, _, provider) = CreateViewModel(messagePanel: panel);
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.Navigation.TrackDataExtents(From.AddDays(-30.0), To);
		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.GatedLayer = null;
		provider.HistoryGate.SetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>([UnloadableEnvelope(1)]));
		await AwaitPanelEntries(panel, 1);

		var queriesBefore = provider.HistoryQueryCount;

		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-1.0));
		await ReleaseAndAwait(viewModel, 1, () => scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1));

		provider.HistoryQueryCount.Should().BeGreaterThan(queriesBefore);
		state.CurrentValue.Should().Be(FakeDataProvider.DefaultCenter);
	}

	// The far result loads pen 1 and throws on pen 2, so pen 1 holds rows of a band nothing is fetched for.
	[AvaloniaFact]
	public async Task AnApplyThatThrowsPartWayLeavesNoBandFetched()
	{
		var panel = new MessagePanelViewModel();
		var (viewModel, scheduler, _, provider) = CreateViewModel(messagePanel: panel);
		viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.AddPen(FakeDataProvider.SecondPen);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
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

		var queriesBefore = provider.HistoryQueryCount;

		await ReleaseAndAwait(viewModel, 2, () =>
		{
			viewModel.Navigation.PanBy(4 * width);
			scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		});

		provider.HistoryQueryCount.Should().BeGreaterThan(queriesBefore);
		provider.LastQueriedFromUtc.Should().Be(windowFrom);
		viewModel.FindPen(1)!.CurrentValue.Should().Be(FakeDataProvider.DefaultCenter);
	}

	// Skipping the axis here freezes it with no path back, since the gate that would ask again opens on a
	// result. APanInsideTheBandAfterAFailedQueryAsksAgain pins the ask.
	[AvaloniaFact]
	public void AFailedQueryOutsideTheBandStillAppliesTheAxis()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		var revisionBefore = viewModel.ScalesRevision;
		var queriesBefore = provider.HistoryQueryCount;
		provider.FailHistory = true;

		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().BeGreaterThan(queriesBefore);
		viewModel.ScalesRevision.Should().BeGreaterThan(revisionBefore);
	}

	[AvaloniaFact]
	public void APanPastTheBandReadsTheVisibleWindowFirstAndItsMarginsAfterIt()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-60.0));
		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);

		var from = viewModel.Navigation.From;
		var to = viewModel.Navigation.To;
		var width = to - from;
		ReadsSince(provider, queriesBefore).Should().Equal(
			(from, to, HistoryColumnTarget.MaxColumns),
			(from - width, to + width, ExpandedColumnFactor * HistoryColumnTarget.MaxColumns));
	}

	// The read the drag's first move issued lands after the drag moved on past its window: the strip the drag
	// moved onto lies outside that read, and the columns in hand there stay drawn.
	[AvaloniaFact]
	public async Task ADragPastTheBandKeepsTheColumnsInViewWhenItsReadLands()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.BeginDrag();

		viewModel.Navigation.PanBy(-0.6 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var read = provider.ChartHistoryQueries[^1];
		viewModel.Navigation.PanBy(-0.2 * width);
		provider.GatedLayer = null;
		await ReleaseAndAwait(viewModel, 1, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
				[new PenHistoryEnvelope(1, [read.FromUtc, read.ToUtc], [7.0, 7.0], [7.0, 7.0], [7.0, 7.0])])));

		var columns = state.Line.SnapshotColumns();
		columns[0].X.Should().BeLessThanOrEqualTo(LocalTimeAxis.ToAxis(viewModel.Navigation.From));
		columns[^1].X.Should().BeGreaterThanOrEqualTo(LocalTimeAxis.ToAxis(viewModel.Navigation.To));
		viewModel.EndDrag();
	}

	// The read of the first wheel notch lands after the second: it spans the window of the first notch alone,
	// and the columns in hand past both its sides still draw, and read, the wider window in view.
	[AvaloniaFact]
	public async Task AZoomOutReadLandingWhileTheZoomGoesOnKeepsTheColumnsInView()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		viewModel.Navigation.PanBy(-width);
		scheduler.AdvanceBy(TimeSpan.FromSeconds(5.0).Ticks);
		var anchor = viewModel.Navigation.From + (width / 2);
		provider.GatedLayer = viewModel.Navigation.ActiveLayer;

		viewModel.Navigation.ZoomAt(1.25, anchor);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var read = provider.ChartHistoryQueries[^1];
		read.TargetColumnCount.Should().Be(HistoryColumnTarget.MaxColumns, "the first notch reads its window alone");
		viewModel.Navigation.ZoomAt(1.25, anchor);
		provider.GatedLayer = null;
		await ReleaseAndAwait(viewModel, 1, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
				[new PenHistoryEnvelope(1, [read.FromUtc, read.ToUtc], [7.0, 7.0], [7.0, 7.0], [7.0, 7.0])])));

		var columns = state.Line.SnapshotColumns();
		columns[0].X.Should().BeLessThanOrEqualTo(LocalTimeAxis.ToAxis(viewModel.Navigation.From));
		columns[^1].X.Should().BeGreaterThanOrEqualTo(LocalTimeAxis.ToAxis(viewModel.Navigation.To));
		viewModel.MoveCursor(viewModel.Navigation.From.AddMinutes(1.0));
		viewModel.CursorValues[1].Should().NotBeNull("the cursor reads the columns drawn past the read");
	}

	// A drag right after a zoom: the zoom's read lands while the drag holds the window a fifth of a width
	// off the zoomed one, and the columns the range before the zoom drew still fill that strip.
	[AvaloniaFact]
	public async Task AZoomsReadLandingDuringTheDragThatFollowsKeepsTheColumnsInView()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		viewModel.Navigation.SetSticky(false);
		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.Navigation.ZoomAt(0.5, From.AddMinutes(30.0));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var read = provider.ChartHistoryQueries[^1];
		read.TargetColumnCount.Should().Be(HistoryColumnTarget.MaxColumns, "the zoom reads its window alone");

		viewModel.BeginDrag();
		viewModel.Navigation.PanBy(-0.2 * (viewModel.Navigation.To - viewModel.Navigation.From));
		provider.GatedLayer = null;
		await ReleaseAndAwait(viewModel, 1, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
				[new PenHistoryEnvelope(1, [read.FromUtc, read.ToUtc], [7.0, 7.0], [7.0, 7.0], [7.0, 7.0])])));

		var columns = state.Line.SnapshotColumns();
		columns[0].X.Should().BeLessThanOrEqualTo(LocalTimeAxis.ToAxis(viewModel.Navigation.From));
		columns[^1].X.Should().BeGreaterThanOrEqualTo(LocalTimeAxis.ToAxis(viewModel.Navigation.To));
		viewModel.EndDrag();
	}

	// A batch folding over the Raw columns while the read past the Raw ceiling is held shuts the gate. The next
	// notch widens the window past that read, and the Raw columns still fill the strip it does not reach.
	[AvaloniaFact]
	public async Task AZoomOutAcrossTheRawCeilingKeepsTheRawColumnsPastTheCoarseReadAfterABatch()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(RealtimeInterval);
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(
			viewModel, scheduler, FakeDataProvider.RealtimeEpoch.AddDays(-30.0), FakeDataProvider.RealtimeEpoch);
		ZoomOutToOneNotchBelowTheRawCeiling(viewModel);
		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);
		provider.GatedLayer = AggregationLayer.Minute;

		viewModel.Navigation.ZoomAt(1.25, viewModel.Navigation.To);
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Minute);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var read = provider.ChartHistoryQueries[^1];
		read.Layer.Should().Be(AggregationLayer.Minute, "the read past the ceiling is held");
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);
		viewModel.Navigation.ZoomAt(1.25, viewModel.Navigation.To);
		provider.GatedLayer = null;
		await ReleaseAndAwait(viewModel, 1, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
				[new PenHistoryEnvelope(1, [read.FromUtc, read.ToUtc], [7.0, 7.0], [7.0, 7.0], [7.0, 7.0])])));

		state.Line.SnapshotColumns()[0].X.Should().BeLessThanOrEqualTo(
			LocalTimeAxis.ToAxis(viewModel.Navigation.From));
	}

	// The read issued before pen 2 was switched on lands without it and shuts the gate. The read of both pens
	// that follows lands after one more notch, and pen 1's columns from before still fill the strips past it.
	[AvaloniaFact]
	public async Task AReadLandingWhileASwitchOnShutTheGateKeepsTheColumnsInView()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.AddPen(FakeDataProvider.SecondPen with { EnabledOnStart = false });
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		viewModel.Navigation.PanBy(-width);
		scheduler.AdvanceBy(TimeSpan.FromSeconds(5.0).Ticks);
		var anchor = viewModel.Navigation.From + (width / 2);
		var firstGate = provider.HistoryGate;
		provider.GatedLayer = viewModel.Navigation.ActiveLayer;

		viewModel.Navigation.ZoomAt(1.25, anchor);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var first = provider.ChartHistoryQueries[^1];
		first.PenIds.Should().Equal(1);
		viewModel.SetPenVisibility(2, true);
		provider.HistoryGate = new();
		await ReleaseAndAwait(viewModel, 1, () => firstGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
				[new PenHistoryEnvelope(1, [first.FromUtc, first.ToUtc], [7.0, 7.0], [7.0, 7.0], [7.0, 7.0])])));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var second = provider.ChartHistoryQueries[^1];
		second.PenIds.Should().Equal(1, 2);
		viewModel.Navigation.ZoomAt(1.25, anchor);
		provider.GatedLayer = null;
		await ReleaseAndAwait(viewModel, 1, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
			[
				new PenHistoryEnvelope(1, [second.FromUtc, second.ToUtc], [8.0, 8.0], [8.0, 8.0], [8.0, 8.0]),
				new PenHistoryEnvelope(2, [second.FromUtc, second.ToUtc], [8.0, 8.0], [8.0, 8.0], [8.0, 8.0])
			])));

		var columns = state.Line.SnapshotColumns();
		columns[0].X.Should().BeLessThanOrEqualTo(LocalTimeAxis.ToAxis(viewModel.Navigation.From));
		columns[^1].X.Should().BeGreaterThanOrEqualTo(LocalTimeAxis.ToAxis(viewModel.Navigation.To));
	}

	// Pen 2 still holds its columns of the range before the pan when it is switched on during the pan's read, so
	// the envelopes in hand span two ranges, and the read of both pens keeps none of pen 2's columns off its own.
	[AvaloniaFact]
	public async Task AReadOffTheTimeAPenWasReadOverKeepsNoneOfItsColumns()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		var second = viewModel.AddPen(FakeDataProvider.SecondPen);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		viewModel.SetPenVisibility(2, false);
		var firstGate = provider.HistoryGate;
		provider.GatedLayer = viewModel.Navigation.ActiveLayer;

		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var far = provider.ChartHistoryQueries[^1];
		far.PenIds.Should().Equal(1);
		viewModel.SetPenVisibility(2, true);
		provider.HistoryGate = new();
		await ReleaseAndAwait(viewModel, 1, () => firstGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
				[new PenHistoryEnvelope(1, [far.FromUtc, far.ToUtc], [7.0, 7.0], [7.0, 7.0], [7.0, 7.0])])));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var both = provider.ChartHistoryQueries[^1];
		both.PenIds.Should().Equal(1, 2);
		provider.GatedLayer = null;
		await ReleaseAndAwait(viewModel, 1, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
			[
				new PenHistoryEnvelope(1, [both.FromUtc, both.ToUtc], [8.0, 8.0], [8.0, 8.0], [8.0, 8.0]),
				new PenHistoryEnvelope(2, [both.FromUtc, both.ToUtc], [8.0, 8.0], [8.0, 8.0], [8.0, 8.0])
			])));

		second.Line.SnapshotColumns().Select(column => column.X).Should().Equal(
			LocalTimeAxis.ToAxis(both.FromUtc),
			LocalTimeAxis.ToAxis(both.ToUtc));
	}

	[AvaloniaFact]
	public void TheMarginReadReplacesTheColumnsKeptAroundTheVisibleRead()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		viewModel.Navigation.SetSticky(false);
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.Navigation.ZoomAt(0.5, From.AddMinutes(30.0));
		scheduler.AdvanceBy(TimeSpan.FromSeconds(5.0).Ticks);

		var margins = provider.ChartHistoryQueries[^1];
		provider.HistoryQueryCount.Should().Be(queriesBefore + 2);
		state.Line.SnapshotColumns().Select(column => column.X).Should().Equal(
			LocalTimeAxis.ToAxis(margins.FromUtc),
			LocalTimeAxis.ToAxis(margins.ToUtc));
	}

	[AvaloniaFact]
	public void PreRenderDataArea_QueriesTheMaximumColumnCountWithItsMargin()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);

		viewModel.Navigation.TargetColumnCount.Should().Be(HistoryColumnTarget.MaxColumns);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);

		provider.LastQueriedTargetColumnCount.Should()
			.Be(ExpandedColumnFactor * HistoryColumnTarget.MaxColumns);
	}

	[AvaloniaTheory]
	[InlineData(0.0)]
	[InlineData(-10.0)]
	[InlineData(double.NaN)]
	public void CollapsedCanvas_KeepsTheLastReportedWidth(double collapsedWidth)
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.ReportDataAreaWidth(700.0);

		viewModel.ReportDataAreaWidth(collapsedWidth);

		viewModel.Navigation.TargetColumnCount.Should().Be(512);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		provider.LastQueriedTargetColumnCount.Should().Be(ExpandedColumnFactor * 700);
	}

	[AvaloniaFact]
	public void ReportedWidthChangingTheLayer_KeepsTheWindowAndReQueriesAtTheNewLayerAndColumnCount()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		var currentWidth = viewModel.Navigation.To - viewModel.Navigation.From;

		// Roughly four hours: 7 s per column at 2048 columns, finer than the minute layer's 15 s spacing,
		// but 56 s per column at 256, where the minute layer fills every column.
		viewModel.Navigation.ZoomAt(TimeSpan.FromHours(4.0) / currentWidth, viewModel.Navigation.To);
		viewModel.ReportDataAreaWidth(2048.0);
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		provider.LastQueriedLayer.Should().Be(AggregationLayer.Raw);
		var fromBefore = viewModel.Navigation.From;
		var toBefore = viewModel.Navigation.To;

		viewModel.ReportDataAreaWidth(256.0);

		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Minute);
		viewModel.Navigation.From.Should().Be(fromBefore);
		viewModel.Navigation.To.Should().Be(toBefore);

		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);

		provider.LastQueriedLayer.Should().Be(AggregationLayer.Minute);
		provider.LastQueriedTargetColumnCount.Should().Be(ExpandedColumnFactor * 256);
	}

	[AvaloniaFact]
	public async Task WidthReportedWhileTheInitialQueryIsInFlight_CancelsItAndAppliesTheReportedResolution()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		provider.GatedLayer = AggregationLayer.Raw;

		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		provider.HistoryQueryCount.Should().Be(1);
		var initialRead = provider.LastQueriedCancellationToken;

		viewModel.ReportDataAreaWidth(256.0);
		provider.GatedLayer = null;
		await ReleaseAndAwait(viewModel, 2, () => scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1));

		initialRead.IsCancellationRequested.Should().BeTrue();
		provider.HistoryQueryCount.Should().Be(2);
		provider.LastQueriedTargetColumnCount.Should().Be(
			(1 + HistoryPrefetch.MarginWindows) * 256, "the live edge's first read carries its right margin");
		state.Line.SnapshotColumns().Should().HaveCount(2);
		state.CurrentValue.Should().Be(FakeDataProvider.DefaultCenter);
	}

	[AvaloniaFact]
	public void EnteringDeltaMode_SwitchesLeftButtonToolToDeltaPlacement()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.ActiveLeftButtonTool.Should().Be(LeftButtonTool.Pan);

		viewModel.SetDeltaModeEnabled(true);

		viewModel.IsDeltaModeEnabled.Should().BeTrue();
		viewModel.ActiveLeftButtonTool.Should().Be(LeftButtonTool.DeltaPlacement);
	}

	[AvaloniaFact]
	public void DeltaMode_TwoClicks_PlaceBothCursorsAndSurfaceDeltaTimeAndActivePenDeltaY()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From, To);
		viewModel.SetDeltaModeEnabled(true);

		// The fake answers one column per edge of the prefetched range, a window width past the visible end.
		var historyEnd = To.AddHours(1.0);

		viewModel.PlaceDeltaCursor(From);
		viewModel.PlaceDeltaCursor(historyEnd);

		viewModel.DeltaFirstCursor.Should().Be(From);
		viewModel.DeltaSecondCursor.Should().Be(historyEnd);
		viewModel.DeltaReadout.Should().NotBeNull();
		viewModel.DeltaReadout!.DeltaTime.Should().Be(historyEnd - From);
		viewModel.DeltaReadout.DeltaY.Should().Be(1.0);
		viewModel.DeltaReadoutText.Should()
			.Contain(Resources.DeltaTimeLabel)
			.And.Contain(Resources.DeltaValueLabel);
	}

	[AvaloniaFact]
	public void DeltaMode_RendersTheDeltaYUnderTheActivePensMask_AndFollowsAMaskTheCatalogueChanges()
	{
		using var culture = new CultureScope("en-US");
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen with { Format = "0.00" });
		LoadInitialHistory(viewModel, scheduler, From, To);
		viewModel.SetDeltaModeEnabled(true);
		viewModel.PlaceDeltaCursor(From);
		viewModel.PlaceDeltaCursor(To.AddHours(1.0));
		viewModel.DeltaReadoutText.Should().EndWith($"{Resources.DeltaValueLabel} 1.00");
		using var monitor = viewModel.Monitor();

		viewModel.ApplyCatalogue([FakeDataProvider.FirstPen with { Format = "0.000" }]);

		monitor.Should().RaisePropertyChangeFor(chart => chart.DeltaReadoutText);
		viewModel.DeltaReadoutText.Should().EndWith($"{Resources.DeltaValueLabel} 1.000");
	}

	[AvaloniaFact]
	public void DeltaMode_SwitchingTheActivePen_RendersItsDeltaYUnderItsOwnMask()
	{
		using var culture = new CultureScope("en-US");
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen with { Format = "0.00" });
		viewModel.AddPen(FakeDataProvider.SecondPen with { Format = "0.0000" });
		LoadInitialHistory(viewModel, scheduler, From, To);
		viewModel.SetDeltaModeEnabled(true);
		viewModel.PlaceDeltaCursor(From);
		viewModel.PlaceDeltaCursor(To.AddHours(1.0));
		using var monitor = viewModel.Monitor();

		viewModel.SetActivePen(FakeDataProvider.SecondPen.PenId);

		monitor.Should().RaisePropertyChangeFor(chart => chart.DeltaReadoutText);
		viewModel.DeltaReadoutText.Should().MatchRegex($@"{Resources.DeltaValueLabel} -?\d+\.\d{{4}}$");
	}

	[AvaloniaFact]
	public void ExitingDeltaMode_ReturnsToPanAndClearsCursors()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From, To);
		viewModel.SetDeltaModeEnabled(true);
		viewModel.PlaceDeltaCursor(From);
		viewModel.PlaceDeltaCursor(To);

		viewModel.SetDeltaModeEnabled(false);

		viewModel.IsDeltaModeEnabled.Should().BeFalse();
		viewModel.ActiveLeftButtonTool.Should().Be(LeftButtonTool.Pan);
		viewModel.DeltaFirstCursor.Should().BeNull();
		viewModel.DeltaSecondCursor.Should().BeNull();
		viewModel.DeltaReadout.Should().BeNull();
		viewModel.DeltaReadoutText.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void HoverDuringDrag_DoesNotPublishTheTraceCursor()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From, To);

		viewModel.BeginDrag();
		viewModel.MoveCursor(From.AddMinutes(30.0));

		viewModel.IsDragging.Should().BeTrue();
		viewModel.CursorTime.Should().BeNull();
		viewModel.CursorValues.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void HoverAfterDragEnds_PublishesTheTraceCursorAgain()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From, To);

		viewModel.BeginDrag();
		viewModel.EndDrag();
		viewModel.MoveCursor(From.AddMinutes(30.0));

		viewModel.IsDragging.Should().BeFalse();
		viewModel.CursorTime.Should().Be(From.AddMinutes(30.0));
	}

	[AvaloniaFact]
	public void RequestInitialHistory_LoadsEnvelopes_ThenALaterGestureResultSupersedesIt()
	{
		// One history path: the initial load applies first; a later debounced gesture re-query supersedes it.
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		provider.LayerCenterOverrides[AggregationLayer.Minute] = 5.0;

		LoadInitialHistory(viewModel, scheduler, From, To);
		viewModel.FindPen(1)!.CurrentValue.Should().Be(2.0);
		viewModel.FindPen(1)!.Line.SnapshotColumns().Should().HaveCount(2);

		// A zoom-out gesture re-queries the coarser layer through the debouncer.
		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Minute);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.LastQueriedLayer.Should().Be(AggregationLayer.Minute);
		viewModel.FindPen(1)!.CurrentValue.Should().Be(5.0);
	}

	[AvaloniaFact]
	public async Task StaleInitialHistory_IsCancelledByAZoomEndingOnAnotherWindow()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);

		provider.GatedLayer = AggregationLayer.Raw;
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var initialRead = provider.LastQueriedCancellationToken;

		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		viewModel.Navigation.ActiveLayer.Should().NotBe(AggregationLayer.Raw);
		await ReleaseAndAwait(viewModel, 2, () => scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1));

		initialRead.IsCancellationRequested.Should().BeTrue();
		provider.HistoryQueryCount.Should().Be(2);
		viewModel.FindPen(1)!.CurrentValue.Should().Be(2.0);
	}

	[AvaloniaFact]
	public void DisposingTheChart_CancelsTheHistoryReadInFlight_AndALateResultAppliesNothing()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		var pen = viewModel.AddPen(FakeDataProvider.FirstPen);
		provider.GatedLayer = AggregationLayer.Raw;

		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var read = provider.LastQueriedCancellationToken;
		read.IsCancellationRequested.Should().BeFalse();

		viewModel.Dispose();

		read.IsCancellationRequested.Should().BeTrue();

		var release = () => provider.HistoryGate.SetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
		[
			new PenHistoryEnvelope(1, [From, To], [99.0, 99.0], [99.0, 99.0], [99.0, 99.0])
		]));

		release.Should().NotThrow();
		pen.CurrentValue.Should().BeNull();
	}

	[AvaloniaFact]
	public void History_OmittingARequestedPen_ClearsThatPensCurve()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.AddPen(FakeDataProvider.SecondPen);

		LoadInitialHistory(viewModel, scheduler, From, To);
		viewModel.FindPen(2)!.Line.SnapshotColumns().Should().HaveCount(2);

		// The next window holds no row for pen 2, so the provider answers with no envelope for it at all.
		provider.OmittedPenIds.Add(2);
		viewModel.Navigation.ZoomAt(48.0, viewModel.Navigation.To);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		viewModel.FindPen(2)!.Line.SnapshotColumns().Should().BeEmpty();
		viewModel.FindPen(2)!.CurrentValue.Should().BeNull();
		viewModel.FindPen(1)!.Line.SnapshotColumns().Should().HaveCount(2);
	}

	[AvaloniaFact]
	public async Task History_PenAddedWhileTheQueryWasInFlight_KeepsItsCurve()
	{
		// What the request asked for, not the current pen dictionary, decides what is cleared: pen 2 is
		// added after the request was issued, so a result omitting it says nothing about it. The gate's
		// continuation runs on the thread pool, so the apply is awaited rather than assumed inline.
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		provider.GatedLayer = AggregationLayer.Raw;
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		var lateState = viewModel.AddPen(FakeDataProvider.SecondPen);
		lateState.LoadHistory(new PenHistoryEnvelope(2, [From, To], [4.0, 4.0], [4.0, 4.0], [4.0, 4.0]), To);

		await ReleaseAndAwait(viewModel, 2, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
			[
				new PenHistoryEnvelope(1, [From, To], [1.0, 2.0], [1.0, 2.0], [1.0, 2.0])
			])));

		viewModel.FindPen(1)!.CurrentValue.Should().Be(2.0);
		lateState.Line.SnapshotColumns().Should().HaveCount(2);
		lateState.CurrentValue.Should().Be(4.0);
	}

	[AvaloniaFact]
	public void AFiftyPenCatalogueWithEightSwitchedOn_ReadsOnlyThoseEight()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(_fiftyPens);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;

		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);

		provider.ChartHistoryQueries.Should().HaveCount(4, "each window is read, then its margins");
		provider.ChartHistoryQueries.Should().AllSatisfy(query => query.PenIds.Should().Equal(_switchedOnPenIds));
	}

	// Pen 6 holds the curve of the window it was hidden over, which the read of the pens shown then moved past.
	[AvaloniaFact]
	public async Task SwitchingAPenOn_ReadsItAloneOverTheRangeInHandAndClearsItsCurveUntilTheReadLands()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(_fiftyPens);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		viewModel.SetPenVisibility(6, false);
		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var rangeInHand = provider.ChartHistoryQueries[^1];
		var switchedOn = viewModel.FindPen(6)!;
		switchedOn.Line.SnapshotColumns().Should().HaveCount(2, "the pen keeps the curve it was hidden with");
		var queriesBefore = provider.HistoryQueryCount;
		provider.GatedLayer = viewModel.Navigation.ActiveLayer;

		viewModel.SetPenVisibility(6, true);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(queriesBefore + 1);
		provider.LastQueriedPenIds.Should().Equal(6);
		provider.LastQueriedFromUtc.Should().Be(rangeInHand.FromUtc);
		provider.LastQueriedToUtc.Should().Be(rangeInHand.ToUtc);
		provider.LastQueriedLayer.Should().Be(rangeInHand.Layer);
		switchedOn.Line.SnapshotColumns().Should().BeEmpty();
		viewModel.MoveCursor(viewModel.Navigation.From.AddMinutes(30.0));
		viewModel.CursorValues.Should().NotContainKey(6);

		provider.GatedLayer = null;
		await ReleaseAndAwait(viewModel, 1, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
				[
					new PenHistoryEnvelope(
						6,
						[rangeInHand.FromUtc, rangeInHand.ToUtc],
						[7.0, 7.0],
						[7.0, 7.0],
						[7.0, 7.0])
				])));

		switchedOn.Line.SnapshotColumns().Should().HaveCount(2);
		switchedOn.CurrentValue.Should().Be(7.0);
		var stayedOn = viewModel.FindPen(12)!;
		stayedOn.Line.SnapshotColumns().Should().HaveCount(2, "a pen the result does not name keeps its curve");
		stayedOn.CurrentValue.Should().Be(FakeDataProvider.DefaultCenter);

		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);

		provider.HistoryQueryCount.Should().Be(queriesBefore + 2, "the margins of the window are read once");
		provider.LastQueriedPenIds.Should().Equal([.. _switchedOnPenIds]);
		provider.LastQueriedTargetColumnCount.Should()
			.Be(ExpandedColumnFactor * HistoryColumnTarget.MaxColumns);
	}

	// The read in flight names the pens shown before the switch, so the read the switch asks for over the new
	// window supersedes it, and meanwhile the pen draws nothing from the range it was hidden over.
	[AvaloniaFact]
	public async Task SwitchingAPenOnWhileAGestureReadIsInFlight_ReadsEveryVisiblePenOverTheNewWindow()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(_fiftyPens);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		viewModel.SetPenVisibility(6, false);
		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var switchedOn = viewModel.FindPen(6)!;
		switchedOn.Line.SnapshotColumns().Should().HaveCount(2, "the pen keeps the curve it was hidden with");
		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var gestureRead = provider.ChartHistoryQueries[^1];
		gestureRead.PenIds.Should().NotContain(6);
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.SetPenVisibility(6, true);

		switchedOn.Line.SnapshotColumns().Should().BeEmpty();
		provider.GatedLayer = null;
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		await AwaitQueryCount(provider, queriesBefore + 1);
		gestureRead.CancellationToken.IsCancellationRequested.Should().BeTrue();
		provider.LastQueriedPenIds.Should().Equal(_switchedOnPenIds);
		provider.LastQueriedFromUtc.Should().Be(viewModel.Navigation.From);
		provider.LastQueriedToUtc.Should().Be(viewModel.Navigation.To);
	}

	// The catalogue read drops the range in hand, and the pens read for it still lack pen 6.
	[AvaloniaFact]
	public void SwitchingAPenOnWithNoRangeInHand_ClearsTheCurveItWasHiddenWith()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(_fiftyPens);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		viewModel.SetPenVisibility(6, false);
		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);
		var switchedOn = viewModel.FindPen(6)!;
		switchedOn.Line.SnapshotColumns().Should().HaveCount(2, "the pen keeps the curve it was hidden with");
		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.ApplyCatalogue([.. _fiftyPens, new Pen(51, "Pen 51", ["Group A"], "#ff0000", EnabledOnStart: false)]);

		viewModel.SetPenVisibility(6, true);

		switchedOn.Line.SnapshotColumns().Should().BeEmpty();
		switchedOn.CurrentValue.Should().BeNull();
	}

	// The failed read leaves the pen unread for the range in hand, so the next move asks for it again.
	[AvaloniaFact]
	public void AFailedSwitchOnReadReachesThePanelAndTheNextMoveReadsThePenAgain()
	{
		var panel = new MessagePanelViewModel();
		var (viewModel, scheduler, _, provider) = CreateViewModel(messagePanel: panel);
		viewModel.ApplyCatalogue(_fiftyPens);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		provider.FailHistory = true;

		viewModel.SetPenVisibility(1, true);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		panel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(FakeDataProvider.UnreachableArchive()));
		viewModel.FindPen(1)!.Line.SnapshotColumns().Should().BeEmpty();
		var queriesBefore = provider.HistoryQueryCount;
		provider.FailHistory = false;

		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-30.0));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(queriesBefore + 1);
		provider.LastQueriedPenIds.Should().Equal(1);
		viewModel.FindPen(1)!.CurrentValue.Should().Be(FakeDataProvider.DefaultCenter);
	}

	// A resize inside the deadband moves the reported width and keeps the range in hand, so the pen switched on
	// is read at the column count of the pens it joins and bins on their grid.
	[AvaloniaFact]
	public void ASwitchedOnPenIsReadAtTheColumnCountOfTheRangeItJoins()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(_fiftyPens);
		viewModel.ReportDataAreaWidth(700.0);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		viewModel.ReportDataAreaWidth(704.0);

		viewModel.SetPenVisibility(1, true);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.LastQueriedPenIds.Should().Equal(1);
		provider.LastQueriedTargetColumnCount.Should().Be(ExpandedColumnFactor * 700);
	}

	// The count reaches the ladder on the catalogue apply itself, so a start with many pens on reads the layer
	// the shrunk ceilings give from its first read.
	[AvaloniaFact]
	public void ACatalogueShowingTwiceTheReferencePenCount_IsReadAtTheCoarserLayer()
	{
		const int ColumnCount = 256;
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.ReportDataAreaWidth(ColumnCount);
		var rawCeiling = TimeSpan.FromSeconds(15.0) * ColumnCount;
		(viewModel.Navigation.To - viewModel.Navigation.From).Should().BeLessThan(rawCeiling)
			.And.BeGreaterThan(rawCeiling / 2.0 * 1.1);
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);

		viewModel.ApplyCatalogue([
			.. Enumerable.Range(1, 2 * ChartNavigationController.ReferencePenCount).Select(penId => new Pen(
				penId, $"Pen {penId}", ["Group A"], "#ff0000", EnabledOnStart: true))
		]);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);

		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Minute);
		provider.ChartHistoryQueries.Should().NotBeEmpty()
			.And.AllSatisfy(query => query.Layer.Should().Be(AggregationLayer.Minute));
	}

	[AvaloniaFact]
	public void SwitchingAPenOff_ReadsNothing()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(_fiftyPens);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.SetPenVisibility(6, false);
		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);

		provider.HistoryQueryCount.Should().Be(queriesBefore);
	}

	[AvaloniaFact]
	public void AGestureAfterASwitchOn_ReadsEveryVisiblePen()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(_fiftyPens);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		viewModel.SetPenVisibility(1, true);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		provider.LastQueriedPenIds.Should().Equal(1);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;

		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.LastQueriedPenIds.Should().Equal([1, .. _switchedOnPenIds]);
	}

	// Zoom widths step by 1.25, so no width lands within the 2.2 % below a Minute ceiling that the first extra
	// pen past the 10 % hysteresis needs; at 512 columns one lands 2 % below the Raw ceiling, and the same
	// count moves the chart from Raw to Minute.
	[AvaloniaFact]
	public void SwitchingOnMorePensThanTheHysteresisAbsorbs_MovesToTheCoarserLayerAndReadsItOnce()
	{
		const int ReferencePenCount = ChartNavigationController.ReferencePenCount;
		const int ColumnCount = 512;
		var extraPenCount = (int)Math.Floor(0.1 * ReferencePenCount) + 1;
		var allPenIds = Enumerable.Range(1, ReferencePenCount + extraPenCount).ToArray();
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue([
			.. allPenIds.Select(penId => new Pen(
				penId, $"Pen {penId}", ["Group A"], "#ff0000", EnabledOnStart: penId <= ReferencePenCount))
		]);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		viewModel.ReportDataAreaWidth(ColumnCount);
		var rawCeiling = TimeSpan.FromSeconds(15.0) * ColumnCount;
		var navigation = viewModel.Navigation;
		navigation.ZoomAt(0.98 * rawCeiling / (navigation.To - navigation.From), navigation.To);
		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);
		var width = navigation.To - navigation.From;
		width.Should().BeLessThan(rawCeiling)
			.And.BeGreaterThan(rawCeiling * ReferencePenCount / (ReferencePenCount + extraPenCount) * 1.1);
		navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);
		var queriesBefore = provider.HistoryQueryCount;

		foreach (var penId in allPenIds.Skip(ReferencePenCount))
		{
			viewModel.SetPenVisibility(penId, true);
		}

		navigation.ActiveLayer.Should().Be(AggregationLayer.Minute);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var read = provider.ChartHistoryQueries.Skip(queriesBefore).Should().ContainSingle().Subject;
		read.Layer.Should().Be(AggregationLayer.Minute);
		read.PenIds.Should().Equal(allPenIds);

		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);
		provider.ChartHistoryQueries.Skip(queriesBefore).Should().AllSatisfy(query =>
		{
			query.Layer.Should().Be(AggregationLayer.Minute);
			query.PenIds.Should().Equal(allPenIds);
		});
		var queriesAtMinute = provider.HistoryQueryCount;

		viewModel.SetPenVisibility(allPenIds[^1], false);
		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);

		navigation.ActiveLayer.Should().Be(AggregationLayer.Minute, "the hysteresis widens the layer in force");
		provider.HistoryQueryCount.Should().Be(queriesAtMinute);
	}

	// Reported as fetched, the pan's end would push no read and cancel the one pen 1 still waits on.
	[AvaloniaFact]
	public async Task AWindowIsNotFetchedWhileAVisiblePenIsMissingFromTheSetReadForItsRange()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(_fiftyPens);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		provider.GatedLayer = viewModel.Navigation.ActiveLayer;
		viewModel.SetPenVisibility(1, true);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var penOneRead = provider.LastQueriedCancellationToken;
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-30.0));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		penOneRead.IsCancellationRequested.Should().BeFalse();
		provider.HistoryQueryCount.Should().Be(queriesBefore);

		await ReleaseAndAwait(viewModel, 1, () => provider.HistoryGate.SetResult(
			Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
				[new PenHistoryEnvelope(1, [From, To], [7.0, 7.0], [7.0, 7.0], [7.0, 7.0])])));

		viewModel.FindPen(1)!.CurrentValue.Should().Be(7.0);
	}

	[AvaloniaFact]
	public void AResultForOnePenOverTheRangeInHand_KeepsTheBandFetchedForTheOthers()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(_fiftyPens);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		viewModel.SetPenVisibility(1, true);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.Navigation.PanBy(TimeSpan.FromMinutes(-30.0));
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(queriesBefore);
	}

	// Pen 2 was read for the first range only, so the read of pen 1 alone over the next one leaves it unread there.
	[AvaloniaFact]
	public void AResultForOnePenOverAnotherRange_ReplacesTheSetReadForTheRange()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		viewModel.AddPen(FakeDataProvider.SecondPen);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		viewModel.SetPenVisibility(2, false);
		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var nextRange = provider.ChartHistoryQueries[^1];
		nextRange.PenIds.Should().Equal(1);

		viewModel.SetPenVisibility(2, true);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.LastQueriedPenIds.Should().Equal(2);
		provider.LastQueriedFromUtc.Should().Be(nextRange.FromUtc);
		provider.LastQueriedToUtc.Should().Be(nextRange.ToUtc);
	}

	[AvaloniaFact]
	public void AFailedHistoryQueryReachesTheMessagePanelAndStillRedraws()
	{
		using var messagePanel = new MessagePanelViewModel();
		var (viewModel, scheduler, _, provider) = CreateViewModelOnOneScheduler(messagePanel: messagePanel);
		using var chart = viewModel;
		var state = chart.AddPen(FakeDataProvider.FirstPen);
		var redraws = 0;
		using var redrawSubscription = chart.RedrawRequested.Subscribe(_ => redraws++);
		provider.FailHistory = true;

		chart.RequestInitialHistory();
		// Past the debounce window and short of the 400 ms cap interval, which would admit a second query for
		// the same window.
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(200.0).Ticks);

		messagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(FakeDataProvider.UnreachableArchive()));
		redraws.Should().Be(1, "the failure path keeps the deliberate ApplyAxisModel and RequestRedraw recovery");
		state.CurrentValue.Should().BeNull("a failed read leaves the pen unloaded");
		state.Line.SnapshotColumns().Should().BeEmpty();
	}

	[AvaloniaFact]
	public void RealtimeApplyReportsAThrowingConsumerAndKeepsTheSubscription()
	{
		using var messagePanel = new MessagePanelViewModel();
		var (viewModel, scheduler, _, _) = CreateViewModelOnOneScheduler(RealtimeInterval, messagePanel);
		using var chart = viewModel;
		var state = chart.AddPen(FakeDataProvider.FirstPen);
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
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks + 1);
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
		using var messagePanel = new MessagePanelViewModel();
		var (viewModel, scheduler, _, provider) = CreateViewModelOnOneScheduler(RealtimeInterval, messagePanel);
		using var chart = viewModel;
		var state = chart.AddPen(FakeDataProvider.FirstPen);
		provider.PoisonRealtimeWindow = true;

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks + 1);

		messagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(
				new ExceptionalError(new PoisonedWindowException())));
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
		var thrown = new InvalidOperationException("the provider ended the live edge");
		var provider = new FakeDataProvider(scheduler, RealtimeInterval)
		{
			RealtimeStreamFailure = thrown
		};
		using var panel = new MessagePanelViewModel();
		using var chart = CreateViewModel(
			scheduler: scheduler,
			provider: provider,
			uiScheduler: scheduler,
			messagePanel: panel).ViewModel;

		var advance = () => scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks * 2);

		advance.Should().NotThrow();
		panel.Entries.Should().ContainSingle().Which.RepeatCount.Should()
			.Be(1, "the one failure reaches the operator as one occurrence");
		panel.Entries[0].View.Should().Be(ArchiveFailureMapper.Map(new ExceptionalError(thrown)));
	}

	// The report edits a bound collection, the likeliest thrower on the hop. Without the guard the throw
	// leaves the realtime subscription this method is the observer of and ends the live edge for the session.
	// The log carries both halves: the failure that was being reported, and the panel refusing it.
	[AvaloniaFact]
	public void AThrowOutOfTheMessagePanel_IsLoggedAndTheRealtimeApplyContinues()
	{
		var logger = new FakeLogger<TrendChartViewModel>();
		using var panel = new MessagePanelViewModel();
		var (viewModel, scheduler, _, provider) = CreateViewModelOnOneScheduler(RealtimeInterval, panel, logger);
		using var chart = viewModel;
		var state = chart.AddPen(FakeDataProvider.FirstPen);
		var poison = ReportingTestDoubles.PoisonEntries(panel);

		provider.PoisonRealtimeWindow = true;

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks + 1);

		logger.Failures().Should().HaveCount(2);
		logger.Failures()[0].Should().BeOfType<PoisonedWindowException>("the failure being reported is logged first");
		logger.Failures()[1].Should().BeOfType<PoisonedPanelException>("then the panel's refusal");

		poison.Dispose();
		provider.PoisonRealtimeWindow = false;
		var valueWhenTheReportThrew = state.CurrentValue;
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(200.0).Ticks);

		state.CurrentValue.Should().NotBe(
			valueWhenTheReportThrew, "the batches after the failed report still reach the pen");
	}

	private static (TrendChartViewModel ViewModel, PixelRect DataRect) RenderTwoFlatPens(
		double firstValue,
		double secondValue,
		int activePenId = 1)
	{
		var (viewModel, _, dataRect) = ChartTestBuilder.RenderTwoFlatPens(
			(FakeDataProvider.FirstPen, firstValue),
			(FakeDataProvider.SecondPen, secondValue),
			PickWindowSeconds,
			activePenId);

		return (viewModel, dataRect);
	}

	private static Pixel PixelBetweenTheLines(TrendChartViewModel viewModel, PixelRect dataRect)
	{
		var between = PixelOnLine(viewModel, 1, (PickFirstValue + PickSecondValue) / 2.0, dataRect);
		DistanceTo(viewModel, 1, between, dataRect).Should().BeGreaterThan(PickClearancePixels);
		DistanceTo(viewModel, 2, between, dataRect).Should().BeGreaterThan(PickClearancePixels);

		return between;
	}

	private static double DistanceTo(TrendChartViewModel viewModel, int penId, Pixel click, PixelRect dataRect)
	{
		return viewModel.FindPen(penId)!.Line.DistanceTo(click, dataRect);
	}

	private static Pixel PixelOnLine(TrendChartViewModel viewModel, int penId, double value, PixelRect dataRect)
	{
		var axes = viewModel.FindPen(penId)!.Line.Axes;

		return new Pixel(
			axes.XAxis.GetPixel(LocalTimeAxis.ToAxis(From.AddSeconds(PickWindowSeconds / 2)), dataRect),
			axes.YAxis.GetPixel(value, dataRect));
	}

	// The value a held query carries, distinct from every value the fake answers with, so the assertion says
	// which of the two results the chart ended up holding.
	private static Result<IReadOnlyList<PenHistoryEnvelope>> StaleEnvelopes()
	{
		return Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
		[
			new PenHistoryEnvelope(1, [From, To], [99.0, 99.0], [99.0, 99.0], [99.0, 99.0])
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
	private static TrendChartViewModel CreateViewModelWithStoredScales()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(
			[.. provider.Pens.Select(pen => pen with { ScaleMinOnStart = 5.0, ScaleMaxOnStart = 50.0 })]);
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		viewModel.SetAxisLimits(2, 20.0, 80.0);

		return viewModel;
	}

	private static TrendChartViewModel CreateViewModelWithManualScales()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(provider.Pens);
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		viewModel.SetAxisLimits(2, 20.0, 80.0);

		return viewModel;
	}

	private static void ApplyRangeGesture(TrendChartViewModel viewModel, RangeGesture gesture, double fractionOrFactor)
	{
		switch (gesture)
		{
			case RangeGesture.Shift:
				viewModel.ShiftActivePen(fractionOrFactor);

				break;

			default:
				viewModel.ScaleActivePen(fractionOrFactor, 0.5);

				break;
		}
	}

	private static async Task AssertRawIsReadAfterAReturnFromACoarseRead(
		bool failTheCoarseRead,
		Func<TrendChartViewModel, FakeDataProvider, Task>? land)
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(RealtimeInterval);
		viewModel.AddPen(FakeDataProvider.FirstPen);
		await LoadRawEndingAtTheRealtimeEpoch(viewModel, scheduler, provider);
		var rawWidth = viewModel.Navigation.To - viewModel.Navigation.From;

		provider.FailHistory = failTheCoarseRead;
		await ZoomOutAndIssueTheCoarseRead(viewModel, scheduler, provider);

		if (land is not null)
		{
			await land(viewModel, provider);
		}

		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);
		provider.FailHistory = false;
		var queriesBefore = provider.HistoryQueryCount;

		ZoomBackToTheRawWidth(viewModel, scheduler, rawWidth);

		await AwaitQueryCount(provider, queriesBefore + 1);
		provider.LastQueriedLayer.Should().Be(AggregationLayer.Raw);
	}

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
		await ReleaseAndAwait(
			viewModel, 1, () => provider.HistoryGate.SetResult(EnvelopeEndingAtTheRealtimeEpoch()));
		LandTheMarginReadFromTheReleasedGate(scheduler);
		provider.HistoryGate = new();

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks * 3);
		viewModel.Navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);
	}

	private static IEnumerable<(DateTime FromUtc, DateTime ToUtc, int TargetColumnCount)> ReadsSince(
		FakeDataProvider provider,
		int queriesBefore)
	{
		return provider.ChartHistoryQueries
			.Skip(queriesBefore)
			.Select(query => (query.FromUtc, query.ToUtc, query.TargetColumnCount));
	}

	private static void ZoomOutToOneNotchBelowTheRawCeiling(TrendChartViewModel viewModel)
	{
		while (ChartNavigationController.LayerForWidth(
			1.25 * (viewModel.Navigation.To - viewModel.Navigation.From),
			AggregationLayer.Raw,
			viewModel.Navigation.TargetColumnCount,
			visiblePenCount: 1) == AggregationLayer.Raw)
		{
			viewModel.Navigation.ZoomAt(1.25, viewModel.Navigation.To);
		}
	}

	// The released gate answers the margin read the landed result asked for as soon as it is admitted.
	private static void LandTheMarginReadFromTheReleasedGate(TestScheduler scheduler)
	{
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 2);
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
		return new PenHistoryEnvelope(penId, [From, To], new ThrowsOnFirstRead(99.0, 2), [99.0, 99.0], [99.0, 99.0]);
	}

	// The apply runs on the thread Rx resumes the released query on, so its report lands after the release.
	private static async Task AwaitPanelEntries(MessagePanelViewModel panel, int count)
	{
		await HeadlessWait.Until(() => panel.Entries.Count >= count);

		panel.Entries.Should().HaveCount(count);
	}

	// A query the pipeline issues behind a released one starts once Rx has unwound the released query on the
	// thread it resumed the task on, so a count read straight after the release can run ahead of it.
	private static async Task AwaitQueryCount(FakeDataProvider provider, int count)
	{
		await HeadlessWait.Until(() => provider.HistoryQueryCount >= count);
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
