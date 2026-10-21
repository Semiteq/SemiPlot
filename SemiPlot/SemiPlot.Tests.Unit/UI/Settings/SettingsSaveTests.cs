using System.Security.AccessControl;
using System.Security.Principal;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Configuration;
using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.OpcUa.Configuration;
using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Settings;
using SemiPlot.UI.Startup;

using Xunit;

using YamlDotNet.Core;

using static SemiPlot.Tests.Unit.ResultText;
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

	[Theory]
	[InlineData(ConfigurationSectionName.App, "locale", "de", AppSettingsProblem.ValueInvalid)]
	[InlineData(ConfigurationSectionName.Connection, "port", "70000", ConnectionFileProblem.OutOfRange)]
	[InlineData(ConfigurationSectionName.Connection, "host", "scada-01", ConnectionFileProblem.HostNotIPv4)]
	[InlineData(ConfigurationSectionName.Connection, "password", "", ConnectionFileProblem.MissingField)]
	[InlineData(ConfigurationSectionName.Opc, "port", "70000", ConnectionFileProblem.OutOfRange)]
	[InlineData(ConfigurationSectionName.Opc, "host", "scada-01", ConnectionFileProblem.HostNotIPv4)]
	public void AnInvalidValueIsRefusedAndNoFileChanges(
		ConfigurationSectionName section,
		string key,
		string value,
		object expectedProblem)
	{
		var before = _sandbox.Snapshot();

		var result = _sandbox.Save(Edit(section, (key, value)));

		var error = result.Errors.Should().ContainSingle().Subject;
		var problem = error switch
		{
			AppSettingsError settings => (object)settings.Kind,
			ConnectionFileError file => file.Kind,
			_ => throw new InvalidOperationException(error.Message)
		};
		problem.Should().Be(expectedProblem);
		(error as AppSettingsError)?.Key.Should().Be(key);
		(error as ConnectionFileError)?.Fields.Should().Equal(key);
		(error as ConnectionFileError)?.Section.Should().Be(section);
		_sandbox.ShouldMatch(before);
	}

	[Fact]
	public void APasswordWithSpacesAndAHash_ReloadsThroughTheLoaderVerbatim()
	{
		var result = _sandbox.Save(Edit(ConfigurationSectionName.Connection, ("password", "two words # and a hash")));

		result.IsSuccess.Should().BeTrue(Describe(result));
		_sandbox.LoadConnection().Password.Should().Be("two words # and a hash");
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

		using (DirectoryAccessDenial.OfNewFolders(_sandbox.ConfigDirectory))
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
	public void ASaveReplacesATargetAnotherSavesWriteProbeHoldsOpen()
	{
		var target = _sandbox.ConnectionFile;
		Result result;

		using (new FileStream(target, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
		{
			result = _sandbox.Save(Edit(ConfigurationSectionName.Connection, ("host", "10.20.30.40")));
		}

		result.IsSuccess.Should().BeTrue(Describe(result));
		_sandbox.LoadConnection().Host.Should().Be("10.20.30.40");
	}

	[Fact]
	public void ARefusalKeepsTheLoadersCause()
	{
		var target = _sandbox.ConnectionFile;
		File.WriteAllText(target, File.ReadAllText(target).Replace("port: 5432", "port: \"scada-01\""));

		var result = _sandbox.Save(Edit(ConfigurationSectionName.Connection, ("host", "10.20.30.40")));

		var error = SingleError<ConnectionFileError>(result);
		error.Kind.Should().Be(ConnectionFileProblem.Unparseable);
		error.Section.Should().Be(ConfigurationSectionName.Connection);
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
	[InlineData(ConfigurationSectionName.Opc, "port", "70000")]
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
		(error as ConnectionFileError)?.Fields.Should().Equal(key);
		(error as ConnectionFileError)?.Section.Should().Be(section);
		var detail = ArchiveFailureMapper.Map(error).Detail;
		detail.Should().Contain(realDirectory).And.NotContain(SettingsSave.StagingPrefix);
	}

	[Fact]
	public void AnOpcEditReachesTheOpcFileAndLeavesTheOtherSectionsByteIdentical()
	{
		var before = _sandbox.Snapshot();

		var result = _sandbox.Save(Edit(ConfigurationSectionName.Opc, ("port", "55875")));

		result.IsSuccess.Should().BeTrue(Describe(result));
		var opc = _sandbox.LoadOpc();
		opc.Host.Should().Be("127.0.0.1");
		opc.Port.Should().Be(55875);
		var after = _sandbox.Snapshot();
		after.Keys.Should().BeEquivalentTo(before.Keys, "the save creates no file");
		var otherSections = before.Keys
			.Where(file => Path.GetDirectoryName(file)
				is StartupSequence.SettingsDirectoryName or StartupProbe.ConnectionDirectoryName)
			.ToList();
		otherSections.Should().HaveCount(2, "the shipped set carries one app file and one connection file");

		foreach (var file in otherSections)
		{
			after[file].Should().Equal(before[file], file);
		}

		_sandbox.StagingDirectories().Should().BeEmpty();
	}

	[Fact]
	public void TheCertificateStoreUnderTheOpcSectionSurvivesASaveUntouched()
	{
		var certificates = Path.Combine(
			_sandbox.OpcDirectory, OpcUaConnectionLoader.CertificateStoreDirectoryName, "own", "certs");
		Directory.CreateDirectory(certificates);
		var certificate = Path.Combine(certificates, "semiplot.der");
		byte[] content = [0x30, 0x82, 0x01, 0x0a];
		File.WriteAllBytes(certificate, content);

		var result = _sandbox.Save(Edit(ConfigurationSectionName.Opc, ("port", "55875")));

		result.IsSuccess.Should().BeTrue(Describe(result));
		_sandbox.LoadOpc().Port.Should().Be(55875);
		File.ReadAllBytes(certificate).Should().Equal(content);
		_sandbox.StagingDirectories().Should().BeEmpty();
	}
}
