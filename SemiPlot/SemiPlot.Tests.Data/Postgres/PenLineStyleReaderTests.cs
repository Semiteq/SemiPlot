using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class PenLineStyleReaderTests
{
	// Asserted numerically here rather than through the reader, because a reorder in Core would keep every
	// conversion below passing while silently reinterpreting every commissioned site's catalogue.
	[Fact]
	public void TheOrdinalsAreTheValuesTheCatalogueStores()
	{
		Assert.Equal((short)0, (short)PenLineStyle.Interpolated);
		Assert.Equal((short)1, (short)PenLineStyle.Stepped);
	}

	[Theory]
	[InlineData((short)0, PenLineStyle.Interpolated)]
	[InlineData((short)1, PenLineStyle.Stepped)]
	public void AStoredOrdinalReadsBackAsItsMember(short storedValue, PenLineStyle expected)
	{
		var lineStyle = PenLineStyleReader.Read(storedValue, penId: 7, NullLogger.Instance);

		Assert.Equal(expected, lineStyle);
	}

	[Theory]
	[InlineData((short)2)]
	[InlineData((short)-1)]
	[InlineData(short.MaxValue)]
	public void AnUnrecognisedValueReadsAsInterpolated(short storedValue)
	{
		var lineStyle = PenLineStyleReader.Read(storedValue, penId: 7, NullLogger.Instance);

		Assert.Equal(PenLineStyle.Interpolated, lineStyle);
	}
}
