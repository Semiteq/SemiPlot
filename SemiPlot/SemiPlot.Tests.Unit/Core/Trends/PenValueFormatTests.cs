using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Trends;

// The mask rule is a character and section rule rather than a try over ToString, because .NET throws
// on almost no mask: it prints qqq literally, prints nothing for a zero under # or for any reading
// under ";0", multiplies by 100 under % and divides by 1000 per trailing comma.
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class PenValueFormatTests
{
	[Theory]
	[InlineData("0.##0")]
	[InlineData("0.#E0")]
	[InlineData("#,##0.00")]
	[InlineData("0.0;(0.0);-")]
	public void AMaskThatOnlyShapesTheReadingIsAcceptable(string mask)
	{
		PenValueFormat.IsAcceptable(mask).Should().BeTrue();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("qqq")]
	[InlineData("#")]
	[InlineData("%0.0")]
	[InlineData("0,")]
	[InlineData("0,,")]
	[InlineData("0,.0")]
	[InlineData(";0")]
	public void AMaskThatCannotRenderAReadingIsRejected(string? mask)
	{
		PenValueFormat.IsAcceptable(mask).Should().BeFalse();
	}

	[Theory]
	[InlineData("0.##0", 1234.5678, "1234.568")]
	[InlineData("0.##0", 0.0, "0.000")]
	[InlineData("0.#E0", 1234.5678, "1.2E3")]
	[InlineData("#,##0.00", 1234.5678, "1,234.57")]
	[InlineData("#,##0.00", -5.25, "-5.25")]
	[InlineData("0.0;(0.0);-", 0.0, "-")]
	[InlineData("0.0;(0.0);-", -5.25, "(5.3)")]
	public void AnAcceptableMaskRendersInItsOwnShape(string mask, double value, string expected)
	{
		UnderTheInvariantCulture(() => PenValueFormat.Format(value, mask).Should().Be(expected));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("qqq")]
	[InlineData("#")]
	[InlineData("%0.0")]
	[InlineData("0,")]
	[InlineData(";0")]
	public void ARejectedMaskRendersUnderTheFallback(string? mask)
	{
		UnderTheInvariantCulture(() =>
		{
			PenValueFormat.Format(1234.5678, mask).Should().Be("1234.568");
			PenValueFormat.Format(0.0, mask).Should().Be("0");
		});
	}

	// A difference is often negative and often zero, so it renders under the positive section alone: a
	// reading's negative section may leave the sign out and its zero section may print a dash.
	[Theory]
	[InlineData("0.0;(0.0);-", -5.25, "-5.3")]
	[InlineData("0.0;(0.0);-", 0.0, "0.0")]
	[InlineData("0.0;0.0", -1.5, "-1.5")]
	[InlineData("0.00E+00", -1.2e-5, "-1.20E-05")]
	[InlineData("#,##0.00", 1234.5678, "1,234.57")]
	[InlineData(null, -1.5, "-1.5")]
	[InlineData("qqq", 0.0, "0")]
	public void ADifferenceRendersUnderThePositiveSectionOfTheMask(string? mask, double value, string expected)
	{
		UnderTheInvariantCulture(() => PenValueFormat.FormatDifference(value, mask).Should().Be(expected));
	}

	// Format renders under the operator's culture, so the shapes above are stated in one this machine
	// cannot move.
	private static void UnderTheInvariantCulture(Action assertion)
	{
		using var culture = new CultureScope(string.Empty);

		assertion();
	}
}
