using AwesomeAssertions;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartCursorOverlayTests
{
	private static readonly DataRectPixels _dataRect = new(Left: 50.0, Right: 250.0, Top: 10.0, Bottom: 410.0);

	[Theory]
	[InlineData(150.0)]
	[InlineData(50.0)]
	[InlineData(250.0)]
	public void Project_CursorInsideOrOnAnEdgeOfTheDataRect_PlacesLineAtCursorX(double cursorPixelX)
	{
		var placement = ChartCursorOverlay.Project(cursorPixelX, _dataRect, renderScale: 1.0);

		placement.IsVisible.Should().BeTrue();
		placement.LineX.Should().Be(cursorPixelX);
	}

	[Fact]
	public void Project_CrosshairSpansDataRectHeight()
	{
		var placement = ChartCursorOverlay.Project(cursorPixelX: 150.0, _dataRect, renderScale: 1.0);

		placement.LineTop.Should().Be(_dataRect.Top);
		placement.LineBottom.Should().Be(_dataRect.Bottom);
	}

	[Theory]
	[InlineData(49.0, 1.0)]
	[InlineData(251.0, 1.0)]
	[InlineData(150.0, 0.0)]
	public void Project_CursorOutsideTheDataRectOrNonPositiveRenderScale_IsHidden(
		double cursorPixelX,
		double renderScale)
	{
		var placement = ChartCursorOverlay.Project(cursorPixelX, _dataRect, renderScale);

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
}
