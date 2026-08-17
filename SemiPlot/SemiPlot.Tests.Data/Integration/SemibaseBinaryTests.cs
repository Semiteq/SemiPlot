using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// Discovery goes through SEMIBASE_EXE or PATH and nowhere else: no machine-specific path may be
// compiled into the suite, since the same test project runs on a Linux CI runner.
[Collection(ProcessStateCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class SemibaseBinaryTests : IDisposable
{
	private const string PathVariable = "PATH";

	// '|' is an invalid path character on Windows and merely a name no directory carries on Unix, where
	// the only invalid character is one an environment variable cannot hold. Either way the entry has to
	// be stepped over rather than combined.
	private const string UnusablePathEntry = "bad|entry";

	private readonly string _searchDirectory =
		Path.Combine(Path.GetTempPath(), $"semibase-probe-{Guid.NewGuid():N}");

	public SemibaseBinaryTests()
	{
		Directory.CreateDirectory(_searchDirectory);
	}

	public void Dispose()
	{
		Directory.Delete(_searchDirectory, recursive: true);
	}

	[Fact]
	public void AConfiguredPathResolvesToTheBinaryItNames()
	{
		var executable = WriteExecutable(SemibaseBinary.UnixFileName);

		var resolved = SemibaseBinary.Resolve(executable, []);

		Assert.True(resolved.IsSuccess);
		Assert.Equal(executable, resolved.Value);
	}

	[Fact]
	public void AConfiguredPathThatDoesNotExistIsAStatedReason()
	{
		var missing = Path.Combine(_searchDirectory, "absent-binary");

		var resolved = SemibaseBinary.Resolve(missing, [_searchDirectory]);

		Assert.True(resolved.IsFailed);
		Assert.Contains(
			TestEnvironment.SemibaseExecutableVariable,
			resolved.Errors[0].Message,
			StringComparison.Ordinal);
		Assert.Contains(missing, resolved.Errors[0].Message, StringComparison.Ordinal);
	}

	// A configured path is taken as stated: falling back to PATH would run a different binary than the
	// one the caller named, and the version under test would stop being the pinned one.
	[Fact]
	public void AConfiguredPathIsNotRepairedFromTheSearchDirectories()
	{
		WriteExecutable(SemibaseBinary.UnixFileName);
		WriteExecutable(SemibaseBinary.WindowsFileName);

		var resolved = SemibaseBinary.Resolve(Path.Combine(_searchDirectory, "absent-binary"), [_searchDirectory]);

		Assert.True(resolved.IsFailed);
	}

	[Fact]
	public void ABinaryOnTheSearchPathIsFound()
	{
		var executable = WriteExecutable(
			OperatingSystem.IsWindows() ? SemibaseBinary.WindowsFileName : SemibaseBinary.UnixFileName);

		var resolved = SemibaseBinary.Resolve(configuredPath: null, [_searchDirectory]);

		Assert.True(resolved.IsSuccess);
		Assert.Equal(executable, resolved.Value);
	}

	[Fact]
	public void AnAbsentBinaryIsAReasonNamingBothWaysToSupplyIt()
	{
		var resolved = SemibaseBinary.Resolve(configuredPath: null, [_searchDirectory]);

		Assert.True(resolved.IsFailed);
		Assert.Contains(
			TestEnvironment.SemibaseExecutableVariable,
			resolved.Errors[0].Message,
			StringComparison.Ordinal);
		Assert.Contains("PATH", resolved.Errors[0].Message, StringComparison.Ordinal);
		Assert.Contains(SemibaseBinary.PinnedVersion, resolved.Errors[0].Message, StringComparison.Ordinal);
	}

	// A search directory that was removed must leave a missing binary a skip rather than an exception.
	[Fact]
	public void AnUnusableSearchEntryDoesNotThrow()
	{
		var resolved = SemibaseBinary.Resolve(
			configuredPath: null,
			[Path.Combine(_searchDirectory, "never-created"), _searchDirectory]);

		Assert.True(resolved.IsFailed);
	}

	// The PATH sanitising sits behind the parameterless overload, so it is reachable only through the
	// variable itself. A quoted entry must still be probed, and an entry carrying a character no path
	// can hold must be skipped rather than thrown out of Path.Combine.
	[Fact]
	public void PathEntriesAreUnquotedAndSanitisedBeforeTheyAreProbed()
	{
		var executable = WriteExecutable(
			OperatingSystem.IsWindows() ? SemibaseBinary.WindowsFileName : SemibaseBinary.UnixFileName);

		var previousPath = Environment.GetEnvironmentVariable(PathVariable);
		var previousExecutable = Environment.GetEnvironmentVariable(TestEnvironment.SemibaseExecutableVariable);

		try
		{
			Environment.SetEnvironmentVariable(TestEnvironment.SemibaseExecutableVariable, null);
			Environment.SetEnvironmentVariable(
				PathVariable,
				string.Join(Path.PathSeparator, UnusablePathEntry, string.Empty, $"\"{_searchDirectory}\""));

			var resolved = SemibaseBinary.Resolve();

			Assert.True(resolved.IsSuccess, string.Join("; ", resolved.Errors.Select(error => error.Message)));
			Assert.Equal(executable, resolved.Value);
		}
		finally
		{
			Environment.SetEnvironmentVariable(PathVariable, previousPath);
			Environment.SetEnvironmentVariable(TestEnvironment.SemibaseExecutableVariable, previousExecutable);
		}
	}

	private string WriteExecutable(string fileName)
	{
		var path = Path.Combine(_searchDirectory, fileName);

		File.WriteAllText(path, string.Empty);

		return path;
	}
}
