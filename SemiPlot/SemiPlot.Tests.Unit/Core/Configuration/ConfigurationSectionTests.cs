using System.Text;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Configuration;
using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.Tools.ArchiveSeeder;
using SemiPlot.UI.Startup;

using Xunit;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SemiPlot.Tests.Unit.Core.Configuration;

[Trait("Component", "Core")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class ConfigurationSectionTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("semiplot-section-").FullName;

	public void Dispose()
	{
		Directory.Delete(_directory, recursive: true);
	}

	[Fact]
	public void TwoFilesWithDisjointKeysMergeIntoOneMapping()
	{
		WriteFile("a.yaml", "locale: ru\n");
		WriteFile("b.yaml", "theme: light\n");

		var result = ConfigurationSection.Read(_directory, ConfigurationSectionName.App);

		result.IsSuccess.Should().BeTrue(Describe(result));
		MappingOf(result.Value).Should().Contain("locale", "ru").And.Contain("theme", "light");
	}

	[Fact]
	public void FilesAreOrderedOrdinallyRatherThanByCulture()
	{
		WriteFile("a.yaml", "locale: ru\n");
		WriteFile("B.yaml", "locale: en\n");

		var error = ErrorOf(ConfigurationSection.Read(_directory, ConfigurationSectionName.App));

		error.FileNames.Should().Equal("B.yaml", "a.yaml");
	}

	[Fact]
	public void TheSameKeyInTwoFilesFailsNamingTheKeyAndBothFiles()
	{
		WriteFile("one.yaml", "locale: ru\ntheme: light\n");
		WriteFile("two.yaml", "locale: en\n");

		var error = ErrorOf(ConfigurationSection.Read(_directory, ConfigurationSectionName.App));

		error.Problem.Should().Be(SectionProblem.KeyConflict);
		error.Section.Should().Be(ConfigurationSectionName.App);
		error.Key.Should().Be("locale");
		error.FileNames.Should().Equal("one.yaml", "two.yaml");
	}

	[Fact]
	public void TheSameKeyTwiceInOneFileFailsNamingTheKeyAndThatFile()
	{
		WriteFile("one.yaml", "locale: ru\nlocale: en\n");

		var error = ErrorOf(ConfigurationSection.Read(_directory, ConfigurationSectionName.App));

		error.Problem.Should().Be(SectionProblem.DuplicateKey);
		error.Key.Should().Be("locale");
		error.FileNames.Should().Equal("one.yaml");
	}

	[Fact]
	public void TheSameKeyInTwoCasesAcrossTwoFilesStillConflicts()
	{
		WriteFile("one.yaml", "locale: ru\n");
		WriteFile("two.yaml", "Locale: en\n");

		var error = ErrorOf(ConfigurationSection.Read(_directory, ConfigurationSectionName.App));

		error.Problem.Should().Be(SectionProblem.KeyConflict);
		error.FileNames.Should().Equal("one.yaml", "two.yaml");
	}

	[Fact]
	public void AQuotedValueThatReadsAsYamlSurvivesTheMergeAsText()
	{
		WriteFile("a.yaml", "password: \"null\"\nuser: \"on\"\n");

		var result = ConfigurationSection.Read(_directory, ConfigurationSectionName.Connection);

		result.IsSuccess.Should().BeTrue(Describe(result));
		MappingOf(result.Value).Should().Contain("password", "null").And.Contain("user", "on");
	}

	[Fact]
	public void AnAbsentFolderYieldsTheDirectoryMissingProblem()
	{
		var absent = Path.Combine(_directory, "app");

		var error = ErrorOf(ConfigurationSection.Read(absent, ConfigurationSectionName.App));

		error.Problem.Should().Be(SectionProblem.DirectoryMissing);
		error.Directory.Should().Be(absent);
	}

	[Fact]
	public void AFolderWithoutYamlFilesYieldsADifferentProblem()
	{
		WriteFile("app.txt", "locale: ru\n");

		var error = ErrorOf(ConfigurationSection.Read(_directory, ConfigurationSectionName.App));

		error.Problem.Should().Be(SectionProblem.NoFiles);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void ABlankDirectoryYieldsATypedErrorRatherThanAThrow(string directory)
	{
		var error = ErrorOf(ConfigurationSection.Read(directory, ConfigurationSectionName.Connection));

		error.Problem.Should().Be(SectionProblem.DirectoryMissing);
		error.Section.Should().Be(ConfigurationSectionName.Connection);
	}

	[Fact]
	public void AnEmptyFileContributesNothingAndFailsNothing()
	{
		WriteFile("a.yaml", string.Empty);
		WriteFile("b.yaml", "locale: ru\n");

		var result = ConfigurationSection.Read(_directory, ConfigurationSectionName.App);

		result.IsSuccess.Should().BeTrue(Describe(result));
		MappingOf(result.Value).Should().ContainSingle().Which.Should()
			.Be(new KeyValuePair<string, string>("locale", "ru"));
	}

	[Fact]
	public void AFileWrittenWithAByteOrderMarkStillContributesItsFirstKey()
	{
		File.WriteAllText(
			Path.Combine(_directory, "a.yaml"), "locale: ru\ntheme: light\n", new UTF8Encoding(true));

		var result = ConfigurationSection.Read(_directory, ConfigurationSectionName.App);

		result.IsSuccess.Should().BeTrue(Describe(result));
		MappingOf(result.Value).Should().Contain("locale", "ru");
	}

	[Fact]
	public void TheShippedConnectionBodySurvivesTheMerge()
	{
		WriteFile("connection.yaml", ShippedConnectionBody());

		var result = ConfigurationSection.Read(_directory, ConfigurationSectionName.Connection);

		result.IsSuccess.Should().BeTrue(Describe(result));

		var dto = new DeserializerBuilder()
			.WithNamingConvention(UnderscoredNamingConvention.Instance)
			.IgnoreUnmatchedProperties()
			.Build()
			.Deserialize<PostgresConnectionDto>(result.Value);

		dto.Host.Should().Be("localhost");
		dto.Port.Should().Be(5432);
		dto.Database.Should().Be("semiplot");
		dto.User.Should().Be("semiplot");
		dto.Password.Should().BeEmpty();
		dto.SourceTimeZone.Should().Be("Europe/Moscow");
		dto.PollIntervalMs.Should().Be(1000);
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
		File.WriteAllText(Path.Combine(_directory, name), content);
	}

	private static Dictionary<string, string> MappingOf(string yaml)
	{
		return new DeserializerBuilder().Build().Deserialize<Dictionary<string, string>?>(yaml) ?? [];
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
