using System.Globalization;

using AwesomeAssertions;

using SemiPlot.Core.Configuration;
using SemiPlot.UI;
using SemiPlot.UI.Startup;

using Serilog.Events;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Startup;

// Every case leaves the connection section absent, so the probe fails at PostgresConnectionLoader and
// no connection is opened. A connection failure where the settings are broken proves the order is wrong.
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class StartupSequenceTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("semiplot-startup-sequence-").FullName;

	private readonly CultureInfo? _previousCulture = CultureInfo.DefaultThreadCurrentUICulture;

	public void Dispose()
	{
		CultureInfo.DefaultThreadCurrentUICulture = _previousCulture;
		Directory.Delete(_directory, recursive: true);
	}

	[Fact]
	public void AnInvalidSettingsValue_FailsBeforeTheConnectionFileIsRead()
	{
		WriteSettings("locale: klingon\ntheme: light\n");

		var outcome = StartupSequence.Run(Options());

		outcome.Startup.Errors.Should().ContainSingle().Which.Should().BeOfType<AppSettingsError>()
			.Which.Kind.Should().Be(AppSettingsProblem.ValueInvalid);
	}

	[Fact]
	public void AMissingSettingsSection_FailsBeforeTheConnectionSectionIsRead()
	{
		var outcome = StartupSequence.Run(Options());

		outcome.Startup.Errors.Should().ContainSingle().Which.Should().BeOfType<ConfigurationSectionError>()
			.Which.Problem.Should().Be(SectionProblem.DirectoryMissing);
	}

	[Fact]
	public void ASettingsFailure_CarriesNoSettings()
	{
		WriteSettings("theme: light\n");

		var outcome = StartupSequence.Run(Options());

		outcome.Settings.Should().BeNull();
	}

	[Fact]
	public void ReadableSettings_ReachTheConnectionSectionAndSurviveItsFailure()
	{
		WriteSettings("locale: en\ntheme: dark\n");

		var outcome = StartupSequence.Run(Options());

		outcome.Startup.Errors.Should().ContainSingle().Which.Should().BeOfType<ConfigurationSectionError>()
			.Which.Section.Should().Be(ConfigurationSectionName.Connection);
		outcome.Settings.Should().Be(new AppSettings(UiLanguage.En, AppThemeVariant.Dark));
	}

	// Parked on the other language first, so a CultureFor arm that ignores its input cannot pass by
	// leaving the bootstrap culture in place.
	[Theory]
	[InlineData("en", "ru", "en")]
	[InlineData("ru", "en", "ru")]
	public void TheConfiguredLocale_BecomesTheDefaultUiCulture(string locale, string parked, string expected)
	{
		CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(parked);
		WriteSettings($"locale: {locale}\ntheme: light\n");

		StartupSequence.Run(Options());

		CultureInfo.DefaultThreadCurrentUICulture?.Name.Should().Be(expected);
	}

	[Fact]
	public void ASettingsFailure_LeavesTheBootstrapCultureInForce()
	{
		WriteSettings("locale: klingon\ntheme: light\n");

		StartupSequence.Run(Options());

		CultureInfo.DefaultThreadCurrentUICulture?.Name.Should().Be("ru");
	}

	private StartupOptions Options()
	{
		return new StartupOptions(
			_directory,
			Path.Combine(_directory, "semiplot.log"),
			LogEventLevel.Fatal);
	}

	private void WriteSettings(string content)
	{
		var directory = Path.Combine(_directory, StartupSequence.SettingsDirectoryName);

		Directory.CreateDirectory(directory);

		File.WriteAllText(Path.Combine(directory, "app.yaml"), content);
	}
}
