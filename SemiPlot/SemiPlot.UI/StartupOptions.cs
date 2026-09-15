using FluentResults;

using Serilog.Events;

namespace SemiPlot.UI;

public sealed record StartupOptions(
	string ConfigDir,
	string LogFilePath,
	LogEventLevel LoggingLevel)
{
	public const string ConfigDirKey = "--config-dir";

	public const string LogFileKey = "--log-file";

	public const string LoggingLevelKey = "--logging-level";

	/// <summary>What ParseLogLevel accepts.</summary>
	public const string LoggingLevelValues =
		"verbose, debug, info, information, warning, error, fatal";

	public static Result<StartupOptions> Parse(string[] args)
	{
		string? configDir = null;
		string? logFilePath = null;
		LogEventLevel? loggingLevel = null;

		for (var i = 0; i < args.Length; i++)
		{
			var key = args[i];

			if (key is not (ConfigDirKey or LogFileKey or LoggingLevelKey))
			{
				return Fail(StartupArgumentsProblem.Unknown, key);
			}

			if (i + 1 >= args.Length)
			{
				return Fail(StartupArgumentsProblem.ValueMissing, key);
			}

			var value = args[++i];

			if (string.IsNullOrWhiteSpace(value))
			{
				return Fail(StartupArgumentsProblem.ValueMissing, key);
			}

			switch (key)
			{
				case ConfigDirKey:
					configDir = value;
					break;

				case LogFileKey:
					logFilePath = value;
					break;

				default:
					loggingLevel = ParseLogLevel(value);

					if (loggingLevel is null)
					{
						return Fail(StartupArgumentsProblem.ValueInvalid, key, LoggingLevelValues);
					}

					break;
			}
		}

		return Compose(configDir, logFilePath, loggingLevel);
	}

	private static Result<StartupOptions> Compose(
		string? configDir,
		string? logFilePath,
		LogEventLevel? loggingLevel)
	{
		if (configDir is null)
		{
			return Fail(StartupArgumentsProblem.Missing, ConfigDirKey);
		}

		if (logFilePath is null)
		{
			return Fail(StartupArgumentsProblem.Missing, LogFileKey);
		}

		if (loggingLevel is null)
		{
			return Fail(StartupArgumentsProblem.Missing, LoggingLevelKey);
		}

		return Result.Ok(new StartupOptions(configDir, logFilePath, loggingLevel.Value));
	}

	private static Result<StartupOptions> Fail(
		StartupArgumentsProblem kind,
		string key,
		string acceptedValues = "")
	{
		return Result.Fail<StartupOptions>(new StartupArgumentsError(kind, key, acceptedValues));
	}

	private static LogEventLevel? ParseLogLevel(string value)
	{
		return value.ToLowerInvariant() switch
		{
			"verbose" => LogEventLevel.Verbose,
			"debug" => LogEventLevel.Debug,
			"info" or "information" => LogEventLevel.Information,
			"warning" => LogEventLevel.Warning,
			"error" => LogEventLevel.Error,
			"fatal" => LogEventLevel.Fatal,
			_ => null
		};
	}
}
