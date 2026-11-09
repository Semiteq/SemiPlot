using System.Collections;

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
public sealed class PenRowOrderTests
{
	private static readonly StoredGroup[] _groups = [Chamber, Gas];

	private static readonly Dictionary<string, (IComparer Order, int[] ExpectedIds)> _orders = new()
	{
		[nameof(PenRowOrder.ById)] = (PenRowOrder.ById, [3, 7, 12]),
		[nameof(PenRowOrder.ByName)] = (PenRowOrder.ByName, [3, 12, 7]),
		[nameof(PenRowOrder.ByUnit)] = (PenRowOrder.ByUnit, [7, 3, 12]),
		[nameof(PenRowOrder.ByEnabledOnStart)] = (PenRowOrder.ByEnabledOnStart, [3, 7, 12]),
		[nameof(PenRowOrder.ByGroups)] = (PenRowOrder.ByGroups, [12, 7, 3]),
	};

	public static TheoryData<string> Orders => [.. _orders.Keys];

	[AvaloniaTheory]
	[MemberData(nameof(Orders))]
	public void EachOrder_SortsByItsKeyThenById(string order)
	{
		var (comparer, expectedIds) = _orders[order];
		var rows = RowsOf(Power, Argon, Pressure);

		IdsOf(Sorted(rows, comparer)).Should().Equal(expectedIds);
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
}
