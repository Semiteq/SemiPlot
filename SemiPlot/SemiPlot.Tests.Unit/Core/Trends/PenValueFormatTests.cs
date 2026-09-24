using System.Globalization;

using AwesomeAssertions;

using SemiPlot.Core.Trends;

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

	// A trailing or pre-decimal comma is the scaling specifier, not the grouping one: it divides the
	// reading by a thousand per comma, so it is rejected for the same reason % is.
	[Theory]
	[InlineData("0,", 1234.5678, "1")]
	[InlineData("0,,", 1234.5678, "0")]
	[InlineData("0,.0", 1234.5678, "1.2")]
	public void AScalingCommaWouldRenderAWrongNumber(string mask, double value, string scaled)
	{
		UnderTheInvariantCulture(() =>
		{
			value.ToString(mask, CultureInfo.InvariantCulture).Should().Be(scaled);
			PenValueFormat.Format(value, mask).Should().NotBe(scaled);
		});
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

	// Format renders under the operator's culture, so the shapes above are stated in one this machine
	// cannot move.
	private static void UnderTheInvariantCulture(Action assertion)
	{
		var previous = CultureInfo.CurrentCulture;

		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

			assertion();
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}
}
