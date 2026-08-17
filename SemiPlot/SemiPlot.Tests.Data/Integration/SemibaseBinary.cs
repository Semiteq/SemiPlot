using FluentResults;

namespace SemiPlot.Tests.Data.Integration;

// semibase provisions the container the same way it provisions a site, so this repository defines
// neither the roles nor their grants. It is a pinned release binary rather than a package, and it is
// discovered exactly like the container runtime: through SEMIBASE_EXE or on PATH, with its absence
// reported as an unavailable reason instead of thrown.
public static class SemibaseBinary
{
	// github.com/Semiteq/SemiBase, pinned so that a change there cannot fail this suite without a
	// version to blame. Bumping it is a deliberate edit here and in the CI job.
	public const string PinnedVersion = "v0.1.0";

	public const string WindowsFileName = "semibase.exe";

	public const string UnixFileName = "semibase";

	public static Result<string> Resolve()
	{
		return Resolve(TestEnvironment.SemibaseExecutable, PathDirectories());
	}

	public static Result<string> Resolve(string? configuredPath, IEnumerable<string> searchDirectories)
	{
		if (configuredPath is not null)
		{
			return File.Exists(configuredPath)
				? Result.Ok(Path.GetFullPath(configuredPath))
				: Result.Fail<string>(
					$"{TestEnvironment.SemibaseExecutableVariable} points at '{configuredPath}', "
						+ "which does not exist.");
		}

		foreach (var directory in searchDirectories)
		{
			foreach (var fileName in FileNames())
			{
				var candidate = Path.Combine(directory, fileName);

				if (File.Exists(candidate))
				{
					return Result.Ok(Path.GetFullPath(candidate));
				}
			}
		}

		return Result.Fail<string>(
			$"semibase was not found on PATH: download the {PinnedVersion} release binary from "
				+ $"github.com/Semiteq/SemiBase and point {TestEnvironment.SemibaseExecutableVariable} at it.");
	}

	private static IEnumerable<string> FileNames()
	{
		return OperatingSystem.IsWindows() ? [WindowsFileName, UnixFileName] : [UnixFileName];
	}

	// A PATH entry may be quoted or carry a character no path can hold; either would throw out of
	// Path.Combine and turn a missing binary into a crash rather than a skip.
	private static IEnumerable<string> PathDirectories()
	{
		var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

		return path
			.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(entry => entry.Trim('"'))
			.Where(entry => entry.Length > 0 && entry.IndexOfAny(Path.GetInvalidPathChars()) < 0);
	}
}
