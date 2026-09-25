using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Configuration;
using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.Tools.ArchiveSeeder;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Configuration;

[Trait("Component", "Core")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class ConfigurationSectionWriterTests : IDisposable
{
	private readonly string _root = Directory.CreateTempSubdirectory("semiplot-section-writer-").FullName;

	private readonly string _section;

	private readonly string _staging;

	public ConfigurationSectionWriterTests()
	{
		_section = Directory.CreateDirectory(Path.Combine(_root, "section")).FullName;
		_staging = Directory.CreateDirectory(Path.Combine(_root, "staging")).FullName;
	}

	public void Dispose()
	{
		Directory.Delete(_root, recursive: true);
	}

	[Theory]
	[InlineData("locale", "en", "a.yaml", "b.yaml")]
	[InlineData("theme", "dark", "b.yaml", "a.yaml")]
	public void AnEditedKeyRewritesItsOwnerAndCopiesEveryOtherFileByteForByte(
		string key,
		string value,
		string owner,
		string other)
	{
		WriteFile("a.yaml", "# kept as written\nlocale: ru\n");
		WriteFile("b.yaml", "# kept as written\ntheme: light\n");
		var original = File.ReadAllBytes(Path.Combine(_section, owner));

		var rewritten = Staged(ConfigurationSectionName.App, (key, value));

		rewritten.Should().Equal(owner);
		File.ReadAllBytes(Path.Combine(_staging, other)).Should()
			.Equal(File.ReadAllBytes(Path.Combine(_section, other)));
		File.ReadAllBytes(Path.Combine(_section, owner)).Should().Equal(original);
		ValuesOf(_staging, ConfigurationSectionName.App).Should().Contain(key, value).And.HaveCount(2);
		ConfigurationSection.Read(_staging, ConfigurationSectionName.App).IsSuccess.Should().BeTrue();
	}

	[Fact]
	public void AFileOwningNoEditedKeyIsNotReported()
	{
		WriteFile("a.yaml", "locale: ru\n");
		WriteFile("b.yaml", "theme: light\n");

		var rewritten = Staged(ConfigurationSectionName.App);

		rewritten.Should().BeEmpty();
		Directory.GetFiles(_staging).Select(Path.GetFileName).Should().BeEquivalentTo("a.yaml", "b.yaml");
	}

	[Fact]
	public void AKeyTheFormatDoesNotModelSurvivesTheWrite()
	{
		WriteFile("app.yaml", "locale: ru\ntheme: light\noperator_note: calibrated on site\n");

		Staged(ConfigurationSectionName.App, ("theme", "dark"));

		ValuesOf(_staging, ConfigurationSectionName.App).Should()
			.Contain("operator_note", "calibrated on site")
			.And.Contain("locale", "ru")
			.And.Contain("theme", "dark");
	}

	[Fact]
	public void AKeySpelledInAnotherCaseIsReplacedRatherThanRepeated()
	{
		WriteFile("app.yaml", "Locale: ru\ntheme: light\n");

		Staged(ConfigurationSectionName.App, ("locale", "en"));

		var staged = ConfigurationSection.ReadOwned(_staging, ConfigurationSectionName.App);

		staged.IsSuccess.Should().BeTrue(Describe(staged));
		staged.Value.Values.Should().HaveCount(2).And.Contain("locale", "en");
		File.ReadAllText(Path.Combine(_staging, "app.yaml")).Should().NotContain("Locale");
	}

	[Theory]
	[InlineData("on")]
	[InlineData("no")]
	[InlineData("~")]
	[InlineData("null")]
	[InlineData("123")]
	[InlineData("12:34")]
	[InlineData("#comment")]
	[InlineData("two words # and a hash")]
	public void APasswordThatAlsoReadsAsYamlSurvivesAsText(string password)
	{
		WriteFile(ConnectionFileWriter.FileName, ShippedConnectionBody());

		Staged(ConfigurationSectionName.Connection, ("password", password));

		var loaded = PostgresConnectionLoader.Load(_staging);

		loaded.IsSuccess.Should().BeTrue(Describe(loaded));
		loaded.Value.Password.Should().Be(password);
	}

	[Fact]
	public void TheUntouchedKeysOfARewrittenFileStillLoad()
	{
		WriteFile(ConnectionFileWriter.FileName, ShippedConnectionBody() + "schema: archive\n");

		Staged(ConfigurationSectionName.Connection, ("host", "10.20.30.40"), ("password", "secret"));

		var loaded = PostgresConnectionLoader.Load(_staging);

		loaded.IsSuccess.Should().BeTrue(Describe(loaded));
		loaded.Value.Host.Should().Be("10.20.30.40");
		loaded.Value.Port.Should().Be(5432);
		loaded.Value.Database.Should().Be("semiplot");
		loaded.Value.Username.Should().Be("semiplot");
		loaded.Value.PollInterval.Should().Be(TimeSpan.FromSeconds(1));
		loaded.Value.Schema.Should().Be("archive");
	}

	[Fact]
	public void AnEditedKeyNoFileCarriesFailsWithKeyAbsent()
	{
		WriteFile("app.yaml", "locale: ru\ntheme: light\n");
		var owned = OwnedOf(ConfigurationSectionName.App);

		var error = ErrorOf(Stage(owned, ConfigurationSectionName.App, ("operator_note", "added")));

		error.Problem.Should().Be(SectionProblem.KeyAbsent);
		error.Key.Should().Be("operator_note");
		error.Directory.Should().Be(_section);
	}

	[Fact]
	public void AKeyRemovedFromItsOwnerAfterTheReadFailsWithKeyAbsent()
	{
		WriteFile("app.yaml", "locale: ru\ntheme: light\n");
		var owned = OwnedOf(ConfigurationSectionName.App);
		WriteFile("app.yaml", "locale: ru\n");

		var error = ErrorOf(Stage(owned, ConfigurationSectionName.App, ("theme", "dark")));

		error.Problem.Should().Be(SectionProblem.KeyAbsent);
		error.Key.Should().Be("theme");
	}

	[Fact]
	public void AnOwningFileRemovedAfterTheReadFailsWithUnreadableNamingIt()
	{
		WriteFile("a.yaml", "locale: ru\n");
		WriteFile("b.yaml", "theme: light\n");
		var owned = OwnedOf(ConfigurationSectionName.App);
		File.Delete(Path.Combine(_section, "a.yaml"));

		var error = ErrorOf(Stage(owned, ConfigurationSectionName.App, ("locale", "en")));

		error.Problem.Should().Be(SectionProblem.Unreadable);
		error.FileNames.Should().Equal("a.yaml");
		error.Directory.Should().Be(_section);
	}

	[Fact]
	public void ASectionFolderRemovedAfterTheReadFailsWithUnlistable()
	{
		WriteFile("app.yaml", "locale: ru\ntheme: light\n");
		var owned = OwnedOf(ConfigurationSectionName.App);
		Directory.Delete(_section, recursive: true);

		var error = ErrorOf(Stage(owned, ConfigurationSectionName.App));

		error.Problem.Should().Be(SectionProblem.Unlistable);
		error.Directory.Should().Be(_section);
	}

	[Fact]
	public void ACopyThatCannotBeWrittenFailsWithUnwritableNamingTheFile()
	{
		WriteFile("app.yaml", "locale: ru\ntheme: light\n");
		var owned = OwnedOf(ConfigurationSectionName.App);
		Directory.Delete(_staging);

		var error = ErrorOf(Stage(owned, ConfigurationSectionName.App));

		error.Problem.Should().Be(SectionProblem.Unwritable);
		error.FileNames.Should().Equal("app.yaml");
		error.Directory.Should().Be(_section);
	}

	[Fact]
	public void ARewriteThatCannotBeWrittenFailsWithUnwritableNamingTheFile()
	{
		WriteFile("app.yaml", "locale: ru\ntheme: light\n");
		var owned = OwnedOf(ConfigurationSectionName.App);
		Directory.Delete(_staging);

		var error = ErrorOf(Stage(owned, ConfigurationSectionName.App, ("theme", "dark")));

		error.Problem.Should().Be(SectionProblem.Unwritable);
		error.FileNames.Should().Equal("app.yaml");
	}

	private IReadOnlyList<string> Staged(ConfigurationSectionName section, params (string Key, string Value)[] edits)
	{
		var result = Stage(OwnedOf(section), section, edits);

		result.IsSuccess.Should().BeTrue(Describe(result));

		return result.Value;
	}

	private Result<IReadOnlyList<string>> Stage(
		OwnedSection owned,
		ConfigurationSectionName section,
		params (string Key, string Value)[] edits)
	{
		var mapping = edits.ToDictionary(edit => edit.Key, edit => edit.Value);

		return ConfigurationSectionWriter.Stage(owned, mapping, section, _section, _staging);
	}

	private OwnedSection OwnedOf(ConfigurationSectionName section)
	{
		var owned = ConfigurationSection.ReadOwned(_section, section);

		owned.IsSuccess.Should().BeTrue(Describe(owned));

		return owned.Value;
	}

	private static IReadOnlyDictionary<string, string> ValuesOf(string directory, ConfigurationSectionName section)
	{
		var owned = ConfigurationSection.ReadOwned(directory, section);

		owned.IsSuccess.Should().BeTrue(Describe(owned));

		return owned.Value.Values;
	}

	private static string ShippedConnectionBody()
	{
		return File.ReadAllText(
			Path.Combine(
				AppContext.BaseDirectory,
				"ConfigFiles",
				StartupProbe.ConnectionDirectoryName,
				ConnectionFileWriter.FileName));
	}

	private void WriteFile(string name, string content)
	{
		File.WriteAllText(Path.Combine(_section, name), content);
	}

	private static ConfigurationSectionError ErrorOf<TValue>(Result<TValue> result)
	{
		result.IsFailed.Should().BeTrue();

		return result.Errors.OfType<ConfigurationSectionError>().Should().ContainSingle().Subject;
	}

	private static string Describe<TValue>(Result<TValue> result)
	{
		return string.Join("; ", result.Errors.Select(error => error.Message));
	}
}
