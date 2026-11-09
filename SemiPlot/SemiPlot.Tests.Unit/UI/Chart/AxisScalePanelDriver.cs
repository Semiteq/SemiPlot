using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;

using SemiPlot.UI.Chart;

using static SemiPlot.Tests.Unit.UI.Chart.ChartViewTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Chart;

/// <summary>Opens the axis scale panel on a shown chart and reaches its parts, for the panel's view tests.</summary>
internal static class AxisScalePanelDriver
{
	public static (Flyout Flyout, AxisScalePanel Panel) OpenPanel(TrendChartViewModel viewModel, ShownChart shown)
	{
		HeadlessInput.Click(shown.Window, shown.PlotControl, AxisRegionPoint(viewModel));
		Dispatcher.UIThread.RunJobs();

		var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(shown.PlotControl)!;

		return (flyout, (AxisScalePanel)flyout.Content!);
	}

	public static FlyoutPresenter PresenterOf(AxisScalePanel panel)
	{
		return panel.GetVisualAncestors().OfType<FlyoutPresenter>().First();
	}

	public static (Size Panel, Size Message, Size Presenter) SizesOf(AxisScalePanel panel, TextBlock message)
	{
		return (panel.Bounds.Size, message.Bounds.Size, PresenterOf(panel).Bounds.Size);
	}

	public static void TypeInto(Window window, AxisScalePanel panel, string fieldName, string text)
	{
		var field = panel.FindControl<TextBox>(fieldName)!;

		HeadlessInput.Clear(window, field);
		HeadlessInput.Type(window, field, text);
	}
}
