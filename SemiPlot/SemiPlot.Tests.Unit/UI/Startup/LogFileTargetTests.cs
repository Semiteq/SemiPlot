using AwesomeAssertions;

using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Startup;

[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class LogFileTargetTests : IDisposable
{
	private readonly TemporaryDirectory _directory = new("semiplot-log-target");

	public void Dispose()
	{
		_directory.Dispose();
	}

	[Fact]
	public void AMissingLogFolderIsCreatedAndTheFileIsOpened()
	{
		var path = Path.Combine(_directory, "Logs", "semiplot.log");

		var result = LogFileTarget.Prepare(path);

		result.IsSuccess.Should().BeTrue(ResultText.Describe(result));
		File.Exists(path).Should().BeTrue();
	}

	[Fact]
	public void AnExistingLogFileKeepsWhatItHolds()
	{
		var path = Path.Combine(_directory, "semiplot.log");
		File.WriteAllText(path, "previous run");

		LogFileTarget.Prepare(path).IsSuccess.Should().BeTrue();

		File.ReadAllText(path).Should().Be("previous run");
	}

	[Fact]
	public void APathThatNamesAFolderFailsWithTheNamedPath()
	{
		var result = LogFileTarget.Prepare(_directory);

		result.IsFailed.Should().BeTrue();

		var error = result.Errors.OfType<LogFileError>().Single();

		error.FilePath.Should().Be(_directory);
		error.Reason.Should().NotBeNullOrWhiteSpace();
	}
}
