using AwesomeAssertions;

using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Trends;

[Trait("Component", "Core")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class PenRangeGestureTests
{
	private const double Precision = 1e-9;

	[Theory]
	[InlineData(0.1, -10.0, 90.0)]
	[InlineData(-0.1, 10.0, 110.0)]
	public void Shift_Linear_MovesBothLimitsByTheFractionOfTheSpan(
		double fraction,
		double expectedMin,
		double expectedMax)
	{
		var shifted = PenRangeGesture.Shift(0.0, 100.0, isLogarithmic: false, fraction);

		shifted.Should().NotBeNull();
		shifted!.Value.Min.Should().BeApproximately(expectedMin, Precision);
		shifted.Value.Max.Should().BeApproximately(expectedMax, Precision);
	}

	[Fact]
	public void Shift_Logarithmic_MovesBothDecadesByATenthOfTheDecadeSpan()
	{
		var shifted = PenRangeGesture.Shift(1.0, 1000.0, isLogarithmic: true, fraction: 0.1);

		shifted.Should().NotBeNull();
		Math.Log10(shifted!.Value.Min).Should().BeApproximately(-0.3, Precision);
		Math.Log10(shifted.Value.Max).Should().BeApproximately(2.7, Precision);
	}

	[Theory]
	[InlineData(0.8)]
	[InlineData(1.25)]
	public void Scale_Linear_KeepsTheAnchorValueAtTheAnchorFraction(double factor)
	{
		var scaled = PenRangeGesture.Scale(0.0, 100.0, isLogarithmic: false, factor, anchorFraction: 0.25);

		scaled.Should().NotBeNull();
		var (min, max) = scaled!.Value;
		(max - min).Should().BeApproximately(100.0 * factor, Precision);
		(min + (0.25 * (max - min))).Should().BeApproximately(25.0, Precision);
	}

	[Fact]
	public void Scale_Logarithmic_KeepsTheAnchorDecade()
	{
		var scaled = PenRangeGesture.Scale(1.0, 1000.0, isLogarithmic: true, factor: 0.8, anchorFraction: 0.5);

		scaled.Should().NotBeNull();
		var bottom = Math.Log10(scaled!.Value.Min);
		var top = Math.Log10(scaled.Value.Max);
		(top - bottom).Should().BeApproximately(2.4, Precision);
		(bottom + (0.5 * (top - bottom))).Should().BeApproximately(1.5, Precision);
	}

	[Fact]
	public void Scale_InThenOut_RestoresTheSpan()
	{
		var narrowed = PenRangeGesture.Scale(-20.0, 80.0, isLogarithmic: false, factor: 0.8, anchorFraction: 0.3);
		var restored = PenRangeGesture.Scale(
			narrowed!.Value.Min,
			narrowed.Value.Max,
			isLogarithmic: false,
			factor: 1.25,
			anchorFraction: 0.3);

		restored.Should().NotBeNull();
		restored!.Value.Min.Should().BeApproximately(-20.0, Precision);
		restored.Value.Max.Should().BeApproximately(80.0, Precision);
	}

	[Fact]
	public void Scale_AnchorAtTheBottom_HoldsTheMinimum()
	{
		var scaled = PenRangeGesture.Scale(10.0, 110.0, isLogarithmic: false, factor: 0.8, anchorFraction: 0.0);

		scaled.Should().NotBeNull();
		scaled!.Value.Min.Should().BeApproximately(10.0, Precision);
		scaled.Value.Max.Should().BeApproximately(90.0, Precision);
	}

	[Fact]
	public void Scale_AnchorAtTheTop_HoldsTheMaximum()
	{
		var scaled = PenRangeGesture.Scale(10.0, 110.0, isLogarithmic: false, factor: 0.8, anchorFraction: 1.0);

		scaled.Should().NotBeNull();
		scaled!.Value.Min.Should().BeApproximately(30.0, Precision);
		scaled.Value.Max.Should().BeApproximately(110.0, Precision);
	}

	[Fact]
	public void Shift_LinearOverflow_ReturnsNull()
	{
		PenRangeGesture.Shift(-1e308, 1e308, isLogarithmic: false, fraction: 0.1).Should().BeNull();
	}

	[Fact]
	public void Scale_LinearOverflow_ReturnsNull()
	{
		PenRangeGesture.Scale(0.0, 1e300, isLogarithmic: false, factor: 1e10, anchorFraction: 0.5).Should().BeNull();
	}

	[Fact]
	public void Shift_LogarithmicPastTheLargestDouble_ReturnsNull()
	{
		PenRangeGesture.Shift(1.0, 1e300, isLogarithmic: true, fraction: -0.1).Should().BeNull();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Shift_ZeroSpan_ReturnsNull(bool isLogarithmic)
	{
		PenRangeGesture.Shift(5.0, 5.0, isLogarithmic, fraction: 0.1).Should().BeNull();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Scale_ZeroSpan_ReturnsNull(bool isLogarithmic)
	{
		PenRangeGesture.Scale(5.0, 5.0, isLogarithmic, factor: 1.25, anchorFraction: 0.5).Should().BeNull();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Scale_CollapsingTheSpan_ReturnsNull(bool isLogarithmic)
	{
		PenRangeGesture.Scale(1.0, 100.0, isLogarithmic, factor: 0.0, anchorFraction: 0.5).Should().BeNull();
	}

	[Fact]
	public void Shift_LogarithmicMinimumUnderflowingToZero_ReturnsNull()
	{
		PenRangeGesture.Shift(1e-300, 1e10, isLogarithmic: true, fraction: 0.1).Should().BeNull();
	}

	[Fact]
	public void Shift_LogarithmicNonPositiveMinimum_ReturnsNull()
	{
		PenRangeGesture.Shift(0.0, 100.0, isLogarithmic: true, fraction: 0.1).Should().BeNull();
	}
}
