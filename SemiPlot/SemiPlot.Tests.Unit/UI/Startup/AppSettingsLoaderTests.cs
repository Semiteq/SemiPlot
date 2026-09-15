using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Configuration;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Startup;

[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class AppSettingsLoaderTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("semiplot-app-settings-").FullName;

	public void Dispose()
	{
		Directory.Delete(_directory, recursive: true);
	}

	[Theory]
	[InlineData("ru", UiLanguage.Ru)]
	[InlineData("en", UiLanguage.En)]
	public void AValidSectionCarriesTheConfiguredLocale(string text, UiLanguage expected)
	{
		WriteFile($"locale: {text}\ntheme: light\n");

		var result = AppSettingsLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Locale.Should().Be(expected);
	}

	[Theory]
	[InlineData("light", AppThemeVariant.Light)]
	[InlineData("dark", AppThemeVariant.Dark)]
	public void AValidSectionCarriesTheConfiguredTheme(string text, AppThemeVariant expected)
	{
		WriteFile($"locale: ru\ntheme: {text}\n");

		var result = AppSettingsLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Theme.Should().Be(expected);
	}

	[Fact]
	public void AKeyTheFormatDoesNotNameIsIgnored()
	{
		WriteFile("locale: ru\ntheme: dark\nwindow_width: 800\n");

		var result = AppSettingsLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Should().Be(new AppSettings(UiLanguage.Ru, AppThemeVariant.Dark));
	}

	[Fact]
	public void TwoFilesOfTheSectionAreMergedIntoOneSettings()
	{
		WriteFile("locale: ru\n", "a.yaml");
		WriteFile("theme: dark\n", "b.yaml");

		var result = AppSettingsLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Should().Be(new AppSettings(UiLanguage.Ru, AppThemeVariant.Dark));
	}

	[Fact]
	public void AnAbsentSectionYieldsASectionErrorRatherThanAThrow()
	{
		var absent = Path.Combine(_directory, "app");

		var result = AppSettingsLoader.Load(absent);

		var error = SectionErrorOf(result);
		error.Problem.Should().Be(SectionProblem.DirectoryMissing);
		error.Section.Should().Be(ConfigurationSectionName.App);
	}

	[Fact]
	public void ASectionWithNoFileYieldsTheNoFilesProblem()
	{
		var result = AppSettingsLoader.Load(_directory);

		SectionErrorOf(result).Problem.Should().Be(SectionProblem.NoFiles);
	}

	[Fact]
	public void AKeyCarriedByTwoFilesYieldsTheKeyConflictProblem()
	{
		WriteFile("locale: ru\ntheme: light\n", "a.yaml");
		WriteFile("locale: en\n", "b.yaml");

		var result = AppSettingsLoader.Load(_directory);

		var error = SectionErrorOf(result);
		error.Problem.Should().Be(SectionProblem.KeyConflict);
		error.Key.Should().Be("locale");
		error.FileNames.Should().Equal("a.yaml", "b.yaml");
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void ABlankPathYieldsATypedErrorRatherThanAThrow(string path)
	{
		var result = AppSettingsLoader.Load(path);

		SectionErrorOf(result).Problem.Should().Be(SectionProblem.DirectoryMissing);
	}

	[Fact]
	public void AMalformedDocumentYieldsTheSectionUnreadableProblem()
	{
		WriteFile("locale: [ru\ntheme: :\n");

		var result = AppSettingsLoader.Load(_directory);

		SectionErrorOf(result).Problem.Should().Be(SectionProblem.Unreadable);
	}

	// The merged text is well-formed YAML; a key whose shape the DTO cannot take is what still throws here.
	[Fact]
	public void AKeyWhoseShapeTheFormatRejectsYieldsTheUnreadableDiscriminator()
	{
		WriteFile("locale:\n  nested: ru\ntheme: light\n");

		var result = AppSettingsLoader.Load(_directory);

		var error = ErrorOf(result);
		error.Kind.Should().Be(AppSettingsProblem.Unreadable);
		error.Path.Should().Be(_directory);
	}

	[Fact]
	public void AKeyWhoseShapeTheFormatRejectsCarriesItsCausingException()
	{
		WriteFile("locale:\n  nested: ru\ntheme: light\n");

		var result = AppSettingsLoader.Load(_directory);

		ErrorOf(result).Reasons.OfType<ExceptionalError>().Should().ContainSingle();
	}

	[Theory]
	[InlineData("theme: light\n", "locale")]
	[InlineData("locale: ru\n", "theme")]
	public void AnAbsentKeyYieldsTheKeyMissingDiscriminator(string content, string key)
	{
		WriteFile(content);

		var result = AppSettingsLoader.Load(_directory);

		var error = ErrorOf(result);
		error.Kind.Should().Be(AppSettingsProblem.KeyMissing);
		error.Key.Should().Be(key);
	}

	[Theory]
	[InlineData("locale: \"   \"\ntheme: light\n", "locale")]
	[InlineData("locale: ru\ntheme: \"   \"\n", "theme")]
	public void ABlankKeyYieldsTheKeyMissingDiscriminator(string content, string key)
	{
		WriteFile(content);

		var result = AppSettingsLoader.Load(_directory);

		var error = ErrorOf(result);
		error.Kind.Should().Be(AppSettingsProblem.KeyMissing);
		error.Key.Should().Be(key);
	}

	[Fact]
	public void AnEmptyFileYieldsTheKeyMissingDiscriminator()
	{
		WriteFile(string.Empty);

		var result = AppSettingsLoader.Load(_directory);

		var error = ErrorOf(result);
		error.Kind.Should().Be(AppSettingsProblem.KeyMissing);
		error.Key.Should().Be("locale");
	}

	[Theory]
	[InlineData("locale: klingon\ntheme: light\n", "locale", "ru", "en")]
	[InlineData("locale: ru\ntheme: sepia\n", "theme", "light", "dark")]
	public void AValueOutsideItsSetYieldsTheValueInvalidDiscriminator(
		string content,
		string key,
		string firstAccepted,
		string secondAccepted)
	{
		WriteFile(content);

		var result = AppSettingsLoader.Load(_directory);

		var error = ErrorOf(result);
		error.Kind.Should().Be(AppSettingsProblem.ValueInvalid);
		error.Key.Should().Be(key);
		error.AcceptedValues.Should().Contain(firstAccepted).And.Contain(secondAccepted);
	}

	[Fact]
	public void AnInvalidLocaleIsReportedBeforeAnInvalidTheme()
	{
		WriteFile("locale: klingon\ntheme: sepia\n");

		var result = AppSettingsLoader.Load(_directory);

		result.Errors.Should().ContainSingle();
		ErrorOf(result).Key.Should().Be("locale");
	}

	[Theory]
	[InlineData("locale: RU")]
	[InlineData("locale: \"  ru  \"")]
	public void AValueIsTrimmedAndMatchedWithoutCase(string localeLine)
	{
		WriteFile(localeLine + "\ntheme: LIGHT\n");

		var result = AppSettingsLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Should().Be(new AppSettings(UiLanguage.Ru, AppThemeVariant.Light));
	}

	private static AppSettingsError ErrorOf(Result<AppSettings> result)
	{
		result.IsFailed.Should().BeTrue();

		return result.Errors.OfType<AppSettingsError>().Should().ContainSingle().Which;
	}

	private static ConfigurationSectionError SectionErrorOf(Result<AppSettings> result)
	{
		result.IsFailed.Should().BeTrue();

		return result.Errors.OfType<ConfigurationSectionError>().Should().ContainSingle().Which;
	}

	private static string Describe(Result<AppSettings> result)
	{
		return string.Join("; ", result.Errors.Select(error => error.Message));
	}

	private void WriteFile(string content, string name = "app.yaml")
	{
		File.WriteAllText(Path.Combine(_directory, name), content);
	}
}
