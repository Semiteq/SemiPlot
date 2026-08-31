using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

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
		Assert.Equal((short)0, (short)PenLineStyle.Interpolated);
		Assert.Equal((short)1, (short)PenLineStyle.Stepped);
	}
}
