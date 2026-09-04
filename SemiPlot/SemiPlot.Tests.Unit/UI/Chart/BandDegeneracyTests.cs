using AwesomeAssertions;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class BandDegeneracyTests
{
	[Fact]
	public void IsDegenerate_EmptyBand_IsDegenerate()
	{
		BandDegeneracy.IsDegenerate([]).Should().BeTrue();
	}

	[Fact]
	public void IsDegenerate_EveryColumnFlat_IsDegenerate()
	{
		var band = new[] { (0.0, 5.0, 5.0), (1.0, 7.0, 7.0), (2.0, 3.0, 3.0) };

		BandDegeneracy.IsDegenerate(band).Should().BeTrue();
	}

	[Fact]
	public void IsDegenerate_AnyColumnHasSpread_IsNotDegenerate()
	{
		var band = new[] { (0.0, 5.0, 5.0), (1.0, 8.0, 6.0), (2.0, 3.0, 3.0) };

		BandDegeneracy.IsDegenerate(band).Should().BeFalse();
	}

	[Fact]
	public void IsDegenerate_OnlyGapColumns_IsDegenerate()
	{
		var band = new[] { (0.0, double.NaN, double.NaN), (1.0, double.NaN, double.NaN) };

		BandDegeneracy.IsDegenerate(band).Should().BeTrue();
	}

	[Fact]
	public void IsDegenerate_SpreadColumnAmongGaps_IsNotDegenerate()
	{
		var band = new[] { (0.0, double.NaN, double.NaN), (1.0, 9.0, 4.0) };

		BandDegeneracy.IsDegenerate(band).Should().BeFalse();
	}
}
