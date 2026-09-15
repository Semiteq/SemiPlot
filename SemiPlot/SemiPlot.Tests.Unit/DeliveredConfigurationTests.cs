using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.Tools.ArchiveSeeder;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit;

[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class DeliveredConfigurationTests : IDisposable
{
	private const string ShippedRoot = "ConfigFiles";

	private const string ShippedPasswordKey = "password: \"\"";

	private readonly string _directory = Directory.CreateTempSubdirectory("semiplot-delivered-").FullName;

	public void Dispose()
	{
		Directory.Delete(_directory, recursive: true);
	}

	[Fact]
	public void TheShippedAppSectionLoads()
	{
		var result = AppSettingsLoader.Load(Shipped(StartupSequence.SettingsDirectoryName));

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Should().Be(new AppSettings(UiLanguage.Ru, AppThemeVariant.Light));
	}

	[Fact]
	public void TheShippedConnectionSectionCarriesNoCredential()
	{
		var result = PostgresConnectionLoader.Load(Shipped(StartupProbe.ConnectionDirectoryName));

		result.IsFailed.Should().BeTrue();

		var error = result.Errors.OfType<ConnectionFileError>().Single();

		error.Kind.Should().Be(ConnectionFileProblem.MissingField);
		error.Reason.Should().Contain("password");
	}

	[Fact]
	public void TheShippedConnectionSectionIsValidOnceItCarriesACredential()
	{
		var body = File.ReadAllText(
			Path.Combine(Shipped(StartupProbe.ConnectionDirectoryName), ConnectionFileWriter.FileName));

		body.Should().Contain(ShippedPasswordKey);

		File.WriteAllText(
			Path.Combine(_directory, "connection.yaml"),
			body.Replace(ShippedPasswordKey, "password: \"s3cret\"", StringComparison.Ordinal));

		var result = PostgresConnectionLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Host.Should().Be("localhost");
		result.Value.Port.Should().Be(5432);
		result.Value.Database.Should().Be("semiplot");
		result.Value.Username.Should().Be("semiplot_reader");
		result.Value.Schema.Should().Be("public");
		result.Value.PollInterval.Should().Be(TimeSpan.FromMilliseconds(1000));
		result.Value.SourceTimeZone.Should().Be(TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"));
	}

	// converge overwrites this one file inside the demo's copy of the set. A writer aiming anywhere else
	// would add a second file carrying the same keys, which is the conflict the section rule refuses.
	[Fact]
	public void TheSeederWritesOverTheShippedConnectionFile()
	{
		ConnectionFileWriter.DirectoryName.Should().Be(StartupProbe.ConnectionDirectoryName);

		var target = ConnectionFileWriter.PathFor(Path.Combine(AppContext.BaseDirectory, ShippedRoot));

		File.Exists(target).Should().BeTrue(target);
	}

	private static string Shipped(string section)
	{
		return Path.Combine(AppContext.BaseDirectory, ShippedRoot, section);
	}

	private static string Describe<TValue>(Result<TValue> result)
	{
		return string.Join("; ", result.Errors.Select(error => error.Message));
	}
}
