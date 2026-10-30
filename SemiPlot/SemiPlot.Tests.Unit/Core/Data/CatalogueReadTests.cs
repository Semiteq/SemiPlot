using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Data;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class CatalogueReadTests
{
	private const string FallbackColor = "#808080";

	[Fact]
	public void EqualCataloguesMatch()
	{
		ACatalogueRead().Matches(ACatalogueRead()).Should().BeTrue();
	}

	[Fact]
	public void TheSamePensAndGroupsInAnotherOrderMatch()
	{
		var read = TwoPensInTwoGroups();
		var reordered = new CatalogueRead(
			new PenCatalogue([.. read.Stored.Pens.Reverse()], [.. read.Stored.Groups.Reverse()]),
			[.. read.Pens.Reverse()]);

		read.Matches(reordered).Should().BeTrue();
	}

	[Fact]
	public void AnUnusableMaskReplacedByAnotherUnusableMask_DoesNotMatch_WithAnUnchangedNormalisedPen()
	{
		var previous = ACatalogueRead(storedFormat: "##.##");
		var current = ACatalogueRead(storedFormat: "##.###");

		PenValueFormat.IsAcceptable("##.##").Should().BeFalse();
		PenValueFormat.IsAcceptable("##.###").Should().BeFalse();
		current.Pens.Should().Equal(previous.Pens);

		previous.Matches(current).Should().BeFalse();
	}

	[Fact]
	public void ANullColourReplacedByTheFallbackColour_DoesNotMatch_WithAnUnchangedNormalisedPen()
	{
		var previous = ACatalogueRead(storedColor: null);
		var current = ACatalogueRead(storedColor: FallbackColor);

		current.Pens.Should().Equal(previous.Pens);

		previous.Matches(current).Should().BeFalse();
	}

	[Fact]
	public void AChangeOnAGroupThatHoldsNoPen_DoesNotMatch_WithUnchangedPens()
	{
		var previous = new CatalogueRead(
			new PenCatalogue([StoredPen(1)], [new StoredGroup(1, "Vacuum", [])]),
			[ChartPen(1, "#123456", [])]);
		var current = new CatalogueRead(
			new PenCatalogue([StoredPen(1)], [new StoredGroup(1, "Chamber", [])]),
			[ChartPen(1, "#123456", [])]);

		current.Pens.Should().Equal(previous.Pens);

		previous.Matches(current).Should().BeFalse();
	}

	[Fact]
	public void AGroupRename_DoesNotMatch()
	{
		ACatalogueRead(groupName: "Vacuum").Matches(ACatalogueRead(groupName: "Chamber")).Should().BeFalse();
	}

	[Fact]
	public void AFoldFlagChangeAlone_DoesNotMatch()
	{
		var previous = ACatalogueRead();
		var current = previous with
		{
			Stored = previous.Stored with { Groups = [previous.Stored.Groups[0] with { FoldedOnStart = true }] }
		};

		current.Pens.Should().Equal(previous.Pens);

		previous.Matches(current).Should().BeFalse();
	}

	[Fact]
	public void AMembershipMove_DoesNotMatch()
	{
		var previous = TwoPensInTwoGroups();
		var current = new CatalogueRead(
			new PenCatalogue(
				previous.Stored.Pens,
				[new StoredGroup(1, "Vacuum", []), new StoredGroup(2, "Chamber", [1, 2])]),
			[ChartPen(1, "#123456", ["Chamber"]), ChartPen(2, "#234567", ["Chamber"])]);

		previous.Matches(current).Should().BeFalse();
	}

	[Fact]
	public void AnAddedPen_DoesNotMatch()
	{
		var previous = ACatalogueRead();
		var current = new CatalogueRead(
			new PenCatalogue([.. previous.Stored.Pens, StoredPen(2)], previous.Stored.Groups),
			[.. previous.Pens, ChartPen(2, "#234567", [])]);

		previous.Matches(current).Should().BeFalse();
		current.Matches(previous).Should().BeFalse("a removed pen is the same comparison the other way round");
	}

	[Fact]
	public void AStoredPenAddedAlone_DoesNotMatch()
	{
		var previous = ACatalogueRead();
		var current = previous with
		{
			Stored = previous.Stored with { Pens = [.. previous.Stored.Pens, StoredPen(2)] }
		};

		previous.Matches(current).Should().BeFalse();
		current.Matches(previous).Should().BeFalse();
	}

	[Fact]
	public void AnAddedGroup_DoesNotMatch()
	{
		var previous = ACatalogueRead();
		var current = previous with
		{
			Stored = previous.Stored with { Groups = [.. previous.Stored.Groups, new StoredGroup(2, "Chamber", [])] }
		};

		previous.Matches(current).Should().BeFalse();
		current.Matches(previous).Should().BeFalse("a removed group is the same comparison the other way round");
	}

	private static CatalogueRead TwoPensInTwoGroups()
	{
		return new CatalogueRead(
			new PenCatalogue(
				[StoredPen(1), StoredPen(2, color: "#234567")],
				[new StoredGroup(1, "Vacuum", [1]), new StoredGroup(2, "Chamber", [2])]),
			[ChartPen(1, "#123456", ["Vacuum"]), ChartPen(2, "#234567", ["Chamber"])]);
	}

	private static CatalogueRead ACatalogueRead(
		string storedFormat = "0.###",
		string? storedColor = "#123456",
		string groupName = "Vacuum")
	{
		var storedPen = StoredPen(1, storedFormat, storedColor);
		var group = new StoredGroup(1, groupName, [1]);

		return new CatalogueRead(
			new PenCatalogue([storedPen], [group]),
			[ChartPen(1, storedColor ?? FallbackColor, [groupName], NormalisedFormat(storedFormat))]);
	}

	private static string? NormalisedFormat(string storedFormat)
	{
		return PenValueFormat.IsAcceptable(storedFormat) ? storedFormat : null;
	}

	private static StoredPen StoredPen(int id, string? format = "0.###", string? color = "#123456")
	{
		return new StoredPen(
			id,
			$"Pen {id}",
			"Pa",
			format,
			color,
			PenLineStyle.Interpolated,
			true,
			null,
			null,
			false);
	}

	private static Pen ChartPen(int id, string color, IReadOnlyList<string> groups, string? format = "0.###")
	{
		return new Pen(id, $"Pen {id}", groups, color, "Pa", format);
	}
}
