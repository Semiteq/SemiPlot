using AwesomeAssertions;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartCursorOverlayTests
{
	private static readonly DataRectPixels _dataRect = new(Left: 50.0, Right: 250.0, Top: 10.0, Bottom: 410.0);

	[Fact]
	public void Project_CursorInsideDataRect_PlacesLineAtCursorX()
	{
		var placement = ChartCursorOverlay.Project(cursorPixelX: 150.0, _dataRect, renderScale: 1.0);

		placement.IsVisible.Should().BeTrue();
		placement.LineX.Should().Be(150.0);
	}

	[Fact]
	public void Project_CrosshairSpansDataRectHeight()
	{
		var placement = ChartCursorOverlay.Project(cursorPixelX: 150.0, _dataRect, renderScale: 1.0);

		placement.LineTop.Should().Be(_dataRect.Top);
		placement.LineBottom.Should().Be(_dataRect.Bottom);
	}

	[Fact]
	public void Project_CursorOnLeftEdge_IsVisible()
	{
		var placement = ChartCursorOverlay.Project(cursorPixelX: _dataRect.Left, _dataRect, renderScale: 1.0);

		placement.IsVisible.Should().BeTrue();
		placement.LineX.Should().Be(_dataRect.Left);
	}

	[Fact]
	public void Project_CursorOnRightEdge_IsVisible()
	{
		var placement = ChartCursorOverlay.Project(cursorPixelX: _dataRect.Right, _dataRect, renderScale: 1.0);

		placement.IsVisible.Should().BeTrue();
		placement.LineX.Should().Be(_dataRect.Right);
	}

	[Fact]
	public void Project_CursorLeftOfDataRect_IsHidden()
	{
		var placement = ChartCursorOverlay.Project(cursorPixelX: 49.0, _dataRect, renderScale: 1.0);

		placement.IsVisible.Should().BeFalse();
	}

	[Fact]
	public void Project_CursorRightOfDataRect_IsHidden()
	{
		var placement = ChartCursorOverlay.Project(cursorPixelX: 251.0, _dataRect, renderScale: 1.0);

		placement.IsVisible.Should().BeFalse();
	}

	[Fact]
	public void Project_NonUnitRenderScale_ConvertsPixelsToDip()
	{
		var placement = ChartCursorOverlay.Project(cursorPixelX: 200.0, _dataRect, renderScale: 2.0);

		placement.IsVisible.Should().BeTrue();
		placement.LineX.Should().Be(100.0);
		placement.LineTop.Should().Be(_dataRect.Top / 2.0);
		placement.LineBottom.Should().Be(_dataRect.Bottom / 2.0);
	}

	[Fact]
	public void Project_NonPositiveRenderScale_IsHidden()
	{
		var placement = ChartCursorOverlay.Project(cursorPixelX: 150.0, _dataRect, renderScale: 0.0);

		placement.IsVisible.Should().BeFalse();
	}
}
