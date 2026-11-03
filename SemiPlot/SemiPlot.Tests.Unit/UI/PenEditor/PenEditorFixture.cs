using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.PenEditor;

namespace SemiPlot.Tests.Unit.UI.PenEditor;

/// <summary>The stored pens and groups the editor tests share, and numbered pens for a catalogue of any size.</summary>
internal static class PenEditorFixture
{
	public static readonly StoredPen Pressure = new(
		7, "Chamber pressure", "Pa", "0.00", "#1F77B4", PenLineStyle.Interpolated, true, 0, 100, false);

	public static readonly StoredPen Argon = new(
		3, "Argon flow", "sccm", null, "#FF7F0E", PenLineStyle.Stepped, false, null, null, false);

	public static readonly StoredPen Power = new(
		12, "Bias power", "W", null, null, PenLineStyle.Interpolated, true, 0, 500, false);

	public static readonly StoredGroup Chamber = new(2, "Chamber", [3, 7]);

	public static readonly StoredGroup Gas = new(1, "Gas", [3]);

	public static readonly StoredGroup Spare = new(5, "Spare", []);

	/// <summary>The catalogue the realised-window tests open the editor over.</summary>
	public static readonly PenCatalogue ViewCatalogue = new([Argon, Pressure, Power], [Chamber, Spare]);

	/// <summary>A pen named by its number, hidden on start, with no unit, mask or scale.</summary>
	public static StoredPen NumberedPen(int id)
	{
		return new StoredPen(id, $"{id}", null, null, "#2CA02C", PenLineStyle.Interpolated, false, null, null, false);
	}

	/// <summary>Pens 1 to <paramref name="count"/>, each a <see cref="NumberedPen"/>.</summary>
	public static List<StoredPen> PensNumbered(int count)
	{
		return [.. Enumerable.Range(1, count).Select(NumberedPen)];
	}

	/// <summary>Empty groups 1 to <paramref name="count"/>, each named "Group" and its number.</summary>
	public static List<StoredGroup> GroupsNumbered(int count)
	{
		return [.. Enumerable.Range(1, count).Select(id => new StoredGroup(id, $"Group {id}", []))];
	}

	/// <summary>The rows of the editor's pen list in the order its view shows them.</summary>
	public static PenRowViewModel[] RowsOf(PenEditorViewModel editor)
	{
		return [.. editor.PenRows.Cast<PenRowViewModel>()];
	}
}
