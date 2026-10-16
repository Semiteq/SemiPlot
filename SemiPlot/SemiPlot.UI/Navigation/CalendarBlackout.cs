using Avalonia.Controls;

namespace SemiPlot.UI.Navigation;

/// <summary>Writes a day list into the blackout dates of the pickers whose template is applied.</summary>
internal static class CalendarBlackout
{
	public static void Apply(IReadOnlyList<DateTime> days, params ReadOnlySpan<CalendarDatePicker> pickers)
	{
		foreach (var picker in pickers)
		{
			if (picker.BlackoutDates is not { } blackoutDates)
			{
				continue;
			}

			blackoutDates.Clear();
			foreach (var day in days)
			{
				blackoutDates.Add(new CalendarDateRange(day));
			}
		}
	}
}
