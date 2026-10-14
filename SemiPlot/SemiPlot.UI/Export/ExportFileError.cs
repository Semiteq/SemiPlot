using FluentResults;

namespace SemiPlot.UI.Export;

public sealed class ExportFileError(string path, string reason, bool isPartialFile = false)
	: Error(isPartialFile
		? $"The partial export file '{path}' could not be deleted: {reason}"
		: $"The export file '{path}' could not be written: {reason}")
{
	public string FilePath { get; } = path;

	/// <summary>The runtime's own message.</summary>
	public string Reason { get; } = reason;

	public bool IsPartialFile { get; } = isPartialFile;
}
