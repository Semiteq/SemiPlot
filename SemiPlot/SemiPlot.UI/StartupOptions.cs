using Serilog.Events;

namespace SemiPlot.UI;

public sealed record StartupOptions(
	string ConfigDir,
	string LogFilePath,
	LogEventLevel LoggingLevel,
	bool UseStub)
{
	public const string DefaultConfigDir =
		@"C:\DISTR\Config\SemiPlot";

	public const string DefaultLogFilePath =
		@"C:\DISTR\Logs\SemiPlot\semiplot.log";

	public const LogEventLevel DefaultLoggingLevel =
		LogEventLevel.Warning;

	public const bool DefaultUseStub = false;

	public static StartupOptions Parse(string[] args)
	{
		var configDir = DefaultConfigDir;
		var logFilePath = DefaultLogFilePath;
		var logLevel = DefaultLoggingLevel;
		var useStub = DefaultUseStub;

		for (var i = 0; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "--config-dir" when i + 1 < args.Length:
					configDir = args[++i];
					break;

				case "--log-file" when i + 1 < args.Length:
					logFilePath = args[++i];
					break;

				case "--logging-level" when i + 1 < args.Length:
					logLevel = ParseLogLevel(args[++i]);
					break;

				case "--use-stub":
					useStub = true;
					break;
			}
		}

		return new StartupOptions(
			configDir,
			logFilePath,
			logLevel,
			useStub);
	}

	// Parsing runs before CreateLogger, so an unrecognised level cannot be reported through Serilog and
	// goes to the standard error stream instead — the same route EnsureLogDirExists takes. Silence here
	// would leave an operator who mistyped the level reading Warning-level logs and no reason why.
	private static LogEventLevel ParseLogLevel(string value)
	{
		var level = value.ToLowerInvariant() switch
		{
			"verbose" => LogEventLevel.Verbose,
			"debug" => LogEventLevel.Debug,
			"info" or "information" => LogEventLevel.Information,
			"warning" => LogEventLevel.Warning,
			"error" => LogEventLevel.Error,
			"fatal" => LogEventLevel.Fatal,
			_ => (LogEventLevel?)null
		};

		if (level is null)
		{
			Console.Error.WriteLine(
				$"Unrecognised --logging-level '{value}'. Use verbose, debug, info, warning, error or "
				+ $"fatal. Falling back to {DefaultLoggingLevel}.");
		}

		return level ?? DefaultLoggingLevel;
	}
}
