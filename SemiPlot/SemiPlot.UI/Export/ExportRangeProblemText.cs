using SemiPlot.UI.Localization;
using SemiPlot.UI.Navigation;

namespace SemiPlot.UI.Export;

internal static class ExportRangeProblemText
{
	public static string Of(ExportRangeProblem problem)
	{
		return problem switch
		{
			ExportRangeProblem.StartDateMissing => Resources.ExportMessageStartDateMissing,
			ExportRangeProblem.StartUnreadable =>
				Resources.FormatExportMessageStartUnreadable(WallClockRangeForm.TimeFieldFormat),
			ExportRangeProblem.EndDateMissing => Resources.ExportMessageEndDateMissing,
			ExportRangeProblem.EndUnreadable =>
				Resources.FormatExportMessageEndUnreadable(WallClockRangeForm.TimeFieldFormat),
			ExportRangeProblem.EndNotAfterStart => Resources.ExportMessageEndNotAfterStart,
			ExportRangeProblem.SpanOverLimit =>
				Resources.FormatExportMessageSpanOverLimit(ExportRangeForm.MaxSpan.TotalHours),
			ExportRangeProblem.NoPenTicked => Resources.ExportMessageNoPenTicked,
			_ => throw new ArgumentOutOfRangeException(nameof(problem), problem, null)
		};
	}
}
