using System.Diagnostics;
using System.Reflection;

using FluentResults;

namespace SemiPlot.UI.Startup;

/// <summary>
/// Starts a copy of the running process with the launch keys it was started with
/// (docs/architecture/overview.md#another-instance).
/// </summary>
public sealed class InstanceLauncher
{
	private const string DotnetHostName = "dotnet";

	private readonly StartupOptions _options;
	private readonly string? _processPath;
	private readonly string? _entryAssemblyPath;
	private readonly Action<ProcessStartInfo> _start;

	public InstanceLauncher(StartupOptions options)
		: this(options, Environment.ProcessPath, Assembly.GetEntryAssembly()?.Location, StartProcess)
	{
	}

	internal InstanceLauncher(
		StartupOptions options,
		string? processPath,
		string? entryAssemblyPath,
		Action<ProcessStartInfo> start)
	{
		_options = options;
		_processPath = processPath;
		_entryAssemblyPath = entryAssemblyPath;
		_start = start;
	}

	/// <summary>The configuration directory the copy reads, which the settings window of this process writes.</summary>
	public string ConfigDirectory => _options.ConfigDir;

	/// <summary>A failed result when no host can be named or the process does not start.</summary>
	public Result Start()
	{
		if (string.IsNullOrEmpty(_processPath) || NeedsEntryAssembly(_processPath))
		{
			return Result.Fail(new InstanceHostUnknownError());
		}

		try
		{
			_start(BuildStartInfo(_processPath, _entryAssemblyPath, _options));

			return Result.Ok();
		}
		catch (Exception startFailure)
		{
			return Result.Fail(new ExceptionalError(startFailure));
		}
	}

	internal static ProcessStartInfo BuildStartInfo(
		string processPath,
		string? entryAssemblyPath,
		StartupOptions options)
	{
		var startInfo = new ProcessStartInfo(processPath) { UseShellExecute = false };

		if (IsDotnetHost(processPath) && !string.IsNullOrEmpty(entryAssemblyPath))
		{
			startInfo.ArgumentList.Add(entryAssemblyPath);
		}

		startInfo.ArgumentList.Add(StartupOptions.ConfigDirKey);
		startInfo.ArgumentList.Add(options.ConfigDir);
		startInfo.ArgumentList.Add(StartupOptions.LogFileKey);
		startInfo.ArgumentList.Add(options.LogFilePath);
		startInfo.ArgumentList.Add(StartupOptions.LoggingLevelKey);
		startInfo.ArgumentList.Add(options.LoggingLevel.ToString());

		return startInfo;
	}

	private bool NeedsEntryAssembly(string processPath)
	{
		return IsDotnetHost(processPath) && string.IsNullOrEmpty(_entryAssemblyPath);
	}

	private static bool IsDotnetHost(string processPath)
	{
		return string.Equals(
			Path.GetFileNameWithoutExtension(processPath), DotnetHostName, StringComparison.OrdinalIgnoreCase);
	}

	private static void StartProcess(ProcessStartInfo startInfo)
	{
		using var started = Process.Start(startInfo);
	}
}
