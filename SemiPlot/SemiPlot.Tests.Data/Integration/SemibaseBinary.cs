using FluentResults;

namespace SemiPlot.Tests.Data.Integration;

// Names the semibase executable the SEMIPLOT_TEST_PG path spawns. SEMIBASE_EXE is the only source and
// nothing searches PATH. The container path resolves nothing from the machine at all, because the
// image carries its own provisioner.
public static class SemibaseBinary
{
	public static Result<string> Resolve()
	{
		if (TestEnvironment.SemibaseExecutable is not { } configuredPath)
		{
			return Result.Fail<string>(
				$"{TestEnvironment.SemibaseExecutableVariable} is not set: {TestEnvironment.TestServerVariable} "
					+ "names a server this suite provisions by spawning semibase, so the variable must point at "
					+ "the binary.");
		}

		return File.Exists(configuredPath)
			? Result.Ok(Path.GetFullPath(configuredPath))
			: Result.Fail<string>(
				$"{TestEnvironment.SemibaseExecutableVariable} points at '{configuredPath}', "
					+ "which does not exist.");
	}
}
