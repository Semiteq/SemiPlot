using FluentResults;

namespace SemiPlot.UI.Startup;

/// <summary>
/// Opens the log file before the logger is built. Serilog's file sink reports its own open failure
/// only to Serilog.Debugging.SelfLog and then writes nowhere, which on a windowed executable is a
/// silent start with no log at all.
/// </summary>
public static class LogFileTarget
{
	public static Result Prepare(string logFilePath)
	{
		try
		{
			var directory = Path.GetDirectoryName(Path.GetFullPath(logFilePath));

			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}

			using var file = new FileStream(
				logFilePath,
				FileMode.Append,
				FileAccess.Write,
				FileShare.ReadWrite | FileShare.Delete);

			return Result.Ok();
		}
		catch (Exception exception) when (
			exception is IOException
				or UnauthorizedAccessException
				or ArgumentException
				or NotSupportedException)
		{
			return Result.Fail(new LogFileError(logFilePath, exception.Message));
		}
	}
}
