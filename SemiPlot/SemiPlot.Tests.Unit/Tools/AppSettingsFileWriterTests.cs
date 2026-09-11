using AwesomeAssertions;

using SemiPlot.Tools.ArchiveSeeder;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit.Tools;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class AppSettingsFileWriterTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("semiplot-app-settings-file-").FullName;

	public void Dispose()
	{
		Directory.Delete(_directory, recursive: true);
	}

	[Fact]
	public async Task TheWrittenFileRoundTripsThroughTheLoader()
	{
		await AppSettingsFileWriter.WriteAsync(_directory, TestContext.Current.CancellationToken);

		var result = AppSettingsLoader.Load(StartupSequence.SettingsPath(_directory));

		result.IsSuccess.Should().BeTrue();
		result.Value.Locale.Should().Be(UiLanguage.Ru);
		result.Value.Theme.Should().Be(AppThemeVariant.Light);
	}

	[Fact]
	public async Task TheUiSubdirectoryIsCreatedWhenItDoesNotExist()
	{
		var nested = Path.Combine(_directory, "nested");

		await AppSettingsFileWriter.WriteAsync(nested, TestContext.Current.CancellationToken);

		File.Exists(StartupSequence.SettingsPath(nested)).Should().BeTrue();
	}

	[Fact]
	public void TheWriterTargetsThePathTheApplicationReads()
	{
		Path.Combine(_directory, AppSettingsFileWriter.DirectoryName, AppSettingsFileWriter.FileName)
			.Should().Be(StartupSequence.SettingsPath(_directory));
	}

	[Fact]
	public async Task ASecondRunOverwritesTheFileItFinds()
	{
		var path = StartupSequence.SettingsPath(_directory);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		await File.WriteAllTextAsync(path, "locale: en\ntheme: dark\n", TestContext.Current.CancellationToken);

		await AppSettingsFileWriter.WriteAsync(_directory, TestContext.Current.CancellationToken);

		var result = AppSettingsLoader.Load(path);
		result.Value.Locale.Should().Be(UiLanguage.Ru);
		result.Value.Theme.Should().Be(AppThemeVariant.Light);
	}
}
