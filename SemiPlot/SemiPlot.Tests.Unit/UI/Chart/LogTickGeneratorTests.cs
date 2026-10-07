using AwesomeAssertions;

using ScottPlot;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class LogTickGeneratorTests
{
	private const float AxisPixels = 400f;
	private const float PanelPixels = 380f;
	private const string IntegerMask = "0";
	private const string InvariantCultureName = "";

	[Fact]
	public void AMaskTooCoarseForTheSpacing_LeavesEveryMajorLabelDistinct()
	{
		var generator = new LogTickGenerator { Mask = IntegerMask };
		var range = new CoordinateRange(Math.Log10(100.0), Math.Log10(100.5));

		Regenerate(generator, range, AxisPixels);

		var majors = generator.Ticks.Where(tick => tick.IsMajor).Select(tick => tick.Label).ToList();
		majors.Should().NotBeEmpty().And.OnlyHaveUniqueItems();
		generator.Ticks.Should().HaveCount(
			LogAxis.Ticks(range.Min, range.Max, AxisPixels).Count,
			"a demoted major keeps its gridline as a minor");
	}

	[Theory]
	[InlineData(0.02, 0.5, "0.0", new[] { 0.1, 0.2, 0.5 }, new[] { "0.1", "0.2", "0.5" })]
	[InlineData(0.2, 5.0, IntegerMask, new[] { 1.0, 2.0, 5.0 }, new[] { "1", "2", "5" })]
	[InlineData(0.003, 0.08, "0.00", new[] { 0.01, 0.02, 0.05 }, new[] { "0.01", "0.02", "0.05" })]
	public void AMaskTooCoarseForTheRange_LabelsOnlyTheMajorsItReadsBack(
		double bottom, double top, string mask, double[] labelledValues, string[] labels)
	{
		using var culture = new CultureScope(InvariantCultureName);
		var generator = new LogTickGenerator { Mask = mask };

		Regenerate(generator, new CoordinateRange(Math.Log10(bottom), Math.Log10(top)), AxisPixels);

		var majors = generator.Ticks.Where(tick => tick.IsMajor).ToList();
		majors.Select(tick => tick.Label).Should().Equal(labels);
		majors.Select(tick => Math.Pow(10.0, tick.Position)).Should().Equal(
			labelledValues, (actual, expected) => Math.Abs(actual - expected) < expected * 1e-9);
	}

	[Fact]
	public void TheTwoLengthsOfOneFrame_ReuseTheirTicks()
	{
		var generator = new LogTickGenerator();
		var range = new CoordinateRange(-6.0, -1.0);

		Regenerate(generator, range, AxisPixels);
		var atAxis = generator.Ticks;
		Regenerate(generator, range, PanelPixels);
		var atPanel = generator.Ticks;
		Regenerate(generator, range, AxisPixels);

		generator.Ticks.Should().BeSameAs(atAxis);
		atPanel.Should().NotBeSameAs(atAxis);
	}

	private static void Regenerate(LogTickGenerator generator, CoordinateRange range, float pixels)
	{
		using var paint = Paint.NewDisposablePaint();
		generator.Regenerate(range, Edge.Left, new PixelLength(pixels), paint, new LabelStyle());
	}
}
