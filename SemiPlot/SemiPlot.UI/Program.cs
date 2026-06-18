using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.DataSource.Stub;

using Serilog;
using Serilog.Events;

namespace SemiPlot.UI;

public static class Program
{
	private const LogEventLevel DefaultLoggingLevel = LogEventLevel.Information;

	[STAThread]
	public static void Main()
	{
		CreateLogger(ResolveLogFilePath(), DefaultLoggingLevel);

		try
		{
			using var serviceProvider = BuildServiceProvider();

			App.Run(serviceProvider);
		}
		catch (Exception ex)
		{
			Log.Fatal(ex, "Application terminated unexpectedly");
		}
		finally
		{
			Log.CloseAndFlushAsync().GetAwaiter().GetResult();
		}
	}

	private static ServiceProvider BuildServiceProvider()
	{
		var services =
			new ServiceCollection()
				.AddData()
				.AddUi();

		services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));

		return services.BuildServiceProvider();
	}

	private static string ResolveLogFilePath()
	{
		var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

		return Path.Combine(root, "SemiPlot", "Logs", "semiplot.log");
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
