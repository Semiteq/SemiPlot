using System.Security.AccessControl;
using System.Security.Principal;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Configuration;
using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Settings;
using SemiPlot.UI.Startup;

using Xunit;

using YamlDotNet.Core;

using static SemiPlot.Tests.Unit.UI.Settings.SettingsSaveSandbox;

namespace SemiPlot.Tests.Unit.UI.Settings;

[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class SettingsSaveTests : IDisposable
{
	private readonly SettingsSaveSandbox _sandbox = new();

	public void Dispose()
	{
		_sandbox.Dispose();
	}

	[Fact]
	public void ALocaleOutsideTheVocabularyIsRefusedAndNoFileChanges()
	{
		var before = _sandbox.Snapshot();

		var result = _sandbox.Save(Edit(ConfigurationSectionName.App, ("locale", "de")));

		var error = SingleError<AppSettingsError>(result);
		error.Kind.Should().Be(AppSettingsProblem.ValueInvalid);
		error.Key.Should().Be("locale");
		_sandbox.ShouldMatch(before);
	}

	[Fact]
	public void AnOutOfRangePortIsRefusedAndNoFileChanges()
	{
		var before = _sandbox.Snapshot();

		var result = _sandbox.Save(Edit(ConfigurationSectionName.Connection, ("port", "70000")));

		SingleError<ConnectionFileError>(result).Kind.Should().Be(ConnectionFileProblem.OutOfRange);
		_sandbox.ShouldMatch(before);
	}

	[Fact]
	public void AHostNameIsRefusedAndNoFileChanges()
	{
		var before = _sandbox.Snapshot();

		var result = _sandbox.Save(Edit(ConfigurationSectionName.Connection, ("host", "scada-01")));

		SingleError<ConnectionFileError>(result).Kind.Should().Be(ConnectionFileProblem.HostNotIPv4);
		_sandbox.ShouldMatch(before);
	}

	[Fact]
	public void ABlankPasswordIsRefusedAndNoFileChanges()
	{
		var before = _sandbox.Snapshot();

		var result = _sandbox.Save(Edit(ConfigurationSectionName.Connection, ("password", "")));

		SingleError<ConnectionFileError>(result).Kind.Should().Be(ConnectionFileProblem.MissingField);
		_sandbox.ShouldMatch(before);
	}

	[Fact]
	public void AValidSaveReloadsThroughBothLoadersToWhatWasTyped()
	{
		var edits = new Dictionary<ConfigurationSectionName, IReadOnlyDictionary<string, string>>
		{
			[ConfigurationSectionName.App] = new Dictionary<string, string>
			{
				["locale"] = "en",
				["theme"] = "dark"
			},
			[ConfigurationSectionName.Connection] = new Dictionary<string, string>
			{
				["host"] = "10.20.30.40",
				["port"] = "5433",
				["database"] = "archive",
				["user"] = "viewer",
				["password"] = "two words # and a hash",
				["poll_interval_ms"] = "250"
			}
		};

		var result = _sandbox.Save(edits);

		result.IsSuccess.Should().BeTrue(Describe(result));
		AppSettingsLoader.Load(_sandbox.AppDirectory).Value.Should()
			.Be(new AppSettings(UiLanguage.En, AppThemeVariant.Dark));
		var connection = _sandbox.LoadConnection();
		connection.Host.Should().Be("10.20.30.40");
		connection.Port.Should().Be(5433);
		connection.Database.Should().Be("archive");
		connection.Username.Should().Be("viewer");
		connection.Password.Should().Be("two words # and a hash");
		connection.PollInterval.Should().Be(TimeSpan.FromMilliseconds(250));
		_sandbox.StagingDirectories().Should().BeEmpty();
	}

	[Fact]
	public void AReadOnlyTargetFailsTheSaveBeforeAnyFileMoves()
	{
		var before = _sandbox.Snapshot();
		File.SetAttributes(_sandbox.ConnectionFile, FileAttributes.ReadOnly);
		var edits = new Dictionary<ConfigurationSectionName, IReadOnlyDictionary<string, string>>
		{
			[ConfigurationSectionName.App] = new Dictionary<string, string> { ["theme"] = "dark" },
			[ConfigurationSectionName.Connection] = new Dictionary<string, string> { ["host"] = "10.20.30.40" }
		};

		var result = _sandbox.Save(edits);

		var error = SingleError<ConfigurationSectionError>(result);
		error.Problem.Should().Be(SectionProblem.Unwritable);
		error.Section.Should().Be(ConfigurationSectionName.Connection);
		error.Directory.Should().Be(_sandbox.ConnectionDirectory);
		error.FileNames.Should().Equal(ConnectionFileName);
		_sandbox.ShouldMatch(before);
	}

	[Fact]
	public void AConfigDirectoryThatRefusesTheStagingFolderFailsTheSave()
	{
		var before = _sandbox.Snapshot();
		Result result;

		using (new DirectoryWriteDenial(_sandbox.ConfigDirectory))
		{
			result = _sandbox.Save(Edit(ConfigurationSectionName.App, ("theme", "dark")));
		}

		var error = SingleError<ConfigurationSectionError>(result);
		error.Problem.Should().Be(SectionProblem.Unwritable);
		error.Directory.Should().Be(_sandbox.ConfigDirectory);
		error.FileNames.Should().ContainSingle().Which.Should().StartWith(SettingsSave.StagingPrefix);
		_sandbox.ShouldMatch(before);
	}

	[Fact]
	public void ASaveKeepsTheTargetsUnixMode()
	{
		if (OperatingSystem.IsWindows())
		{
			return;
		}

		var target = _sandbox.ConnectionFile;
		File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);

		var result = _sandbox.Save(Edit(ConfigurationSectionName.Connection, ("host", "10.20.30.40")));

		result.IsSuccess.Should().BeTrue(Describe(result));
		_sandbox.LoadConnection().Host.Should().Be("10.20.30.40");
		File.GetUnixFileMode(target).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
	}

	[Fact]
	public void ASaveKeepsTheTargetsExplicitAccessRule()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		var target = new FileInfo(_sandbox.ConnectionFile);
		var guests = new SecurityIdentifier(WellKnownSidType.BuiltinGuestsSid, null);
		var security = target.GetAccessControl();
		security.AddAccessRule(new FileSystemAccessRule(guests, FileSystemRights.Read, AccessControlType.Allow));
		target.SetAccessControl(security);

		var result = _sandbox.Save(Edit(ConfigurationSectionName.Connection, ("host", "10.20.30.40")));

		result.IsSuccess.Should().BeTrue(Describe(result));
		_sandbox.LoadConnection().Host.Should().Be("10.20.30.40");
		var explicitIdentities = new List<IdentityReference>();

		foreach (FileSystemAccessRule rule in new FileInfo(target.FullName).GetAccessControl()
			.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)))
		{
			explicitIdentities.Add(rule.IdentityReference);
		}

		explicitIdentities.Should().Contain(guests);
	}

	// A reader that shares write but not delete lets the write probe through and refuses the replace.
	[Fact]
	public void AReplaceRefusedAfterTheProbeFailsWithUnwritableNamingTheFile()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		var target = _sandbox.ConnectionFile;
		var before = _sandbox.Snapshot();
		Result result;

		using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
		{
			result = _sandbox.Save(Edit(ConfigurationSectionName.Connection, ("host", "10.20.30.40")));
		}

		var error = SingleError<ConfigurationSectionError>(result);
		error.Problem.Should().Be(SectionProblem.Unwritable);
		error.Directory.Should().Be(_sandbox.ConnectionDirectory);
		error.FileNames.Should().Equal(ConnectionFileName);
		_sandbox.ShouldMatch(before);
	}

	[Fact]
	public void ARefusalKeepsTheLoadersCause()
	{
		var target = _sandbox.ConnectionFile;
		File.WriteAllText(target, File.ReadAllText(target).Replace("port: 5432", "port: \"scada-01\""));

		var result = _sandbox.Save(Edit(ConfigurationSectionName.Connection, ("host", "10.20.30.40")));

		var error = SingleError<ConnectionFileError>(result);
		error.Kind.Should().Be(ConnectionFileProblem.Unparseable);
		error.Path.Should().Be(_sandbox.ConnectionDirectory);
		error.Reasons.OfType<ExceptionalError>().Should().ContainSingle()
			.Which.Exception.Should().BeAssignableTo<YamlException>();
	}

	[Fact]
	public void AKeyAnotherWriterChangedSurvivesASaveOfADifferentKey()
	{
		_sandbox.Save(Edit(ConfigurationSectionName.Connection, ("host", "10.0.0.9")))
			.IsSuccess.Should().BeTrue();

		var result = _sandbox.Save(Edit(ConfigurationSectionName.Connection, ("port", "5433")));

		result.IsSuccess.Should().BeTrue(Describe(result));
		var connection = _sandbox.LoadConnection();
		connection.Host.Should().Be("10.0.0.9");
		connection.Port.Should().Be(5433);
	}

	[Fact]
	public void TheSameKeySavedTwiceEndsAtTheSecondValue()
	{
		_sandbox.Save(Edit(ConfigurationSectionName.Connection, ("host", "10.0.0.1"))).IsSuccess.Should().BeTrue();

		_sandbox.Save(Edit(ConfigurationSectionName.Connection, ("host", "10.0.0.2"))).IsSuccess.Should().BeTrue();

		_sandbox.LoadConnection().Host.Should().Be("10.0.0.2");
	}

	[Fact]
	public void AnUnmodifiedShippedCopyWithOnlyThePasswordFilledPromotes()
	{
		ShippedConfiguration.CopyTo(_sandbox.ConfigDirectory);
		PostgresConnectionLoader.Load(_sandbox.ConnectionDirectory).IsFailed.Should().BeTrue();

		var result = _sandbox.Save(Edit(ConfigurationSectionName.Connection, ("password", "secret")));

		result.IsSuccess.Should().BeTrue(Describe(result));
		AppSettingsLoader.Load(_sandbox.AppDirectory).IsSuccess.Should().BeTrue();
		_sandbox.LoadConnection().Password.Should().Be("secret");
	}

	[Theory]
	[InlineData(ConfigurationSectionName.App, "locale", "de")]
	[InlineData(ConfigurationSectionName.Connection, "port", "70000")]
	public void ARefusedSaveNamesTheOperatorsFolderAndNeverTheStagingOne(
		ConfigurationSectionName section,
		string key,
		string value)
	{
		var realDirectory = _sandbox.DirectoryOf(section);

		var result = _sandbox.Save(Edit(section, (key, value)));

		var error = result.Errors.Should().ContainSingle().Subject;
		var path = error switch
		{
			AppSettingsError settings => settings.Path,
			ConnectionFileError file => file.Path,
			_ => throw new InvalidOperationException(error.Message)
		};
		path.Should().Be(realDirectory);
		var detail = ArchiveFailureMapper.Map(error).Detail;
		detail.Should().Contain(realDirectory).And.NotContain(SettingsSave.StagingPrefix);
	}

	[Fact]
	public void NoStagingFolderRemainsAfterASuccessfulOrARefusedSave()
	{
		_sandbox.Save(Edit(ConfigurationSectionName.App, ("theme", "dark"))).IsSuccess.Should().BeTrue();
		_sandbox.StagingDirectories().Should().BeEmpty();

		_sandbox.Save(Edit(ConfigurationSectionName.App, ("theme", "sepia"))).IsFailed.Should().BeTrue();
		_sandbox.StagingDirectories().Should().BeEmpty();
	}
}
