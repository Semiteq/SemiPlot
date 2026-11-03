using System.Collections;

namespace SemiPlot.UI.PenEditor;

/// <summary>The pen list's sort orders, one per sortable column, each by its key and then by pen id.</summary>
public static class PenRowOrder
{
	public static readonly IComparer ById =
		Comparer<PenRowViewModel>.Create((left, right) => left.Pen.Id.CompareTo(right.Pen.Id));

	public static readonly IComparer ByName = ThenById(ByText(row => row.Pen.Name));

	public static readonly IComparer ByUnit = ThenById(ByText(row => row.Pen.Unit));

	public static readonly IComparer ByEnabledOnStart =
		ThenById((left, right) => left.Pen.EnabledOnStart.CompareTo(right.Pen.EnabledOnStart));

	public static readonly IComparer ByGroups = ThenById(ByText(row => row.GroupsText));

	private static Comparer<PenRowViewModel> ThenById(Comparison<PenRowViewModel> byKey)
	{
		return Comparer<PenRowViewModel>.Create((left, right) =>
		{
			var byKeyOrder = byKey(left, right);

			return byKeyOrder != 0 ? byKeyOrder : left.Pen.Id.CompareTo(right.Pen.Id);
		});
	}

	private static Comparison<PenRowViewModel> ByText(Func<PenRowViewModel, string?> textOf)
	{
		return (left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(textOf(left), textOf(right));
	}
}
