namespace SemiPlot.Core.Trends;

public enum CalendarLabelKind
{
	Day,
	Month
}

/// <summary>A label under the calendar at <see cref="X"/>, naming its day or that day's month by its kind.</summary>
public readonly record struct CalendarLabel(double X, DateOnly Day, CalendarLabelKind Kind);

/// <summary>A shown candidate by its index, at the left edge the row gives it.</summary>
public readonly record struct PlacedLabel(int Index, double Left);

/// <summary>
/// The label row under the day calendar (docs/architecture/trend-interaction.md#archive-overview-minimap).
/// </summary>
public static class DayCalendarLabels
{
	// A month label stands clear of the separator before its first cell.
	private const double MonthLabelOffset = 2.0;
	private const double LabelGap = 4.0;

	/// <summary>
	/// The first cell's month, each month's first day, and each Monday on an archive that names its Mondays.
	/// </summary>
	public static IReadOnlyList<CalendarLabel> Candidates(DayCalendar calendar)
	{
		var labels = new List<CalendarLabel>();
		for (var index = 0; index < calendar.DayCount; index++)
		{
			var day = calendar.FirstDay.AddDays(index);
			var left = calendar.CellLeft(index);
			if (index == 0 || day.Day == 1)
			{
				labels.Add(new CalendarLabel(left + MonthLabelOffset, day, CalendarLabelKind.Month));
			}
			else if (calendar.ShowsMondayLabels && day.DayOfWeek == DayOfWeek.Monday)
			{
				labels.Add(new CalendarLabel(left, day, CalendarLabelKind.Day));
			}
		}

		return labels;
	}

	/// <summary>
	/// The candidates that show, by index: the first cell's label, then each month, then each Monday, keeps its
	/// place when it starts inside the row 4 px clear of every label kept before it; one that would run past the
	/// row ends at its right end.
	/// </summary>
	public static IReadOnlyList<PlacedLabel> Fitted(
		IReadOnlyList<CalendarLabel> candidates,
		IReadOnlyList<double> widths,
		double rowWidth)
	{
		var fitted = new List<PlacedLabel>();
		var byPriority = Enumerable.Range(0, candidates.Count).OrderBy(index => PriorityOf(candidates, index));
		foreach (var index in byPriority)
		{
			var left = Math.Min(candidates[index].X, rowWidth - widths[index]);
			var right = left + widths[index];
			var isClear = fitted.TrueForAll(kept =>
				left >= kept.Left + widths[kept.Index] + LabelGap || right + LabelGap <= kept.Left);
			if (left >= 0.0 && isClear)
			{
				fitted.Add(new PlacedLabel(index, left));
			}
		}

		fitted.Sort((first, second) => first.Index.CompareTo(second.Index));

		return fitted;
	}

	private static int PriorityOf(IReadOnlyList<CalendarLabel> candidates, int index)
	{
		return index == 0 ? 0 : candidates[index].Kind == CalendarLabelKind.Month ? 1 : 2;
	}
}
