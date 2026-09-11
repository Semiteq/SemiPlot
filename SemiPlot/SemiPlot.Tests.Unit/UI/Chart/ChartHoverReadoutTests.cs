using System.Globalization;

using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartHoverReadoutTests
{
	private static readonly DateTime _cursor = new(2026, 6, 15, 8, 1, 0, DateTimeKind.Utc);

	[Fact]
	public void BuildContent_ShowsEveryVisiblePenValueAtCursorPlusTimestamp()
	{
		var pens = new[] { CreatePen(1, "Pen 1"), CreatePen(2, "Pen 2") };
		var values = new Dictionary<int, double?> { [1] = 2.0, [2] = 20.0 };

		var content = ChartHoverReadout.BuildContent(_cursor, values, pens);

		content.Should().Contain("Pen 1: 2").And.Contain("Pen 2: 20");
		content.Should().Contain(LocalTimestamp(_cursor));
	}

	[Fact]
	public void BuildContent_PenWithGapAtCursor_RendersTheNoValuePlaceholderForThatPen()
	{
		var pens = new[] { CreatePen(1, "Pen 1"), CreatePen(2, "Pen 2") };
		var values = new Dictionary<int, double?> { [1] = 2.0, [2] = null };

		var content = ChartHoverReadout.BuildContent(_cursor, values, pens);

		content.Should().Contain("Pen 1: 2");
		content.Should().Contain($"Pen 2: {Resources.NoValuePlaceholder}");
	}

	[Fact]
	public void BuildContent_SkipsHiddenPens()
	{
		var visible = CreatePen(1, "Pen 1");
		var hidden = CreatePen(2, "Pen 2");
		hidden.IsVisible = false;
		var pens = new[] { visible, hidden };
		var values = new Dictionary<int, double?> { [1] = 2.0, [2] = 20.0 };

		var content = ChartHoverReadout.BuildContent(_cursor, values, pens);

		content.Should().Contain("Pen 1");
		content.Should().NotContain("Pen 2");
	}

	[Fact]
	public void BuildContent_NoPens_RendersTimestampOnly()
	{
		var values = new Dictionary<int, double?>();

		var content = ChartHoverReadout.BuildContent(_cursor, values, []);

		content.Should().Be(LocalTimestamp(_cursor));
	}

	[Fact]
	public void BuildContent_AllPensHidden_RendersTimestampOnly()
	{
		var first = CreatePen(1, "Pen 1");
		var second = CreatePen(2, "Pen 2");
		first.IsVisible = false;
		second.IsVisible = false;
		var pens = new[] { first, second };
		var values = new Dictionary<int, double?> { [1] = 2.0, [2] = 20.0 };

		var content = ChartHoverReadout.BuildContent(_cursor, values, pens);

		content.Should().Be(LocalTimestamp(_cursor));
	}

	[Fact]
	public void BuildContent_PenMissingFromValues_RendersTheNoValuePlaceholder()
	{
		var pens = new[] { CreatePen(1, "Pen 1") };
		var values = new Dictionary<int, double?>();

		var content = ChartHoverReadout.BuildContent(_cursor, values, pens);

		content.Should().Contain($"Pen 1: {Resources.NoValuePlaceholder}");
	}

	private static string LocalTimestamp(DateTime cursorUtc)
	{
		return cursorUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
	}

	private static TrendPenState CreatePen(int projectVarId, string name)
	{
		return new TrendPenState(new Pen(projectVarId, name, "Group", "#ff0000"), new EnvelopeLine());
	}
}
