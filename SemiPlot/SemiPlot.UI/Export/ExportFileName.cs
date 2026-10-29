using System.Globalization;

namespace SemiPlot.UI.Export;

/// <summary>The save dialog's proposed name: docs/architecture/overview.md#the-export-window</summary>
internal static class ExportFileName
{
	private const string RangeStampFormat = "yyyyMMdd_HHmmss";
	private const string MarkerStampFormat = "yyyy-MM-dd_HH-mm";
	private const string CsvExtension = ".csv";
	private const string RefusedNameCharacters = "\\/:*?\"<>|";
	private const int MaxStemLength = 200;

	/// <summary>Named from the label of the start box's marker, else from the range.</summary>
	public static string Suggest(DateTime from, DateTime to, string? startMarkerLabel)
	{
		if (FileStem(startMarkerLabel) is { } stem)
		{
			return $"{stem}_{from.ToString(MarkerStampFormat, CultureInfo.InvariantCulture)}{CsvExtension}";
		}

		var start = from.ToString(RangeStampFormat, CultureInfo.InvariantCulture);
		var end = to.ToString(RangeStampFormat, CultureInfo.InvariantCulture);

		return $"semiplot_{start}-{end}{CsvExtension}";
	}

	/// <summary>Null for a label the cut leaves blank.</summary>
	private static string? FileStem(string? label)
	{
		if (label is null)
		{
			return null;
		}

		var trimmed = label.Trim();
		var stem = trimmed.EndsWith(CsvExtension, StringComparison.OrdinalIgnoreCase)
			? trimmed[..^CsvExtension.Length].TrimEnd()
			: trimmed;

		return stem.Length == 0 ? null : new string([.. Capped(stem).Select(ToFileNameCharacter)]);
	}

	/// <summary>Keeps the name with its stamp suffix under the 255 characters a file name may hold.</summary>
	private static string Capped(string stem)
	{
		if (stem.Length <= MaxStemLength)
		{
			return stem;
		}

		var length = char.IsHighSurrogate(stem[MaxStemLength - 1]) ? MaxStemLength - 1 : MaxStemLength;

		return stem[..length].TrimEnd();
	}

	private static char ToFileNameCharacter(char character)
	{
		return char.IsControl(character) || RefusedNameCharacters.Contains(character) ? '_' : character;
	}
}
