using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// A resolved path that is not runnable is the failure a wrong SEMIBASE_EXE produces, and it must reach
// the fixture as a stated reason rather than as an exception: an unavailable runtime is a skip, and a
// crash out of the collection fixture would be neither a skip nor a named failure.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class SemibaseProvisionerTests : IDisposable
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), $"semibase-run-{Guid.NewGuid():N}");

	public SemibaseProvisionerTests()
	{
		Directory.CreateDirectory(_directory);
	}

	public void Dispose()
	{
		Directory.Delete(_directory, recursive: true);
	}

	[Fact]
	public async Task AFileThatIsNotRunnableIsAStatedReason()
	{
		var executable = Path.Combine(_directory, SemibaseBinary.UnixFileName);

		await File.WriteAllTextAsync(executable, string.Empty, TestContext.Current.CancellationToken);

		var reported = await SemibaseProvisioner.RunAsync(
			executable,
			["version"],
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(reported.IsFailed);
		Assert.Contains(executable, reported.Errors[0].Message, StringComparison.Ordinal);
		Assert.Contains("did not run", reported.Errors[0].Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task AnAbsentExecutableIsAStatedReason()
	{
		var reported = await SemibaseProvisioner.RunAsync(
			Path.Combine(_directory, "never-created"),
			["version"],
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(reported.IsFailed);
		Assert.Contains("did not run", reported.Errors[0].Message, StringComparison.Ordinal);
	}
}
