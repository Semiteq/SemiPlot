using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

// The probe's truth table alone. Running the probe needs a database and is covered by the gated tests;
// deciding what its two booleans mean is pure logic, and two of the four combinations — both relations
// present, and a fallback the caller must supply — are reachable in no gated test.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class MissingRelationProbeTests
{
	[Theory]
	[InlineData(false, true, "semiplot_tags")]
	[InlineData(true, false, "trends")]
	[InlineData(false, false, "semiplot_tags")]
	public void TheAbsentRelationIsNamed(bool tagCatalogPresent, bool trendsPresent, string expected)
	{
		Assert.Equal(expected, MissingRelationProbe.Resolve(tagCatalogPresent, trendsPresent));
	}

	// Both present and a 42P01 still raised: the failing statement named something else entirely, so the
	// probe has nothing to add and the caller falls back to its own statement's relation.
	[Fact]
	public void BothRelationsPresentLeavesTheAnswerToTheCaller()
	{
		Assert.Null(MissingRelationProbe.Resolve(tagCatalogPresent: true, trendsPresent: true));
	}
}
