using FluentResults;

namespace SemiPlot.UI.Startup;

/// <summary>The path given by --log-file could not be opened for writing.</summary>
public sealed class LogFileError(string path, string reason)
	: Error($"The log file '{path}' could not be opened: {reason}")
{
	public string FilePath { get; } = path;

	public string Reason { get; } = reason;
}
