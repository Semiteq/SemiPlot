using System.Globalization;

namespace SemiPlot.UI.PenEditor;

/// <summary>The pen list's filter: a row matches when its id, name or groups contain the trimmed text.</summary>
public static class PenRowFilter
{
	public static bool Matches(PenRowViewModel row, string? filterText)
	{
		if (string.IsNullOrWhiteSpace(filterText))
		{
			return true;
		}

		var text = filterText.Trim();

		return Contains(row.Pen.Id.ToString(CultureInfo.InvariantCulture), text)
			|| Contains(row.Pen.Name, text)
			|| Contains(row.GroupsText, text);
	}

	private static bool Contains(string field, string text)
	{
		return field.Contains(text, StringComparison.CurrentCultureIgnoreCase);
	}
}
