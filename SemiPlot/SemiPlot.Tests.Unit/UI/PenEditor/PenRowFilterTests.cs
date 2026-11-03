using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.UI.PenEditor;

using Xunit;

using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorFixture;

namespace SemiPlot.Tests.Unit.UI.PenEditor;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
[Collection(ProcessGlobalStateCollection.Name)]
public sealed class PenRowFilterTests
{
	private static readonly StoredGroup[] _groups = [Chamber, Gas];

	[AvaloniaTheory]
	[InlineData("")]
	[InlineData("   ")]
	public void ABlankText_MatchesEveryRow(string filterText)
	{
		MatchingIds(filterText).Should().Equal(3, 7, 12);
	}

	[AvaloniaTheory]
	[InlineData("1", new[] { 12 })]
	[InlineData("2", new[] { 12 })]
	[InlineData("7", new[] { 7 })]
	[InlineData("power", new[] { 12 })]
	[InlineData("flow", new[] { 3 })]
	[InlineData("gas", new[] { 3 })]
	[InlineData("chamber", new[] { 3, 7 })]
	[InlineData("ber, Ga", new[] { 3 })]
	public void TheText_MatchesTheIdTheNameOrTheGroupsAnywhereInside(string filterText, int[] expectedIds)
	{
		MatchingIds(filterText).Should().Equal(expectedIds);
	}

	[AvaloniaFact]
	public void TheMatch_IgnoresCaseInLatinAndCyrillic()
	{
		var chamberPressure = new PenRowViewModel(
			Pressure with { Name = "\u0414\u0430\u0432\u043b\u0435\u043d\u0438\u0435 \u043a\u0430\u043c\u0435\u0440\u044b" },
			_groups);

		PenRowFilter.Matches(new PenRowViewModel(Argon, _groups), "ARGON").Should().BeTrue();
		PenRowFilter.Matches(chamberPressure, "\u0414\u0410\u0412\u041b\u0415\u041d\u0418\u0415").Should().BeTrue();
		PenRowFilter.Matches(chamberPressure, "\u041a\u0410\u041c\u0415\u0420\u042b").Should().BeTrue();
	}

	// Under Turkish casing the capital of 'i' is the dotted capital, which an ordinal comparison would not fold.
	[AvaloniaFact]
	public void TheMatch_FoldsCaseByTheCurrentCulture()
	{
		using var cultureScope = new CultureScope("tr-TR");

		PenRowFilter.Matches(new PenRowViewModel(Power, _groups), "B\u0130AS").Should().BeTrue();
	}

	[AvaloniaFact]
	public void TheText_IsTrimmedAtBothEnds()
	{
		MatchingIds("  argon  ").Should().Equal(3);
	}

	[AvaloniaFact]
	public void TheUnit_IsNotMatched()
	{
		MatchingIds("sccm").Should().BeEmpty();
	}

	private static IEnumerable<int> MatchingIds(string filterText)
	{
		return new[] { Argon, Pressure, Power }
			.Select(pen => new PenRowViewModel(pen, _groups))
			.Where(row => PenRowFilter.Matches(row, filterText))
			.Select(row => row.Pen.Id);
	}
}
