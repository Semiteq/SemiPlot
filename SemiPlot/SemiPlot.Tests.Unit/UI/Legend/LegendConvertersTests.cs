using System.Globalization;

using Avalonia.Headless.XUnit;
using Avalonia.Media;

using AwesomeAssertions;

using SemiPlot.UI.Legend;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Legend;

/// <summary>The sidebar's two conversions, including the arm the provider is meant to make unreachable.</summary>
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
	[InlineData("Dampers", "DAMPERS")]
	[InlineData("\u0417\u0430\u0441\u043b\u043e\u043d\u043a\u0438", "\u0417\u0410\u0421\u041b\u041e\u041d\u041a\u0418")]
	[InlineData(null, "")]
	public void ToCapitals_SetsAGroupNameInCapitals(string? name, string expected)
	{
		LegendConverters.ToCapitals.Convert(name, typeof(string), null, CultureInfo.InvariantCulture)
			.Should().Be(expected);
	}
}
