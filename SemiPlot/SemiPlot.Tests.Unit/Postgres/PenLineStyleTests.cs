using AwesomeAssertions;

using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Unit.Postgres;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class PenLineStyleTests
{
	// Asserted numerically, because a reorder in Core would silently reinterpret every commissioned site's
	// catalogue: semiplot_tags.line_style stores the ordinal.
	[Fact]
	public void TheOrdinalsAreTheValuesTheCatalogueStores()
	{
		((short)PenLineStyle.Interpolated).Should().Be(0);
		((short)PenLineStyle.Stepped).Should().Be(1);
	}
}
