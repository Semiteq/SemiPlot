using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartDeltaCursorReaderTests
{
	[Fact]
	public void FormatReadout_WithoutADeltaY_RendersTheNoValuePlaceholder()
	{
		var readout = new DeltaReadout(TimeSpan.FromSeconds(30.0), null);

		var text = ChartDeltaCursorReader.FormatReadout(readout);

		text.Should()
			.Contain(Resources.DeltaValueLabel)
			.And.Contain(Resources.NoValuePlaceholder);
	}

	[Fact]
	public void FormatReadout_WithADeltaY_RendersTheNumber()
	{
		using var culture = new CultureScope("en-US");
		var readout = new DeltaReadout(TimeSpan.FromSeconds(30.0), 1.5);

		var text = ChartDeltaCursorReader.FormatReadout(readout);

		text.Should()
			.Contain("1.5")
			.And.NotContain(Resources.NoValuePlaceholder);
	}

	[Theory]
	[InlineData(30.0, "30s")]
	[InlineData(90.0, "1m 30s")]
	[InlineData(3725.0, "1h 2m 5s")]
	public void FormatReadout_RendersTheDeltaTimeInItsLargestWholeUnits(double seconds, string expected)
	{
		var readout = new DeltaReadout(TimeSpan.FromSeconds(seconds), 1.5);

		var text = ChartDeltaCursorReader.FormatReadout(readout);

		text.Should().Contain($"{Resources.DeltaTimeLabel} {expected}   ");
	}
}
