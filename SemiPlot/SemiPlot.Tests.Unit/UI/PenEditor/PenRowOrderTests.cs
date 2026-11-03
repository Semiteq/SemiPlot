using System.Collections;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.UI.PenEditor;

using Xunit;

using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorFixture;

namespace SemiPlot.Tests.Unit.UI.PenEditor;

[Trait("Component", "UI")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class PenRowOrderTests
{
	private static readonly StoredGroup[] _groups = [Chamber, Gas];

	public static TheoryData<string, int[]> Orders => new()
	{
		{ nameof(PenRowOrder.ById), [3, 7, 12] },
		{ nameof(PenRowOrder.ByName), [3, 12, 7] },
		{ nameof(PenRowOrder.ByUnit), [7, 3, 12] },
		{ nameof(PenRowOrder.ByEnabledOnStart), [3, 7, 12] },
		{ nameof(PenRowOrder.ByGroups), [12, 7, 3] },
	};

	[AvaloniaTheory]
	[MemberData(nameof(Orders))]
	public void EachOrder_SortsByItsKeyThenById(string order, int[] expectedIds)
	{
		var rows = RowsOf(Argon, Pressure, Power);

		IdsOf(Sorted(rows, OrderNamed(order))).Should().Equal(expectedIds);
	}

	[AvaloniaFact]
	public void TheTextOrders_IgnoreCase()
	{
		var beta = NumberedPen(1) with { Name = "Beta" };
		var alpha = NumberedPen(2) with { Name = "alpha" };
		var upper = NumberedPen(3) with { Name = "Same" };
		var lower = NumberedPen(4) with { Name = "same" };

		IdsOf(Sorted(RowsOf(beta, alpha), PenRowOrder.ByName)).Should().Equal(2, 1);
		IdsOf(Sorted(RowsOf(lower, upper), PenRowOrder.ByName)).Should().Equal(3, 4);
	}

	private static PenRowViewModel[] RowsOf(params StoredPen[] pens)
	{
		return [.. pens.Select(pen => new PenRowViewModel(pen, _groups))];
	}

	private static PenRowViewModel[] Sorted(PenRowViewModel[] rows, IComparer comparer)
	{
		var sorted = (PenRowViewModel[])rows.Clone();
		Array.Sort(sorted, comparer);

		return sorted;
	}

	private static IEnumerable<int> IdsOf(IEnumerable<PenRowViewModel> rows)
	{
		return rows.Select(row => row.Pen.Id);
	}

	private static IComparer OrderNamed(string order)
	{
		return order switch
		{
			nameof(PenRowOrder.ById) => PenRowOrder.ById,
			nameof(PenRowOrder.ByName) => PenRowOrder.ByName,
			nameof(PenRowOrder.ByUnit) => PenRowOrder.ByUnit,
			nameof(PenRowOrder.ByEnabledOnStart) => PenRowOrder.ByEnabledOnStart,
			nameof(PenRowOrder.ByGroups) => PenRowOrder.ByGroups,
			_ => throw new ArgumentOutOfRangeException(nameof(order), order, null)
		};
	}
}
