using AwesomeAssertions;

using ScottPlot;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.UI.Chart;

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

		region!.Contains(insideAxisPanel, verticalCenter).Should().BeTrue();
		region.Contains(dataRect.HorizontalCenter, verticalCenter).Should().BeFalse();
	}

	[Fact]
	public void IsUpperHalf_TopOfDataAreaIsUpper_BottomIsLower()
	{
		var (plot, axis) = RenderedPlot();
		var region = ChartAxisRegion.TryCreate(plot, axis)!;
		var dataRect = plot.RenderManager.LastRender.Layout.DataRect;

		region.IsUpperHalf(dataRect.Top + 1f).Should().BeTrue();
		region.IsUpperHalf(dataRect.Bottom - 1f).Should().BeFalse();
	}

	[Fact]
	public void ValueAt_MapsTopToMax_BottomToMin()
	{
		var (plot, axis) = RenderedPlot();
		var region = ChartAxisRegion.TryCreate(plot, axis)!;
		var dataRect = plot.RenderManager.LastRender.Layout.DataRect;

		region.ValueAt(dataRect.Top).Should().BeApproximately(axis.Range.Max, 0.5);
		region.ValueAt(dataRect.Bottom).Should().BeApproximately(axis.Range.Min, 0.5);
	}

	[Fact]
	public void TryCreate_ForARightEdgeAxis_ContainsAPointInsideItsPanelToTheRightOfTheDataArea()
	{
		var plot = new Plot();
		var rightAxis = plot.Axes.AddRightAxis();
		var scatter = plot.Add.Scatter(new double[] { 0, 1, 2 }, new double[] { 0, 50, 100 });
		scatter.Axes.YAxis = rightAxis;
		plot.Axes.SetLimitsY(0.0, 100.0, rightAxis);
		plot.RenderInMemory(PlotWidth, PlotHeight);

		var region = ChartAxisRegion.TryCreate(plot, rightAxis);

		region.Should().NotBeNull();

		var layout = plot.RenderManager.LastRender.Layout;
		var dataRect = layout.DataRect;
		var verticalCenter = (dataRect.Top + dataRect.Bottom) / 2f;
		// Probe the panel's measured midpoint so the assertion does not depend on ScottPlot's panel width.
		var offset = layout.PanelOffsets[rightAxis];
		var size = layout.PanelSizes[rightAxis];
		var insideRightPanel = dataRect.Right + offset + (size / 2f);

		region!.Contains(insideRightPanel, verticalCenter).Should().BeTrue();
		region.Contains(dataRect.HorizontalCenter, verticalCenter).Should().BeFalse();
	}

	[Fact]
	public void ValueAt_DegenerateHeight_ReturnsTheAxisMaxInsteadOfDividingByZero()
	{
		// A zero-height data area would divide by zero in the pixel->value mapping; the guard returns the max.
		var region = ChartAxisRegion.ForTesting(panelLeft: 0f, panelRight: 10f, dataTop: 50f, dataBottom: 50f,
			axisMin: 1.0, axisMax: 9.0);

		region.ValueAt(50f).Should().Be(9.0);
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
