using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;
using SemiPlot.UI.PenEditor;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.ChartViewTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Chart;

// The axis scale panel through Avalonia's real pipeline: a press on the axis opens the flyout, and the
// operator's clicks, typed text and keys are the only input.
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
[Collection(ProcessGlobalStateCollection.Name)]
public sealed class AxisScalePanelViewTests
{
	[AvaloniaFact]
	public void ClickOnTheAxisRegion_OpensThePanelSeededWithTheDrawnPensBoundsNameAndUnit()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.AddPen(new Pen(3, "Pen 3", ["Group A"], "#0000ff", Unit: "sccm"));
		viewModel.SetActivePen(3);
		viewModel.SetAxisLimits(3, 10.0, 90.5);
		using var shown = ShowChart(viewModel);

		var (flyout, panel) = OpenPanel(viewModel, shown);

		flyout.IsOpen.Should().BeTrue("a press on the axis region opens the panel");
		panel.FindControl<TextBlock>("AxisScalePenName")!.Text.Should().Be("Pen 3");
		panel.FindControl<TextBlock>("AxisScalePenUnit")!.Text.Should().Be("sccm");
		panel.FindControl<TextBox>("AxisScaleMaximum")!.Text.Should().Be(PenFormRules.FormatBound(90.5));
		panel.FindControl<TextBox>("AxisScaleMinimum")!.Text.Should().Be(PenFormRules.FormatBound(10.0));
		viewModel.ScaleSettings[3].Mode.Should().Be(ScaleMode.Manual, "the press writes nothing");
	}

	[AvaloniaFact]
	public void ClickWhereAHiddenPensAxisWas_OpensNothing()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var axisPoint = AxisRegionPoint(viewModel);
		viewModel.SetPenVisibility(2, false);
		viewModel.SetPenVisibility(1, false);
		viewModel.Plot.RenderInMemory((int)shown.PlotControl.Bounds.Width, (int)shown.PlotControl.Bounds.Height);

		HeadlessInput.Click(shown.Window, shown.PlotControl, axisPoint);

		FlyoutBase.GetAttachedFlyout(shown.PlotControl)!.IsOpen.Should().BeFalse();
	}

	[AvaloniaFact]
	public void AFractionTypedInTheMaximum_IsWrittenByEnterWithThePanelOpen()
	{
		using var culture = new CultureScope("en-US");
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);

		TypeInto(shown.Window, panel, "AxisScaleMaximum", "150.5");
		HeadlessInput.Press(shown.Window, PhysicalKey.Enter);

		viewModel.ScaleSettings[1].ManualMax.Should().Be(150.5);
		flyout.IsOpen.Should().BeTrue("Enter writes the pair and keeps the panel open");
	}

	[AvaloniaFact]
	public void AnUnreadableEntry_IsMarkedAndEnterWritesNothing()
	{
		using var culture = new CultureScope("en-US");
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);

		TypeInto(shown.Window, panel, "AxisScaleMaximum", "1x");

		panel.FindControl<TextBlock>("AxisScaleValidationMessage")!.Text.Should().Be(Resources.AxisScaleBoundInvalid);
		panel.FindControl<TextBox>("AxisScaleMaximum")!.Classes.Should().Contain("invalid");

		HeadlessInput.Press(shown.Window, PhysicalKey.Enter);

		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
		flyout.IsOpen.Should().BeTrue("a refused entry leaves the panel open");
	}

	[AvaloniaFact]
	public void Escape_ClosesThePanelAndWritesNothing()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 0.0, 100.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);

		TypeInto(shown.Window, panel, "AxisScaleMinimum", "50");
		HeadlessInput.Press(shown.Window, PhysicalKey.Escape);

		flyout.IsOpen.Should().BeFalse();
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((0.0, 100.0));
	}

	[AvaloniaFact]
	public void AClickOutsideThePanel_WritesThePendingPairOnceAndClosesIt()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 0.0, 100.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);
		TypeInto(shown.Window, panel, "AxisScaleMinimum", "50");
		var revision = viewModel.ScalesRevision;

		ClickOutsideThePanel(shown);

		flyout.IsOpen.Should().BeFalse("a click outside dismisses the panel");
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((50.0, 100.0));
		viewModel.ScalesRevision.Should().Be(revision + 1, "the dismiss applies the axis model once");
	}

	[AvaloniaFact]
	public void TheWindowLosingActivation_WritesThePendingPairAndClosesThePanel()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 0.0, 100.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);
		TypeInto(shown.Window, panel, "AxisScaleMinimum", "50");

		// The headless window posts its Deactivated callback from Hide, as a platform window does on losing activation.
		shown.Window.Hide();
		flyout.IsOpen.Should().BeTrue("only the posted deactivation may dismiss the panel");
		Dispatcher.UIThread.RunJobs();

		flyout.IsOpen.Should().BeFalse();
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((50.0, 100.0));
	}

	[AvaloniaFact]
	public void APressOnThePanelsEmptyArea_WritesThePendingPairWithThePanelOpen()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 0.0, 100.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);

		TypeInto(shown.Window, panel, "AxisScaleMinimum", "50");
		PressThePanelsEmptyArea(shown, panel);

		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((50.0, 100.0));
		panel.FindControl<TextBox>("AxisScaleMinimum")!.IsFocused.Should().BeFalse("the press ends the edit");
		flyout.IsOpen.Should().BeTrue();
	}

	[AvaloniaFact]
	public void Escape_AfterAPressOnThePanelsEmptyArea_ClosesThePanel()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 0.0, 100.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);

		TypeInto(shown.Window, panel, "AxisScaleMinimum", "50");
		PressThePanelsEmptyArea(shown, panel);
		HeadlessInput.Press(shown.Window, PhysicalKey.Escape);

		flyout.IsOpen.Should().BeFalse("Escape still reaches the panel once the press ended the edit");
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((50.0, 100.0), "the press wrote the pair before Escape");
	}

	[AvaloniaFact]
	public void AClickFromOneBoundIntoTheOther_WritesNothing_AndMovesTheKeyboard()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 0.0, 100.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);
		var maximum = panel.FindControl<TextBox>("AxisScaleMaximum")!;

		TypeInto(shown.Window, panel, "AxisScaleMinimum", "50");
		HeadlessInput.Click(shown.Window, maximum);

		maximum.IsFocused.Should().BeTrue();
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((0.0, 100.0), "the click stays inside the pair");
		flyout.IsOpen.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TabbingBetweenTheBounds_WritesNothing_UntilFocusLeavesThePair()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 0.0, 100.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);

		TypeInto(shown.Window, panel, "AxisScaleMaximum", "50");
		HeadlessInput.Press(shown.Window, PhysicalKey.Tab);

		panel.FindControl<TextBox>("AxisScaleMinimum")!.IsFocused.Should().BeTrue();
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((0.0, 100.0), "a half-edited pair is not written");

		HeadlessInput.Press(shown.Window, PhysicalKey.Tab);

		panel.FindControl<CheckBox>("AxisScaleLogarithmic")!.IsFocused.Should().BeTrue();
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((0.0, 50.0));
		flyout.IsOpen.Should().BeTrue();
	}

	[AvaloniaFact]
	public void OnAnAutoscaledPen_TabAndEnterThroughUntouchedBounds_KeepTheAxisAuto()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);
		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Auto);

		HeadlessInput.Click(shown.Window, panel.FindControl<TextBox>("AxisScaleMaximum")!);
		HeadlessInput.Press(shown.Window, PhysicalKey.Tab);
		HeadlessInput.Press(shown.Window, PhysicalKey.Enter);
		HeadlessInput.Press(shown.Window, PhysicalKey.Tab);

		panel.FindControl<CheckBox>("AxisScaleLogarithmic")!.IsFocused.Should().BeTrue("focus left the pair");
		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Auto, "untouched bounds freeze no range");
		flyout.IsOpen.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TheAutoscaleButton_AfterATypedBound_RevertsThePensAxisToAutoAndClosesThePanel()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);

		TypeInto(shown.Window, panel, "AxisScaleMinimum", "20");
		HeadlessInput.Click(shown.Window, panel.FindControl<Button>("AxisScaleAutoscaleButton")!);

		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Auto);
		flyout.IsOpen.Should().BeFalse();
	}

	[AvaloniaFact]
	public void EnterOnTheFocusedAutoscaleButton_Autoscales_AfterThePairWasWrittenOnLeavingIt()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);

		TypeInto(shown.Window, panel, "AxisScaleMinimum", "20");
		HeadlessInput.Press(shown.Window, PhysicalKey.Tab);
		panel.FindControl<CheckBox>("AxisScaleLogarithmic")!.IsFocused.Should().BeTrue();
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((20.0, 90.0), "focus left the pair");
		HeadlessInput.Press(shown.Window, PhysicalKey.Tab);
		panel.FindControl<Button>("AxisScaleAutoscaleButton")!.IsFocused.Should().BeTrue();
		HeadlessInput.Press(shown.Window, PhysicalKey.Enter);

		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Auto);
		flyout.IsOpen.Should().BeFalse();
	}

	[AvaloniaFact]
	public void TheInitialScaleButton_AfterATypedBound_RestoresTheStoredPairAndClosesThePanel()
	{
		using var viewModel = CreateLoadedViewModel();
		var pen = FakeDataProvider.FirstPen;
		viewModel.ApplyCatalogue([pen with { ScaleMinOnStart = 5.0, ScaleMaxOnStart = 50.0 }]);
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);
		var button = panel.FindControl<Button>("AxisScaleInitialScaleButton")!;

		button.Content.Should().Be(Resources.AxisScaleInitialScale);
		TypeInto(shown.Window, panel, "AxisScaleMinimum", "20");
		HeadlessInput.Click(shown.Window, button);

		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((5.0, 50.0));
		flyout.IsOpen.Should().BeFalse();
	}

	[AvaloniaFact]
	public void TheLogarithmicBox_SwitchesTheAxisAndKeepsThePanelOpenAtItsSize()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 1.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);
		var box = panel.FindControl<CheckBox>("AxisScaleLogarithmic")!;
		var panelSize = panel.Bounds.Size;
		var presenterSize = PresenterOf(panel).Bounds.Size;
		box.Content.Should().Be(Resources.AxisScaleLogarithmic);
		box.IsChecked.Should().BeFalse();

		HeadlessInput.Click(shown.Window, box);

		viewModel.ScaleSettings[1].IsLogarithmic.Should().BeTrue();
		box.IsChecked.Should().BeTrue();
		flyout.IsOpen.Should().BeTrue("the switch keeps the panel open");
		panel.Bounds.Size.Should().Be(panelSize);
		PresenterOf(panel).Bounds.Size.Should().Be(presenterSize);

		HeadlessInput.Click(shown.Window, box);

		viewModel.ScaleSettings[1].IsLogarithmic.Should().BeFalse();
		box.IsChecked.Should().BeFalse();
		flyout.IsOpen.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TheLogarithmicBox_OverAManualMinimumOfZero_StaysClearAndNamesTheRule()
	{
		using var culture = new CultureScope("en-US");
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 0.0, 100.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);
		var box = panel.FindControl<CheckBox>("AxisScaleLogarithmic")!;
		var message = panel.FindControl<TextBlock>("AxisScaleValidationMessage")!;
		var sizes = SizesOf(panel, message);

		HeadlessInput.Click(shown.Window, box);

		box.IsChecked.Should().BeFalse("the refused flag stays off");
		message.Text.Should().Be(Resources.ScaleLogMinimumPositive);
		viewModel.ScaleSettings[1].IsLogarithmic.Should().BeFalse();
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((0.0, 100.0));
		SizesOf(panel, message).Should().Be(sizes);
		flyout.IsOpen.Should().BeTrue();
	}

	[AvaloniaTheory]
	[InlineData("en-US")]
	[InlineData("ru-RU")]
	public void TheMessageLine_KeepsThePanelsSize_EmptyWithAOneLineRuleAndWithTheLogRule(string cultureName)
	{
		using var culture = new CultureScope(cultureName);
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 0.0, 100.0);
		using var shown = ShowChart(viewModel);
		var (_, panel) = OpenPanel(viewModel, shown);
		var message = panel.FindControl<TextBlock>("AxisScaleValidationMessage")!;
		message.Text.Should().BeEmpty();
		var emptySizes = SizesOf(panel, message);

		TypeInto(shown.Window, panel, "AxisScaleMinimum", "150");
		message.Text.Should().Be(Resources.AxisScaleMinimumBelowMaximum);
		var oneLineRuleSizes = SizesOf(panel, message);

		TypeInto(shown.Window, panel, "AxisScaleMinimum", "0");
		HeadlessInput.Click(shown.Window, panel.FindControl<CheckBox>("AxisScaleLogarithmic")!);
		message.Text.Should().Be(Resources.ScaleLogMinimumPositive);
		var logRuleSizes = SizesOf(panel, message);

		oneLineRuleSizes.Should().Be(emptySizes);
		logRuleSizes.Should().Be(emptySizes);
	}

	private static (Flyout Flyout, AxisScalePanel Panel) OpenPanel(TrendChartViewModel viewModel, ShownChart shown)
	{
		HeadlessInput.Click(shown.Window, shown.PlotControl, AxisRegionPoint(viewModel));
		Dispatcher.UIThread.RunJobs();

		var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(shown.PlotControl)!;

		return (flyout, (AxisScalePanel)flyout.Content!);
	}

	private static void ClickOutsideThePanel(ShownChart shown)
	{
		HeadlessInput.Click(shown.Window, shown.PlotControl, new Point(shown.PlotControl.Bounds.Width - 20.0, 20.0));
	}

	private static void PressThePanelsEmptyArea(ShownChart shown, AxisScalePanel panel)
	{
		var boundsTop = panel.FindControl<Grid>("AxisScaleBounds")!.TranslatePoint(default, panel)!.Value.Y;

		HeadlessInput.Click(shown.Window, panel, new Point(panel.Bounds.Width - 4.0, boundsTop - 5.0));
	}

	private static FlyoutPresenter PresenterOf(AxisScalePanel panel)
	{
		return panel.GetVisualAncestors().OfType<FlyoutPresenter>().First();
	}

	private static (Size Panel, Size Message, Size Presenter) SizesOf(AxisScalePanel panel, TextBlock message)
	{
		return (panel.Bounds.Size, message.Bounds.Size, PresenterOf(panel).Bounds.Size);
	}

	private static void TypeInto(Window window, AxisScalePanel panel, string fieldName, string text)
	{
		var field = panel.FindControl<TextBox>(fieldName)!;

		HeadlessInput.Clear(window, field);
		HeadlessInput.Type(window, field, text);
	}
}
