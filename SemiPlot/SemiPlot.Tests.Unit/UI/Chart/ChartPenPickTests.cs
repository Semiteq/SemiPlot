using AwesomeAssertions;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartPenPickTests
{
	public static TheoryData<(int PenId, double Distance)[], int, int?> Cases => new()
	{
		{ [(1, 5.0), (2, 2.0), (3, 4.0)], 1, 2 },
		{ [(1, 3.0), (2, 3.0)], 2, 2 },
		{ [(1, 3.0), (2, 3.0)], 1, 1 },
		{ [(7, 3.0), (4, 3.0), (9, 3.0)], 1, 7 },
		{ [(1, 6.5), (2, 20.0)], 1, null },
		{ [(1, 6.0), (2, 9.0)], 2, 1 },
		{ [], 1, null }
	};

	[Theory]
	[MemberData(nameof(Cases))]
	public void Choose_PicksTheNearestWithinToleranceAndOnATieTheActiveThenTheFirst(
		(int PenId, double Distance)[] candidates,
		int activePenId,
		int? expected)
	{
		ChartPenPick.Choose(candidates, activePenId).Should().Be(expected);
	}
}
