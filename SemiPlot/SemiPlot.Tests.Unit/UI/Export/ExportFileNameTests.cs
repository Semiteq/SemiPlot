using AwesomeAssertions;

using SemiPlot.UI.Export;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Export;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ExportFileNameTests
{
	private const string RangeName = "semiplot_20261027_140327-20261027_150000.csv";

	private static readonly DateTime _start = new(2026, 10, 27, 14, 3, 27);
	private static readonly DateTime _end = new(2026, 10, 27, 15, 0, 0);

	private static string NameFor(string? startMarkerLabel)
	{
		return ExportFileName.Suggest(_start, _end, startMarkerLabel);
	}

	[Theory]
	[InlineData("ru")]
	[InlineData("en")]
	[InlineData("th-TH")]
	[InlineData("fa-IR")]
	public void TheSuggestedFileName_UsesInvariantDigitsUnderEveryInterfaceCulture(string culture)
	{
		using var cultureScope = new CultureScope(culture);

		ExportFileName.Suggest(new DateTime(2026, 10, 13, 8, 5, 9), new DateTime(2026, 10, 14, 7, 0, 0), null)
			.Should().Be("semiplot_20261013_080509-20261014_070000.csv");
		NameFor("RecipeName.csv").Should().Be("RecipeName_2026-10-27_14-03.csv");
	}

	[Theory]
	[InlineData("RecipeName.csv", "RecipeName_2026-10-27_14-03.csv")]
	[InlineData("Note", "Note_2026-10-27_14-03.csv")]
	[InlineData("RecipeName.CSV", "RecipeName_2026-10-27_14-03.csv")]
	[InlineData("Run 3.5", "Run 3.5_2026-10-27_14-03.csv")]
	[InlineData("Lot/Etch.csv", "Lot_Etch_2026-10-27_14-03.csv")]
	[InlineData("Etch.csv.csv", "Etch.csv_2026-10-27_14-03.csv")]
	public void ALabelledStartMarker_NamesTheFileFromTheLabelAndTheStart(string label, string expected)
	{
		NameFor(label).Should().Be(expected);
	}

	[Theory]
	[InlineData("Etch.csv ")]
	[InlineData("  Etch")]
	[InlineData(" Etch .csv ")]
	public void TheLabelsOuterSpaces_AreTrimmedAroundTheExtensionCut(string label)
	{
		NameFor(label).Should().Be("Etch_2026-10-27_14-03.csv");
	}

	[Fact]
	public void ALongLabel_IsCutTo200Characters_SoTheNameStaysUnder255()
	{
		var name = NameFor(new string('E', 300) + ".csv");

		name.Should().Be(new string('E', 200) + "_2026-10-27_14-03.csv");
		name.Length.Should().BeLessThanOrEqualTo(255);
	}

	[Fact]
	public void TheCut_NeverSplitsASurrogatePair()
	{
		NameFor(new string('E', 199) + "\U0001F600" + "tail")
			.Should().Be(new string('E', 199) + "_2026-10-27_14-03.csv");
	}

	[Fact]
	public void EveryRefusedCharacter_BecomesAnUnderscore()
	{
		NameFor("a\\b/c:d*e?f\"g<h>i|j\tk\u0001l.csv")
			.Should().Be("a_b_c_d_e_f_g_h_i_j_k_l_2026-10-27_14-03.csv");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData(".csv")]
	[InlineData(" .CSV ")]
	public void NoMarkerOrAnUnlabelledOne_KeepsTheRangeName(string? label)
	{
		NameFor(label).Should().Be(RangeName);
	}
}
