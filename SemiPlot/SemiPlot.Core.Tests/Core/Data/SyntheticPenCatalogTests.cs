using System.Text.RegularExpressions;

using AwesomeAssertions;

using SemiPlot.Core.Data;

using Xunit;

namespace SemiPlot.Tests.Core.Data;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class SyntheticPenCatalogTests
{
	private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

	[Fact]
	public void Build_IsDeterministic()
	{
		var first = SyntheticPenCatalog.Build();
		var second = SyntheticPenCatalog.Build();

		first.Should().BeEquivalentTo(second, options => options.WithStrictOrdering());
	}

	[Fact]
	public void Build_ProjectVarIdsAreUnique()
	{
		var pens = SyntheticPenCatalog.Build();

		pens.Select(pen => pen.ProjectVarId).Should().OnlyHaveUniqueItems();
	}

	[Fact]
	public void Build_HasExpectedGroupCountsAndTotal()
	{
		var pens = SyntheticPenCatalog.Build();

		pens.Should().HaveCount(50);
		pens.Count(pen => pen.Group == "Heaters").Should().Be(16);
		pens.Count(pen => pen.Group == "Dampers").Should().Be(16);
		pens.Count(pen => pen.Group == "Gas lines").Should().Be(10);
		pens.Count(pen => pen.Group == "Pressures").Should().Be(4);
		pens.Count(pen => pen.Group == "Powers").Should().Be(4);
	}

	[Fact]
	public void Build_EveryPenHasMinNotAboveMax()
	{
		var pens = SyntheticPenCatalog.Build();

		pens.Should().OnlyContain(pen => pen.MinValue <= pen.MaxValue);
	}

	[Fact]
	public void Build_EveryColorIsSixDigitHex()
	{
		var pens = SyntheticPenCatalog.Build();

		pens.Should().OnlyContain(pen => HexColor.IsMatch(pen.Color));
	}
}
