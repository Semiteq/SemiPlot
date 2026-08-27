using System.Text.RegularExpressions;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data;

// The colour format is the one part of the seeder's catalogue nothing else pins. TagCatalogWriter writes
// it into semiplot_tags, ArchiveStatements.PenCatalog reads it back into Pen, and the golden digest is
// over ArchiveRow, which carries (Id, Layer, Timestamp, Value, Quality) and no colour. A hex string the
// UI cannot parse would therefore reach a chart with no test failing first.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class SyntheticPenCatalogColorTests
{
	private static readonly Regex _hexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

	[Fact]
	public void BuildEveryColorIsSixDigitHex()
	{
		var pens = SyntheticPenCatalog.Build();

		Assert.All(pens, pen => Assert.Matches(_hexColor, pen.Color));
	}
}
