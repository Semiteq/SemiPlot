using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Unit.Postgres;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class CatalogueNormaliserTests
{
	[Fact]
	public void ThePensProjection_ReadsInCurrentCultureNameOrderRatherThanOrdinal()
	{
		var rows = new[]
		{
			new RawPenRow(1, "Beta", null, null, null, 0, true, null, null, false),
			new RawPenRow(2, "alpha", null, null, null, 0, true, null, null, false),
			new RawPenRow(3, "Alpha", null, null, null, 0, true, null, null, false)
		};

		var read = new CatalogueNormaliser(NullLogger.Instance).Project(rows, []);

		read.Pens.Select(pen => pen.Name).Should().Equal("alpha", "Alpha", "Beta");
	}
}
