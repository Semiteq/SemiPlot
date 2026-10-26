using AwesomeAssertions;

using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.Tests.Unit.UI.Settings;
using SemiPlot.Tools.ArchiveSeeder;
using SemiPlot.UI.Startup;

using Xunit;

using static SemiPlot.Tests.Unit.ResultText;

namespace SemiPlot.Tests.Unit;

[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class DeliveredConfigurationTests : IDisposable
{
	private const string ShippedPasswordKey = "password: \"\"";

	private readonly TemporaryDirectory _directory = new("semiplot-delivered");

	public void Dispose()
	{
		_directory.Dispose();
	}

	[Fact]
	public void TheShippedAppSectionLoads()
	{
		var result = AppSettingsLoader.Load(
			ShippedConfiguration.SectionDirectory(StartupSequence.SettingsDirectoryName));

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Should().Be(new AppSettings(UiLanguage.Ru, AppThemeVariant.Light));
	}

	[Fact]
	public void TheShippedConnectionSectionCarriesNoCredential()
	{
		var result = PostgresConnectionLoader.Load(
			ShippedConfiguration.SectionDirectory(StartupProbe.ConnectionDirectoryName));

		result.IsFailed.Should().BeTrue();

		var error = result.Errors.OfType<ConnectionFileError>().Single();

		error.Kind.Should().Be(ConnectionFileProblem.MissingField);
		error.Fields.Should().Equal("password");
	}

	[Fact]
	public void TheShippedConnectionSectionIsValidOnceItCarriesACredential()
	{
		var body = ShippedConfiguration.ConnectionBody();

		body.Should().Contain(ShippedPasswordKey);

		File.WriteAllText(
			Path.Combine(_directory, "connection.yaml"),
			body.Replace(ShippedPasswordKey, "password: \"s3cret\"", StringComparison.Ordinal));

		var result = PostgresConnectionLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Host.Should().Be("127.0.0.1");
		result.Value.Port.Should().Be(5432);
		result.Value.Database.Should().Be("semiplot");
		result.Value.Username.Should().Be("semiplot");
		result.Value.Schema.Should().Be("public");
		result.Value.PollInterval.Should().Be(TimeSpan.FromMilliseconds(250));
	}

	// converge overwrites this one file inside the demo's copy of the set. A writer aiming anywhere else
	// would add a second file carrying the same keys, which is the conflict the section rule refuses.
	[Fact]
	public void TheSeederWritesOverTheShippedConnectionFile()
	{
		ConnectionFileWriter.DirectoryName.Should().Be(StartupProbe.ConnectionDirectoryName);

		var target = ConnectionFileWriter.PathFor(ShippedConfiguration.Root);

		File.Exists(target).Should().BeTrue(target);
	}
}
