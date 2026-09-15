namespace SemiPlot.AppHost;

/// <summary>The temporary configuration and log directories the demo stand owns.</summary>
public sealed class DemoDirectories
{
	private const string RootFolderName = "SemiPlot";

	private const string ConfigurationFolderName = "ConfigFiles";

	private const string LogFolderName = "Logs";

	private const string LogFileName = "semiplot.log";

	private DemoDirectories(string configurationDirectory, string logDirectory)
	{
		ConfigurationDirectory = configurationDirectory;
		LogDirectory = logDirectory;
	}

	public string ConfigurationDirectory { get; }

	public string LogDirectory { get; }

	public string LogFilePath => Path.Combine(LogDirectory, LogFileName);

	/// <summary>Sweeps both directories, recreates them and copies the tracked set into the first.</summary>
	public static DemoDirectories Prepare(string trackedConfigurationDirectory)
	{
		if (!Directory.Exists(trackedConfigurationDirectory))
		{
			throw new DirectoryNotFoundException(
				$"The tracked configuration set is missing: {trackedConfigurationDirectory}");
		}

		var root = Path.Combine(Path.GetTempPath(), RootFolderName);
		var directories = new DemoDirectories(
			Path.Combine(root, ConfigurationFolderName),
			Path.Combine(root, LogFolderName));

		Sweep(directories.ConfigurationDirectory);
		RequireSwept(directories.ConfigurationDirectory);
		Sweep(directories.LogDirectory);

		Create(directories.ConfigurationDirectory);
		Create(directories.LogDirectory);
		CopyInto(trackedConfigurationDirectory, directories.ConfigurationDirectory);

		return directories;
	}

	/// <summary>Removes the copy the stand consumed, leaving the log of the run behind.</summary>
	public void RemoveConfiguration()
	{
		Sweep(ConfigurationDirectory);
	}

	// A viewer that outlived its stand still holds semiplot.log open, and that must not stop the
	// next start: every deletion is attempted and a locked file is left where it is.
	private static void Sweep(string directory)
	{
		if (!Directory.Exists(directory))
		{
			return;
		}

		foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
		{
			TryIgnoringLocks(() => File.Delete(file));
		}

		TryIgnoringLocks(() => Directory.Delete(directory, recursive: true));
	}

	// A tolerated log file costs nothing, but a configuration file that survived the sweep would merge
	// with the fresh copy and fail the viewer on a key conflict nobody planted.
	private static void RequireSwept(string directory)
	{
		if (!Directory.Exists(directory))
		{
			return;
		}

		var left = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);

		if (left.Length > 0)
		{
			throw new IOException(
				$"The previous configuration copy could not be removed: {string.Join(", ", left)}");
		}
	}

	// Windows keeps a deleted directory in place while another process still holds a handle inside it,
	// and creating it again during that window throws; the deletion completes on its own shortly after.
	private static void Create(string directory)
	{
		const int Attempts = 10;
		const int PauseMilliseconds = 100;

		for (var attempt = 1; ; attempt++)
		{
			try
			{
				Directory.CreateDirectory(directory);

				return;
			}
			catch (Exception exception) when (
				attempt < Attempts && exception is IOException or UnauthorizedAccessException)
			{
				Thread.Sleep(PauseMilliseconds);
			}
		}
	}

	private static void TryIgnoringLocks(Action deletion)
	{
		try
		{
			deletion();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}

	private static void CopyInto(string source, string destination)
	{
		foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
		{
			Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
		}

		foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
		{
			File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
		}
	}
}
