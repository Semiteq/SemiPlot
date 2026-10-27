using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;

using Xunit;

using static SemiPlot.Tests.Unit.Chart.PenEnvelopes;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartDeltaCursorReaderTests
{
	private const int PenId = 1;

	private static readonly DateTime _origin = new(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc);

	[Theory]
	[InlineData(PenLineStyle.Stepped, -21.0)]
	[InlineData(PenLineStyle.Interpolated, -16.8)]
	public void Measure_ReadsTheActivePenAsItsLineStyleDraws(PenLineStyle lineStyle, double expectedDeltaY)
	{
		var pensById = new Dictionary<int, TrendPenState>
		{
			[PenId] = new(new Pen(PenId, "Pen 1", ["Group A"], "#ff0000", LineStyle: lineStyle), new EnvelopeLine())
		};
		var envelopesById = new Dictionary<int, PenHistoryEnvelope>
		{
			[PenId] = Envelope(PenId, (_origin, 226.0), (_origin.AddMinutes(5), 205.0), (_origin.AddMinutes(10), 205.0))
		};
		var reader = new ChartDeltaCursorReader(pensById, envelopesById);

		reader.Place(_origin.AddMinutes(1));
		reader.Place(_origin.AddMinutes(9));

		reader.Measure(PenId)!.DeltaY!.Value.Should().BeApproximately(expectedDeltaY, 1e-9);
	}

	[Fact]
	public void FormatReadout_WithoutADeltaY_RendersTheNoValuePlaceholder()
	{
		var readout = new DeltaReadout(TimeSpan.FromSeconds(30.0), null);

		var text = ChartDeltaCursorReader.FormatReadout(readout, null);

		text.Should()
			.Contain(Resources.DeltaValueLabel)
			.And.Contain(Resources.NoValuePlaceholder);
	}

	[Fact]
	public void FormatReadout_WithADeltaY_RendersTheNumber()
	{
		using var culture = new CultureScope("en-US");
		var readout = new DeltaReadout(TimeSpan.FromSeconds(30.0), 1.5);

		var text = ChartDeltaCursorReader.FormatReadout(readout, null);

		text.Should()
			.Contain("1.5")
			.And.NotContain(Resources.NoValuePlaceholder);
	}

	[Fact]
	public void FormatReadout_RendersTheDeltaYUnderThePensMask()
	{
		using var culture = new CultureScope("en-US");
		var readout = new DeltaReadout(TimeSpan.FromSeconds(30.0), -1.2e-5);

		var text = ChartDeltaCursorReader.FormatReadout(readout, "0.00E+00");

		text.Should().EndWith($"{Resources.DeltaValueLabel} -1.20E-05");
	}

	[Theory]
	[InlineData(30.0, "30s")]
	[InlineData(90.0, "1m 30s")]
	[InlineData(3725.0, "1h 2m 5s")]
	public void FormatReadout_RendersTheDeltaTimeInItsLargestWholeUnits(double seconds, string expected)
	{
		var readout = new DeltaReadout(TimeSpan.FromSeconds(seconds), 1.5);

		var text = ChartDeltaCursorReader.FormatReadout(readout, null);

		text.Should().Contain($"{Resources.DeltaTimeLabel} {expected}   ");
	}
}
