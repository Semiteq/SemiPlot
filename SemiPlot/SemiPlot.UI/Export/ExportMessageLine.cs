using System.Globalization;

using SemiPlot.UI.Localization;

namespace SemiPlot.UI.Export;

internal static class ExportMessageLine
{
	/// <summary>The most data rows an Excel sheet takes below its header line.</summary>
	public const long ExcelDataRowLimit = 1_048_575;

	public static string ExcelNotice =>
		Resources.FormatExportExcelNotice(ExcelDataRowLimit.ToString("N0", CultureInfo.CurrentCulture));

	public static string Compose(ExportRangeProblem problem, string status, long? rowCount)
	{
		if (problem != ExportRangeProblem.None)
		{
			return ExportRangeProblemText.Of(problem);
		}

		if (status.Length > 0)
		{
			return status;
		}

		return rowCount > ExcelDataRowLimit ? ExcelNotice : string.Empty;
	}
}
