using System.Globalization;

using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

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
		var readout = new DeltaReadout(TimeSpan.FromSeconds(30.0), 1.5);

		var text = ChartDeltaCursorReader.FormatReadout(readout);

		text.Should()
			.Contain(1.5.ToString("0.###", CultureInfo.CurrentCulture))
			.And.NotContain(Resources.NoValuePlaceholder);
	}
}
