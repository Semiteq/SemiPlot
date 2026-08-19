using System.Globalization;

using FluentResults;

using SemiPlot.UI.Startup;

using Serilog;
using Serilog.Events;

namespace SemiPlot.UI;

public static class Program
{
	/// <summary>
	/// Exit code of a start that never drew the main window, so a launcher or a service wrapper can tell
	/// a failed start from an operator closing the application.
	/// </summary>
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
				// The error window replaces the main window; it never precedes it. Once Avalonia is
				// initialised a second BuildAvaloniaApp() throws, so this branch returns rather than
				// falling through to App.Run.
				//
				// Only the first error opens a window, and every error is logged. The probe short-circuits
				// on the first failed step, so the list holds one entry on every path it produces; the
				// loop covers a future step that collects more, and the window stays one state.
				LogStartupFailure(startup.Errors);
				App.RunErrorWindow(StartupFailureMapper.Map(startup.Errors[0]));

				return FailedExitCode;
			}

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
			Log.CloseAndFlushAsync().GetAwaiter().GetResult();
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
		const string Template =
			"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

		var invariant = CultureInfo.InvariantCulture;

		var configuration =
			new LoggerConfiguration()
				.MinimumLevel.Is(logLevel)
				.Enrich.FromLogContext()
				.WriteTo.Console(outputTemplate: Template, formatProvider: invariant);

		if (EnsureLogDirExists(logFilePath))
		{
			configuration = configuration.WriteTo.File(
				path: logFilePath,
				rollingInterval: RollingInterval.Infinite,
				fileSizeLimitBytes: 5 * 1024 * 1024,
				rollOnFileSizeLimit: true,
				retainedFileCountLimit: 5,
				shared: true,
				outputTemplate: Template,
				formatProvider: invariant);
		}

		Log.Logger = configuration.CreateLogger();
	}

	private static bool EnsureLogDirExists(string filePath)
	{
		try
		{
			var directory = Path.GetDirectoryName(filePath);
			if (directory is not null)
			{
				Directory.CreateDirectory(directory);
			}

			return true;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine(
				$"Failed to create log directory for '{filePath}': {ex.Message}. File logging is disabled.");

			return false;
		}
	}
}
