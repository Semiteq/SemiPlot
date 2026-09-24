using System.Globalization;

using Avalonia.Headless.XUnit;
using Avalonia.Media;

using AwesomeAssertions;

using SemiPlot.UI.Legend;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Legend;

/// <summary>The row's two conversions, including the arm the provider is meant to make unreachable.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class LegendConvertersTests
{
	[AvaloniaTheory]
	[InlineData("#FF0000", 255, 0, 0)]
	[InlineData("#00ff00", 0, 255, 0)]
	public void HexToBrush_PaintsTheStoredColour(string hex, byte red, byte green, byte blue)
	{
		var brush = LegendConverters.HexToBrush.Convert(hex, typeof(IBrush), null, CultureInfo.InvariantCulture);

		brush.Should().BeOfType<SolidColorBrush>()
			.Which.Color.Should().Be(Color.FromRgb(red, green, blue));
	}

	// The colour column is checked server-side and a NULL becomes the fallback hex, so this arm is only
	// reachable on a hand-edited installation: it draws no dot rather than throwing out of a binding.
	[AvaloniaTheory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("not a colour")]
	public void HexToBrush_DrawsNothingForAColourItCannotParse(string? hex)
	{
		LegendConverters.HexToBrush.Convert(hex, typeof(IBrush), null, CultureInfo.InvariantCulture)
			.Should().BeSameAs(Brushes.Transparent);
	}

	[AvaloniaTheory]
	[InlineData(true, FontWeight.Bold)]
	[InlineData(false, FontWeight.Normal)]
	public void ActiveToWeight_BoldsTheActiveRow(bool isActive, FontWeight expected)
	{
		LegendConverters.ActiveToWeight.Convert(isActive, typeof(FontWeight), null, CultureInfo.InvariantCulture)
			.Should().Be(expected);
	}
}
