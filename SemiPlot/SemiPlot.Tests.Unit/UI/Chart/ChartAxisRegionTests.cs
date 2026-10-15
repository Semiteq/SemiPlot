using AwesomeAssertions;

using ScottPlot;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartAxisRegionTests
{
	private const int PlotWidth = 400;
	private const int PlotHeight = 300;

	[Fact]
	public void TryCreate_AfterRender_ContainsAPointInsideTheLeftAxisPanel()
	{
		var (plot, axis) = RenderedPlot();
		var region = ChartAxisRegion.TryCreate(plot, axis);

		region.Should().NotBeNull();

		var dataRect = plot.RenderManager.LastRender.Layout.DataRect;
		var insideAxisPanel = dataRect.Left - 2f;
		var verticalCenter = (dataRect.Top + dataRect.Bottom) / 2f;

		region.Contains(insideAxisPanel, verticalCenter).Should().BeTrue();
		region.Contains(dataRect.HorizontalCenter, verticalCenter).Should().BeFalse();
	}

	// A hidden axis keeps a panel of size 0 at offset 0, whose band collapses onto the data rect's own
	// left edge and would answer a press there with the scale panel of a pen nothing draws.
	[Fact]
	public void TryCreate_ForAHiddenAxis_ReturnsNoRegion()
	{
		var (plot, axis) = RenderedPlot();
		axis.IsVisible = false;
		plot.RenderInMemory(PlotWidth, PlotHeight);

		ChartAxisRegion.TryCreate(plot, axis).Should().BeNull();
	}

	[Fact]
	public void TryCreateTimeAxis_AfterRender_ContainsTheBandUnderTheDataAreaOnly()
	{
		var (plot, _) = RenderedPlot();
		var region = ChartAxisRegion.TryCreateTimeAxis(plot);

		region.Should().NotBeNull();

		var layout = plot.RenderManager.LastRender.Layout;
		var dataRect = layout.DataRect;
		var bandTop = dataRect.Bottom + layout.PanelOffsets[plot.Axes.Bottom];
		var bandMiddle = bandTop + (layout.PanelSizes[plot.Axes.Bottom] / 2f);

		region.Contains(dataRect.HorizontalCenter, bandMiddle).Should().BeTrue();
		region.Contains(dataRect.HorizontalCenter, dataRect.VerticalCenter).Should().BeFalse("the data area is no axis");
		region.Contains(dataRect.Left - 2f, bandMiddle).Should().BeFalse("the corner under the Y axis is no time axis");
		region.Contains(dataRect.Right + 2f, bandMiddle).Should().BeFalse();
	}

	[Fact]
	public void TryCreateTimeAxis_ForAHiddenAxis_ReturnsNoRegion()
	{
		var (plot, _) = RenderedPlot();
		plot.Axes.Bottom.IsVisible = false;
		plot.RenderInMemory(PlotWidth, PlotHeight);

		ChartAxisRegion.TryCreateTimeAxis(plot).Should().BeNull();
	}

	private static (Plot Plot, IYAxis Axis) RenderedPlot()
	{
		var plot = new Plot();
		plot.Add.Scatter(new double[] { 0, 1, 2 }, new double[] { 0, 50, 100 });
		plot.Axes.SetLimitsY(0.0, 100.0, plot.Axes.Left);
		plot.RenderInMemory(PlotWidth, PlotHeight);

		return (plot, plot.Axes.Left);
	}
}
