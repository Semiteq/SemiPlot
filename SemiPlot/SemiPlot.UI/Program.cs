using System.Globalization;

using FluentResults;

using SemiPlot.UI.Startup;

using Serilog;
using Serilog.Events;

namespace SemiPlot.UI;

public static class Program
{
	public const int FailedExitCode = 1;

	[STAThread]
	public static int Main(string[] args)
	{
		var options = StartupOptions.Parse(args);

		CreateLogger(options.LogFilePath, options.LoggingLevel);

		try
		{
			var startup = StartupProbe.Run(options);

			if (startup.IsFailed)
			{
				LogStartupFailure(startup.Errors);
				App.RunErrorWindow(StartupFailureMapper.Map(startup.Errors[0]));

				return FailedExitCode;
			}

			// Held for its disposal alone: the scope closes when Main returns, after App.Run.
			using var serviceProvider = startup.Value.ServiceProvider;

			App.Run(startup.Value);

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
		const string template =
			"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

		var invariant = CultureInfo.InvariantCulture;

		var configuration =
			new LoggerConfiguration()
				.MinimumLevel.Is(logLevel)
				.Enrich.FromLogContext()
				.WriteTo.Console(outputTemplate: template, formatProvider: invariant);

		configuration = configuration.WriteTo.File(
			path: logFilePath,
			rollingInterval: RollingInterval.Infinite,
			fileSizeLimitBytes: 5 * 1024 * 1024,
			rollOnFileSizeLimit: true,
			retainedFileCountLimit: 5,
			shared: true,
			outputTemplate: template,
			formatProvider: invariant);

		Log.Logger = configuration.CreateLogger();
	}
}
