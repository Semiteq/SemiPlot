using AwesomeAssertions;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class HistoryColumnTargetTests
{
	[Theory]
	[InlineData(900.0, 900)]
	[InlineData(100.0, HistoryColumnTarget.MinColumns)]
	[InlineData(5000.0, HistoryColumnTarget.MaxColumns)]
	[InlineData(900.6, 901)]
	public void FromDataAreaWidth_RequestsOneColumnPerPixelRoundedAndClampedToTheBounds(
		double width,
		int expectedColumns)
	{
		HistoryColumnTarget.FromDataAreaWidth(width).Should().Be(expectedColumns);
	}
}
