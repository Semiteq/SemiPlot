using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using SemiPlot.Core.Trends;
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
public sealed class AxisScalePanelViewTests
{
	[AvaloniaFact]
	public void ClickOnTheAxisRegion_OpensThePanelSeededWithTheDrawnPensBoundsNameAndUnit()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.AddPen(new Pen(2, "Pen 2", ["Group A"], "#00ff00", Unit: "sccm"));
		viewModel.SetActivePen(2);
		viewModel.SetAxisLimits(2, 10.0, 90.5);
		using var shown = ShowChart(viewModel);

		var (flyout, panel) = OpenPanel(viewModel, shown);

		flyout.IsOpen.Should().BeTrue("a press on the axis region opens the panel");
		panel.FindControl<TextBlock>("AxisScalePenName")!.Text.Should().Be("Pen 2");
		panel.FindControl<TextBlock>("AxisScalePenUnit")!.Text.Should().Be("sccm");
		panel.FindControl<TextBox>("AxisScaleMaximum")!.Text.Should().Be(PenFormRules.FormatBound(90.5));
		panel.FindControl<TextBox>("AxisScaleMinimum")!.Text.Should().Be(PenFormRules.FormatBound(10.0));
		viewModel.ScaleSettings[2].Mode.Should().Be(ScaleMode.Manual, "the press writes nothing");
	}

	[AvaloniaFact]
	public void ClickWhereAHiddenPensAxisWas_OpensNothing()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var axisPoint = AxisRegionPoint(viewModel);
		viewModel.SetPenVisibility(1, false);
		viewModel.Plot.RenderInMemory((int)shown.PlotControl.Bounds.Width, (int)shown.PlotControl.Bounds.Height);

		HeadlessInput.Click(shown.Window, shown.PlotControl, axisPoint);

		FlyoutBase.GetAttachedFlyout(shown.PlotControl)!.IsOpen.Should().BeFalse();
	}

	[AvaloniaFact]
	public void TypedBoundsAndTheApplyButton_WriteAManualScaleAndClosePanel()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);

		TypeInto(shown.Window, panel, "AxisScaleMaximum", "120");
		TypeInto(shown.Window, panel, "AxisScaleMinimum", "-20");
		HeadlessInput.Click(shown.Window, panel.FindControl<Button>("AxisScaleApplyButton")!);

		viewModel.ScaleSettings[1].Should().Be(new PenScaleSettings(1)
		{
			Mode = ScaleMode.Manual,
			ManualMin = -20.0,
			ManualMax = 120.0
		});
		flyout.IsOpen.Should().BeFalse("Apply closes the panel");
	}

	[AvaloniaTheory]
	[InlineData("en-US", "150.5")]
	[InlineData("ru-RU", "150,5")]
	public void AFractionInTheCurrentCulturesSeparator_IsWrittenByEnter(string cultureName, string typed)
	{
		using var culture = new CultureScope(cultureName);
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);

		TypeInto(shown.Window, panel, "AxisScaleMaximum", typed);
		HeadlessInput.Press(shown.Window, PhysicalKey.Enter);

		viewModel.ScaleSettings[1].ManualMax.Should().Be(150.5);
		flyout.IsOpen.Should().BeFalse();
	}

	[AvaloniaTheory]
	[InlineData("en-US", "1x")]
	[InlineData("en-US", "150,5")]
	[InlineData("ru-RU", "150.5")]
	public void AnUnreadableEntry_IsMarkedAndNeitherEnterNorApplyWritesIt(string cultureName, string typed)
	{
		using var culture = new CultureScope(cultureName);
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);
		var applyButton = panel.FindControl<Button>("AxisScaleApplyButton")!;

		TypeInto(shown.Window, panel, "AxisScaleMaximum", typed);

		panel.FindControl<TextBlock>("AxisScaleValidationMessage")!.Text.Should().Be(Resources.AxisScaleBoundInvalid);
		panel.FindControl<TextBox>("AxisScaleMaximum")!.Classes.Should().Contain("invalid");
		applyButton.IsEffectivelyEnabled.Should().BeFalse();

		HeadlessInput.Press(shown.Window, PhysicalKey.Enter);
		HeadlessInput.Click(shown.Window, applyButton);

		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
		flyout.IsOpen.Should().BeTrue("a refused entry leaves the panel open");
	}

	[AvaloniaFact]
	public void Escape_ClosesThePanelAndWritesNothing()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);

		TypeInto(shown.Window, panel, "AxisScaleMaximum", "500");
		HeadlessInput.Press(shown.Window, PhysicalKey.Escape);

		flyout.IsOpen.Should().BeFalse();
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
	}

	[AvaloniaFact]
	public void AClickOutsideThePanel_ClosesItAndWritesNothing()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);

		TypeInto(shown.Window, panel, "AxisScaleMaximum", "500");
		HeadlessInput.Click(shown.Window, shown.PlotControl, new Point(shown.PlotControl.Bounds.Width - 20.0, 20.0));

		flyout.IsOpen.Should().BeFalse("a click outside dismisses the panel");
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
	}

	[AvaloniaTheory]
	[InlineData("en-US")]
	[InlineData("ru-RU")]
	public void AnInvertedPair_ShowsTheMessageKeepsThePanelsSizeAndEnterWritesNothing(string cultureName)
	{
		using var culture = new CultureScope(cultureName);
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);
		var applyButton = panel.FindControl<Button>("AxisScaleApplyButton")!;
		var message = panel.FindControl<TextBlock>("AxisScaleValidationMessage")!;
		var panelSize = panel.Bounds.Size;
		var messageSize = message.Bounds.Size;
		var presenterSize = PresenterOf(panel).Bounds.Size;
		message.Text.Should().BeEmpty();

		TypeInto(shown.Window, panel, "AxisScaleMinimum", "95");
		HeadlessInput.Press(shown.Window, PhysicalKey.Enter);

		message.Text.Should().Be(Resources.AxisScaleMinimumBelowMaximum);
		applyButton.IsEffectivelyEnabled.Should().BeFalse("an inverted pair cannot be applied");
		panel.Bounds.Size.Should().Be(panelSize);
		message.Bounds.Size.Should().Be(messageSize);
		PresenterOf(panel).Bounds.Size.Should().Be(presenterSize);
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
		flyout.IsOpen.Should().BeTrue();
	}

	[AvaloniaTheory]
	[InlineData("en-US")]
	[InlineData("ru-RU")]
	public void AnEmptyField_ShowsTheMessageKeepsThePanelsSizeAndEnterWritesNothing(string cultureName)
	{
		using var culture = new CultureScope(cultureName);
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);
		var message = panel.FindControl<TextBlock>("AxisScaleValidationMessage")!;
		var panelSize = panel.Bounds.Size;
		var messageSize = message.Bounds.Size;
		var presenterSize = PresenterOf(panel).Bounds.Size;

		HeadlessInput.Clear(shown.Window, panel.FindControl<TextBox>("AxisScaleMaximum")!);
		HeadlessInput.Press(shown.Window, PhysicalKey.Enter);

		message.Text.Should().Be(Resources.AxisScaleBoundsRequired);
		panel.FindControl<Button>("AxisScaleApplyButton")!.IsEffectivelyEnabled.Should().BeFalse();
		panel.Bounds.Size.Should().Be(panelSize);
		message.Bounds.Size.Should().Be(messageSize);
		PresenterOf(panel).Bounds.Size.Should().Be(presenterSize);
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
		flyout.IsOpen.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TheAutoscaleButton_RevertsThePensAxisToAutoAndClosesThePanel()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);

		HeadlessInput.Click(shown.Window, panel.FindControl<Button>("AxisScaleAutoscaleButton")!);

		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Auto);
		flyout.IsOpen.Should().BeFalse();
	}

	[AvaloniaFact]
	public void EnterOnTheFocusedAutoscaleButton_Autoscales_AndDoesNotApplyTheFields()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);

		TypeInto(shown.Window, panel, "AxisScaleMinimum", "20");
		HeadlessInput.Press(shown.Window, PhysicalKey.Tab);
		panel.FindControl<Button>("AxisScaleAutoscaleButton")!.IsFocused.Should().BeTrue();
		HeadlessInput.Press(shown.Window, PhysicalKey.Enter);

		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Auto);
		flyout.IsOpen.Should().BeFalse();
	}

	[AvaloniaFact]
	public void TheInitialScaleButton_RestoresTheStoredPairAndClosesThePanel()
	{
		using var viewModel = CreateLoadedViewModel();
		var pen = new Pen(1, "Pen 1", ["Group A"], "#ff0000");
		viewModel.ApplyCatalogue([pen with { ScaleMin = 5.0, ScaleMax = 50.0 }]);
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);
		var button = panel.FindControl<Button>("AxisScaleInitialScaleButton")!;

		button.Content.Should().Be(Resources.AxisScaleInitialScale);
		HeadlessInput.Click(shown.Window, button);

		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((5.0, 50.0));
		flyout.IsOpen.Should().BeFalse();
	}

	[AvaloniaFact]
	public void WhenThePenIsHiddenWhileThePanelIsOpen_ThePanelClosesWithoutWriting()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);

		TypeInto(shown.Window, panel, "AxisScaleMaximum", "500");
		viewModel.SetPenVisibility(1, false);
		Dispatcher.UIThread.RunJobs();

		flyout.IsOpen.Should().BeFalse();
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
	}

	private static (Flyout Flyout, AxisScalePanel Panel) OpenPanel(TrendChartViewModel viewModel, ShownChart shown)
	{
		HeadlessInput.Click(shown.Window, shown.PlotControl, AxisRegionPoint(viewModel));
		Dispatcher.UIThread.RunJobs();

		var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(shown.PlotControl)!;

		return (flyout, (AxisScalePanel)flyout.Content!);
	}

	private static FlyoutPresenter PresenterOf(AxisScalePanel panel)
	{
		return panel.GetVisualAncestors().OfType<FlyoutPresenter>().First();
	}

	private static void TypeInto(Window window, AxisScalePanel panel, string fieldName, string text)
	{
		var field = panel.FindControl<TextBox>(fieldName)!;

		HeadlessInput.Clear(window, field);
		HeadlessInput.Type(window, field, text);
	}
}
