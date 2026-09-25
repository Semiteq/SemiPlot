using System.Globalization;

using FluentResults;

using SemiPlot.UI.Startup;

using Serilog;
using Serilog.Events;

namespace SemiPlot.UI;

public static class Program
{
	private const int FailedExitCode = 1;

	[STAThread]
	public static int Main(string[] args)
	{
		var options = StartupOptions.Parse(args);

		if (options.IsFailed)
		{
			return ReportStartupFailure(options.Errors);
		}

		var logFile = LogFileTarget.Prepare(options.Value.LogFilePath);

		if (logFile.IsFailed)
		{
			return ReportStartupFailure(logFile.Errors);
		}

		CreateLogger(options.Value.LogFilePath, options.Value.LoggingLevel);
		LogStart(options.Value);

		try
		{
			var (settings, startup) = StartupSequence.Run(options.Value);

			if (startup.IsFailed)
			{
				LogStartupFailure(startup.Errors);
				App.Run(settings, startup, options.Value.ConfigDir);

				return FailedExitCode;
			}

			// Held for its disposal alone: the scope closes when Main returns, after App.Run.
			using var serviceProvider = startup.Value.ServiceProvider;

			App.Run(settings, startup, options.Value.ConfigDir);

			return 0;
		}
		catch (Exception ex)
		{
			Log.Fatal(ex, "Application terminated unexpectedly");

			return FailedExitCode;
		}
		finally
		{
			Log.CloseAndFlush();
		}
	}

	// docs/architecture/data-integration.md#startup
	private static int ReportStartupFailure(IReadOnlyList<IError> errors)
	{
		StartupSequence.ApplyBootstrapCulture();

		App.Run(null, Result.Fail<StartupData>(errors), configDirectory: null);

		return FailedExitCode;
	}

	private static void LogStart(StartupOptions options)
	{
		Log.Information(
			"SemiPlot starting; configuration {ConfigDir}, logging level {LoggingLevel}",
			options.ConfigDir,
			options.LoggingLevel);
	}

	private static void LogStartupFailure(IReadOnlyList<IError> errors)
	{
		Log.Fatal(
			"Application startup failed with {ErrorCount} error(s); the user interface was not started",
			errors.Count);

		foreach (var error in errors)
		{
			Log.Fatal("Startup error: {Error}", error.Message);
		}
	}

	private static void CreateLogger(string logFilePath, LogEventLevel logLevel)
	{
		const string Template =
			"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

		var invariant = CultureInfo.InvariantCulture;

		var configuration =
			new LoggerConfiguration()
				.MinimumLevel.Is(logLevel)
				.Enrich.FromLogContext()
				.WriteTo.Console(outputTemplate: Template, formatProvider: invariant);

		configuration = configuration.WriteTo.File(
			path: logFilePath,
			rollingInterval: RollingInterval.Infinite,
			fileSizeLimitBytes: 5 * 1024 * 1024,
			rollOnFileSizeLimit: true,
			retainedFileCountLimit: 5,
			shared: true,
			outputTemplate: Template,
			formatProvider: invariant);

		Log.Logger = configuration.CreateLogger();
	}
}
