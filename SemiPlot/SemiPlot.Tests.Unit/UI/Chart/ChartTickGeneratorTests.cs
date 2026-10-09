using AwesomeAssertions;

using ScottPlot;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartTickGeneratorTests
{
	private const float AxisPixels = 400f;
	private const float PanelPixels = 380f;

	private static readonly CoordinateRange _linearRange = new(0.0, 100.0);
	private static readonly CoordinateRange _pannedLinearRange = new(10.0, 110.0);
	private static readonly CoordinateRange _decades = new(-6.0, -1.0);

	public static TheoryData<string> Generators => [nameof(LinearTickGenerator), nameof(LogTickGenerator)];

	[Theory]
	[MemberData(nameof(Generators))]
	public void AHiddenGenerator_GeneratesNothing(string generatorName)
	{
		var generator = Create(generatorName);

		Regenerate(generator, RangeFor(generator), AxisPixels);

		generator.Ticks.Should().BeEmpty();
	}

	[Theory]
	[MemberData(nameof(Generators))]
	public void AGeneratorHiddenAfterDrawing_PublishesNoTicks(string generatorName)
	{
		var generator = Create(generatorName);
		generator.IsDrawn = true;
		Regenerate(generator, RangeFor(generator), AxisPixels);
		var drawn = generator.Ticks;

		generator.IsDrawn = false;
		Regenerate(generator, RangeFor(generator), PanelPixels);

		drawn.Should().NotBeEmpty();
		generator.Ticks.Should().BeEmpty();
	}

	[Theory]
	[MemberData(nameof(Generators))]
	public void TheTwoLengthsOfOneFrame_GenerateTwice(string generatorName)
	{
		var generator = Create(generatorName);
		generator.IsDrawn = true;
		var range = RangeFor(generator);

		Regenerate(generator, range, AxisPixels);
		var atAxis = generator.Ticks;
		Regenerate(generator, range, PanelPixels);
		var atPanel = generator.Ticks;
		Regenerate(generator, range, AxisPixels);
		var atAxisAgain = generator.Ticks;
		Regenerate(generator, range, PanelPixels);

		atAxis.Should().NotBeEmpty().And.NotBeSameAs(atPanel);
		atAxisAgain.Should().BeSameAs(atAxis);
		generator.Ticks.Should().BeSameAs(atPanel);
	}

	[Fact]
	public void ANewRange_Regenerates()
	{
		var generator = new LinearTickGenerator { IsDrawn = true };
		Regenerate(generator, _linearRange, AxisPixels);
		var before = generator.Ticks;

		Regenerate(generator, _pannedLinearRange, AxisPixels);

		generator.Ticks.Should().NotBeSameAs(before);
		generator.Ticks.Should().OnlyContain(tick => _pannedLinearRange.Contains(tick.Position));
	}

	[Theory]
	[MemberData(nameof(Generators))]
	public void AGeneratorHiddenThenDrawn_TicksAgain(string generatorName)
	{
		var generator = Create(generatorName);
		Regenerate(generator, RangeFor(generator), AxisPixels);
		generator.Ticks.Should().BeEmpty();

		generator.IsDrawn = true;
		Regenerate(generator, RangeFor(generator), AxisPixels);

		generator.Ticks.Should().Contain(tick => tick.IsMajor);
	}

	private static IDrawnTickGenerator Create(string generatorName)
	{
		return generatorName == nameof(LogTickGenerator) ? new LogTickGenerator() : new LinearTickGenerator();
	}

	private static CoordinateRange RangeFor(IDrawnTickGenerator generator)
	{
		return generator is LogTickGenerator ? _decades : _linearRange;
	}

	private static void Regenerate(ITickGenerator generator, CoordinateRange range, float pixels)
	{
		using var paint = Paint.NewDisposablePaint();
		generator.Regenerate(range, Edge.Left, new PixelLength(pixels), paint, new LabelStyle());
	}
}
