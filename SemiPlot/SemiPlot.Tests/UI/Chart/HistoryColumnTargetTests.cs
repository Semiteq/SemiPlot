using AwesomeAssertions;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class HistoryColumnTargetTests
{
	[Theory]
	[InlineData(0.0)]
	[InlineData(-10.0)]
	[InlineData(double.NaN)]
	public void FromDataAreaWidth_NoRenderedAreaYet_Throws(double width)
	{
		var act = () => HistoryColumnTarget.FromDataAreaWidth(width);

		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Fact]
	public void FromDataAreaWidth_WidthInRange_RequestsOneColumnPerPixel()
	{
		HistoryColumnTarget.FromDataAreaWidth(900.0).Should().Be(900);
	}

	[Fact]
	public void FromDataAreaWidth_NarrowArea_ClampsUpToMinimum()
	{
		HistoryColumnTarget.FromDataAreaWidth(100.0).Should().Be(HistoryColumnTarget.MinColumns);
	}

	[Fact]
	public void FromDataAreaWidth_WideArea_ClampsDownToMaximum()
	{
		HistoryColumnTarget.FromDataAreaWidth(5000.0).Should().Be(HistoryColumnTarget.MaxColumns);
	}

	[Fact]
	public void FromDataAreaWidth_FractionalWidth_RoundsToNearestColumn()
	{
		HistoryColumnTarget.FromDataAreaWidth(900.6).Should().Be(901);
	}
}
