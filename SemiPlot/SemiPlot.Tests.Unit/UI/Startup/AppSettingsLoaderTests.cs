using AwesomeAssertions;

using FluentResults;

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
	public void AValidFileCarriesTheConfiguredLocale(string text, UiLanguage expected)
	{
		var path = WriteFile($"locale: {text}\ntheme: light\n");

		var result = AppSettingsLoader.Load(path);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Locale.Should().Be(expected);
	}

	[Theory]
	[InlineData("light", AppThemeVariant.Light)]
	[InlineData("dark", AppThemeVariant.Dark)]
	public void AValidFileCarriesTheConfiguredTheme(string text, AppThemeVariant expected)
	{
		var path = WriteFile($"locale: ru\ntheme: {text}\n");

		var result = AppSettingsLoader.Load(path);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Theme.Should().Be(expected);
	}

	[Fact]
	public void AKeyTheFormatDoesNotNameIsIgnored()
	{
		var path = WriteFile("locale: ru\ntheme: dark\nwindow_width: 800\n");

		var result = AppSettingsLoader.Load(path);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Should().Be(new AppSettings(UiLanguage.Ru, AppThemeVariant.Dark));
	}

	[Fact]
	public void AnAbsentFileYieldsTheNotFoundError()
	{
		var path = Path.Combine(_directory, "app.yaml");

		var result = AppSettingsLoader.Load(path);

		var error = ErrorOf(result);
		error.Kind.Should().Be(AppSettingsProblem.NotFound);
		error.Path.Should().Be(path);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void ABlankPathYieldsATypedErrorRatherThanAThrow(string path)
	{
		var result = AppSettingsLoader.Load(path);

		ErrorOf(result).Path.Should().Be(path);
	}

	// A directory stands in for every path that exists and cannot be read.
	[Fact]
	public void APathThatCannotBeOpenedYieldsTheUnreadableDiscriminator()
	{
		var result = AppSettingsLoader.Load(_directory);

		var error = ErrorOf(result);
		error.Kind.Should().Be(AppSettingsProblem.Unreadable);
		error.Path.Should().Be(_directory);
	}

	[Fact]
	public void AMalformedDocumentYieldsTheUnreadableDiscriminator()
	{
		var path = WriteFile("locale: [ru\ntheme: :\n");

		var result = AppSettingsLoader.Load(path);

		var error = ErrorOf(result);
		error.Kind.Should().Be(AppSettingsProblem.Unreadable);
		error.Path.Should().Be(path);
	}

	[Fact]
	public void AMalformedDocumentCarriesItsCausingException()
	{
		var path = WriteFile("locale: [ru\ntheme: :\n");

		var result = AppSettingsLoader.Load(path);

		ErrorOf(result).Reasons.OfType<ExceptionalError>().Should().ContainSingle();
	}

	[Theory]
	[InlineData("theme: light\n", "locale")]
	[InlineData("locale: ru\n", "theme")]
	public void AnAbsentKeyYieldsTheKeyMissingDiscriminator(string content, string key)
	{
		var path = WriteFile(content);

		var result = AppSettingsLoader.Load(path);

		var error = ErrorOf(result);
		error.Kind.Should().Be(AppSettingsProblem.KeyMissing);
		error.Key.Should().Be(key);
	}

	[Theory]
	[InlineData("locale: \"   \"\ntheme: light\n", "locale")]
	[InlineData("locale: ru\ntheme: \"   \"\n", "theme")]
	public void ABlankKeyYieldsTheKeyMissingDiscriminator(string content, string key)
	{
		var path = WriteFile(content);

		var result = AppSettingsLoader.Load(path);

		var error = ErrorOf(result);
		error.Kind.Should().Be(AppSettingsProblem.KeyMissing);
		error.Key.Should().Be(key);
	}

	[Fact]
	public void AnEmptyFileYieldsTheKeyMissingDiscriminator()
	{
		var path = WriteFile(string.Empty);

		var result = AppSettingsLoader.Load(path);

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
		var path = WriteFile(content);

		var result = AppSettingsLoader.Load(path);

		var error = ErrorOf(result);
		error.Kind.Should().Be(AppSettingsProblem.ValueInvalid);
		error.Key.Should().Be(key);
		error.AcceptedValues.Should().Contain(firstAccepted).And.Contain(secondAccepted);
	}

	[Fact]
	public void AnInvalidLocaleIsReportedBeforeAnInvalidTheme()
	{
		var path = WriteFile("locale: klingon\ntheme: sepia\n");

		var result = AppSettingsLoader.Load(path);

		result.Errors.Should().ContainSingle();
		ErrorOf(result).Key.Should().Be("locale");
	}

	[Theory]
	[InlineData("locale: RU")]
	[InlineData("locale: \"  ru  \"")]
	public void AValueIsTrimmedAndMatchedWithoutCase(string localeLine)
	{
		var path = WriteFile(localeLine + "\ntheme: LIGHT\n");

		var result = AppSettingsLoader.Load(path);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Should().Be(new AppSettings(UiLanguage.Ru, AppThemeVariant.Light));
	}

	private static AppSettingsError ErrorOf(Result<AppSettings> result)
	{
		result.IsFailed.Should().BeTrue();

		return result.Errors.OfType<AppSettingsError>().Should().ContainSingle().Which;
	}

	private static string Describe(Result<AppSettings> result)
	{
		return string.Join("; ", result.Errors.Select(error => error.Message));
	}

	private string WriteFile(string content)
	{
		var path = Path.Combine(_directory, $"app-{Guid.NewGuid():N}.yaml");

		File.WriteAllText(path, content);

		return path;
	}
}
