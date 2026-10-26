using System.Globalization;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Configuration;
using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.UI;
using SemiPlot.UI.Export;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Messages;

// The mapper is a pure function over IError and touches no Avalonia type, so these are plain [Fact].
// Every expected string is read from the resource set, so the assertions hold under either locale;
// the substituted arguments are part of the expected text, because no locale may drop one.
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Messages")]
[Trait("Category", "Unit")]
public sealed class ArchiveFailureMapperTests
{
	private const string AppDirectory = @"C:\Config\app";
	private const string ConnectionDirectory = @"C:\Config\connection";
	private const string ArchiveAddress = "scada-host:5432";
	private const string ArchiveDatabase = "semiplot";

	private static readonly IReadOnlyDictionary<string, FailureCase> _startupArgumentCases =
		new Dictionary<string, FailureCase>
		{
			["Missing"] = new(
				new StartupArgumentsError(StartupArgumentsProblem.Missing, "--config-dir"),
				() => Resources.FailureStartupArgumentsTitle,
				() => Resources.FormatFailureStartupArgumentsMissingDetail("--config-dir"),
				() => Resources.FailureStartupArgumentsMissingRemedy),
			["ValueMissing"] = new(
				new StartupArgumentsError(StartupArgumentsProblem.ValueMissing, "--config-dir"),
				() => Resources.FailureStartupArgumentsTitle,
				() => Resources.FormatFailureStartupArgumentsValueMissingDetail("--config-dir"),
				() => Resources.FailureStartupArgumentsValueMissingRemedy),
			["Unknown"] = new(
				new StartupArgumentsError(StartupArgumentsProblem.Unknown, "--nonsense"),
				() => Resources.FailureStartupArgumentsTitle,
				() => Resources.FormatFailureStartupArgumentsUnknownDetail("--nonsense"),
				() => Resources.FailureStartupArgumentsUnknownRemedy),
			["ValueInvalid"] = new(
				new StartupArgumentsError(
					StartupArgumentsProblem.ValueInvalid,
					StartupOptions.LoggingLevelKey,
					StartupOptions.LoggingLevelValues),
				() => Resources.FailureStartupArgumentsTitle,
				() => Resources.FormatFailureStartupArgumentsValueInvalidDetail(
					StartupOptions.LoggingLevelKey, StartupOptions.LoggingLevelValues),
				() => Resources.FailureStartupArgumentsValueInvalidRemedy)
		};

	private static readonly IReadOnlyDictionary<string, FailureCase> _appSettingsCases =
		new Dictionary<string, FailureCase>
		{
			["Unreadable"] = new(
				new AppSettingsError(AppDirectory, AppSettingsProblem.Unreadable),
				() => Resources.FailureAppSettingsRejectedTitle,
				() => Resources.FormatFailureAppSettingsUnreadableDetail(AppDirectory),
				() => Resources.FailureAppSettingsUnreadableRemedy),
			["KeyMissing"] = new(
				new AppSettingsError(AppDirectory, AppSettingsProblem.KeyMissing, AppSettingsLoader.ThemeKey),
				() => Resources.FailureAppSettingsRejectedTitle,
				() => Resources.FormatFailureAppSettingsKeyMissingDetail(AppDirectory, AppSettingsLoader.ThemeKey),
				() => Resources.FailureAppSettingsKeyMissingRemedy),
			["ValueInvalid"] = new(
				new AppSettingsError(
					AppDirectory, AppSettingsProblem.ValueInvalid, AppSettingsLoader.LocaleKey, "ru, en"),
				() => Resources.FailureAppSettingsRejectedTitle,
				() => Resources.FormatFailureAppSettingsValueInvalidDetail(
					AppDirectory, AppSettingsLoader.LocaleKey, "ru, en"),
				() => Resources.FailureAppSettingsValueInvalidRemedy)
		};

	private static readonly IReadOnlyDictionary<string, FailureCase> _sectionCases =
		new Dictionary<string, FailureCase>
		{
			["AppDirectoryMissing"] = new(
				new ConfigurationSectionError(
					ConfigurationSectionName.App, AppDirectory, SectionProblem.DirectoryMissing),
				() => Resources.FailureConfigurationSectionAppTitle,
				() => Resources.FormatFailureConfigurationSectionDirectoryMissingDetail(AppDirectory),
				() => Resources.FailureConfigurationSectionAppDirectoryMissingRemedy),
			["ConnectionDirectoryMissing"] = new(
				new ConfigurationSectionError(
					ConfigurationSectionName.Connection, ConnectionDirectory, SectionProblem.DirectoryMissing),
				() => Resources.FailureConfigurationSectionConnectionTitle,
				() => Resources.FormatFailureConfigurationSectionDirectoryMissingDetail(ConnectionDirectory),
				() => Resources.FailureConfigurationSectionConnectionDirectoryMissingRemedy),
			["AppNoFiles"] = new(
				new ConfigurationSectionError(ConfigurationSectionName.App, AppDirectory, SectionProblem.NoFiles),
				() => Resources.FailureConfigurationSectionAppTitle,
				() => Resources.FormatFailureConfigurationSectionNoFilesDetail(AppDirectory),
				() => Resources.FailureConfigurationSectionAppNoFilesRemedy),
			["ConnectionNoFiles"] = new(
				new ConfigurationSectionError(
					ConfigurationSectionName.Connection, ConnectionDirectory, SectionProblem.NoFiles),
				() => Resources.FailureConfigurationSectionConnectionTitle,
				() => Resources.FormatFailureConfigurationSectionNoFilesDetail(ConnectionDirectory),
				() => Resources.FailureConfigurationSectionConnectionNoFilesRemedy),
			["DuplicateKey"] = new(
				new ConfigurationSectionError(
					ConfigurationSectionName.App, AppDirectory, SectionProblem.DuplicateKey, "locale", ["app.yaml"]),
				() => Resources.FailureConfigurationSectionAppTitle,
				() => Resources.FormatFailureConfigurationSectionDuplicateKeyDetail("locale", "app.yaml", AppDirectory),
				() => Resources.FailureConfigurationSectionDuplicateKeyRemedy),
			["KeyConflict"] = new(
				new ConfigurationSectionError(
					ConfigurationSectionName.App,
					AppDirectory,
					SectionProblem.KeyConflict,
					"locale",
					["app.yaml", "site.yaml"]),
				() => Resources.FailureConfigurationSectionAppTitle,
				() => Resources.FormatFailureConfigurationSectionKeyConflictDetail(
					"locale", "app.yaml", "site.yaml", AppDirectory),
				() => Resources.FailureConfigurationSectionKeyConflictRemedy),
			["Unreadable"] = new(
				new ConfigurationSectionError(
					ConfigurationSectionName.Connection,
					ConnectionDirectory,
					SectionProblem.Unreadable,
					fileNames: ["connection.yaml"]),
				() => Resources.FailureConfigurationSectionConnectionTitle,
				() => Resources.FormatFailureConfigurationSectionUnreadableDetail(
					ConnectionDirectory, "connection.yaml"),
				() => Resources.FailureConfigurationSectionUnreadableRemedy),
			["Unlistable"] = new(
				new ConfigurationSectionError(
					ConfigurationSectionName.Connection, ConnectionDirectory, SectionProblem.Unlistable),
				() => Resources.FailureConfigurationSectionConnectionTitle,
				() => Resources.FormatFailureConfigurationSectionUnlistableDetail(ConnectionDirectory),
				() => Resources.FailureConfigurationSectionUnreadableRemedy),
			["Unwritable"] = new(
				new ConfigurationSectionError(
					ConfigurationSectionName.Connection,
					ConnectionDirectory,
					SectionProblem.Unwritable,
					fileNames: ["connection.yaml"]),
				() => Resources.FailureConfigurationSectionConnectionTitle,
				() => Resources.FormatFailureConfigurationSectionUnwritableDetail(
					ConnectionDirectory, "connection.yaml"),
				() => Resources.FailureConfigurationSectionUnwritableRemedy),
			["KeyAbsent"] = new(
				new ConfigurationSectionError(
					ConfigurationSectionName.App, AppDirectory, SectionProblem.KeyAbsent, "theme"),
				() => Resources.FailureConfigurationSectionAppTitle,
				() => Resources.FormatFailureConfigurationSectionKeyAbsentDetail("theme", AppDirectory),
				() => Resources.FailureConfigurationSectionKeyAbsentRemedy)
		};

	private static readonly IReadOnlyDictionary<string, FailureCase> _archiveCases =
		new Dictionary<string, FailureCase>
		{
			["Unreachable"] = new(
				Archive(ArchiveFault.Unreachable),
				() => Resources.FailureArchiveUnreachableTitle,
				() => Resources.FormatFailureArchiveUnreachableDetail(ArchiveName),
				() => Resources.FailureArchiveUnreachableRemedy,
				MessageSeverity.Warning),
			["AccessDenied"] = new(
				Archive(ArchiveFault.AccessDenied, "scada_reader"),
				() => Resources.FailureArchiveAccessDeniedTitle,
				() => Resources.FormatFailureArchiveAccessDeniedDetail(ArchiveAddress, "scada_reader", ArchiveDatabase),
				() => Resources.FailureArchiveAccessDeniedRemedy),
			["DatabaseMissing"] = new(
				Archive(ArchiveFault.DatabaseMissing),
				() => Resources.FailureArchiveNotProvisionedTitle,
				() => Resources.FormatFailureArchiveDatabaseMissingDetail(ArchiveAddress, ArchiveDatabase),
				() => Resources.FailureArchiveDatabaseMissingRemedy),
			["TableMissing trends"] = new(
				Archive(ArchiveFault.TableMissing, "trends"),
				() => Resources.FailureArchiveNotProvisionedTitle,
				() => Resources.FormatFailureArchiveTableMissingDetail(ArchiveName, "trends"),
				() => Resources.FormatFailureArchiveTableMissingRemedy("trends")),
			["TableMissing semiplot_tags"] = new(
				Archive(ArchiveFault.TableMissing, "semiplot_tags"),
				() => Resources.FailureArchiveNotProvisionedTitle,
				() => Resources.FormatFailureArchiveTableMissingDetail(ArchiveName, "semiplot_tags"),
				() => Resources.FormatFailureArchiveTableMissingRemedy("semiplot_tags")),
			["QueryTimedOut"] = new(
				Archive(ArchiveFault.QueryTimedOut),
				() => Resources.FailureArchiveQueryTimedOutTitle,
				() => Resources.FormatFailureArchiveQueryTimedOutDetail(ArchiveName),
				() => Resources.FailureArchiveQueryTimedOutRemedy,
				MessageSeverity.Warning),
			["ReadFailed with a SQLSTATE"] = new(
				Archive(ArchiveFault.ReadFailed, "22003"),
				() => Resources.FailureArchiveReadFailedTitle,
				() => Resources.FormatFailureArchiveReadFailedDetail(ArchiveName, "22003"),
				() => Resources.FormatFailureArchiveReadFailedRemedy("22003"),
				MessageSeverity.Warning),
			["ReadFailed without a SQLSTATE"] = new(
				Archive(ArchiveFault.ReadFailed),
				() => Resources.FailureArchiveReadFailedTitle,
				() => Resources.FormatFailureArchiveReadUnnamedDetail(ArchiveName),
				() => Resources.FailureArchiveReadUnnamedRemedy,
				MessageSeverity.Warning),
			["ValueRejected"] = new(
				Archive(ArchiveFault.ValueRejected, "Chamber pressure"),
				() => Resources.FailureArchiveValueRejectedTitle,
				() => Resources.FormatFailureArchiveValueRejectedDetail(ArchiveName, "Chamber pressure"),
				() => Resources.FailureArchiveValueRejectedRemedy,
				MessageSeverity.Warning),
			["NameTaken"] = new(
				Archive(ArchiveFault.NameTaken, "Etch gases"),
				() => Resources.FailureArchiveNameTakenTitle,
				() => Resources.FormatFailureArchiveNameTakenDetail(ArchiveName, "Etch gases"),
				() => Resources.FailureArchiveNameTakenRemedy,
				MessageSeverity.Warning),
			["RowGone"] = new(
				Archive(ArchiveFault.RowGone, "Etch gases"),
				() => Resources.FailureArchiveRowGoneTitle,
				() => Resources.FormatFailureArchiveRowGoneDetail(ArchiveName, "Etch gases"),
				() => Resources.FailureArchiveRowGoneRemedy,
				MessageSeverity.Warning),
			["MarkerGone"] = new(
				Archive(ArchiveFault.MarkerGone, "Etch start"),
				() => Resources.FailureArchiveMarkerGoneTitle,
				() => Resources.FormatFailureArchiveMarkerGoneDetail(ArchiveName, "Etch start"),
				() => Resources.FailureArchiveMarkerGoneRemedy,
				MessageSeverity.Warning),
			["ConnectionLost"] = new(
				new ArchiveError(ArchiveFault.ConnectionLost, "bench.example", 5432, "semiplot_dev", "3"),
				() => Resources.FailureArchiveConnectionLostTitle,
				() => Resources.FormatFailureArchiveConnectionLostDetail(
					Resources.FormatFailureArchiveNameFormat("semiplot_dev", "bench.example:5432"), "3"),
				() => Resources.FailureArchiveConnectionLostRemedy,
				MessageSeverity.Warning),
			["ShapeUnexpected"] = new(
				Archive(ArchiveFault.ShapeUnexpected, "column \"v\" does not exist"),
				() => Resources.FailureArchiveShapeUnexpectedTitle,
				() => Resources.FormatFailureArchiveShapeUnexpectedDetail(ArchiveName, "column \"v\" does not exist"),
				() => Resources.FailureArchiveShapeUnexpectedRemedy)
		};

	private static readonly IReadOnlyDictionary<string, FailureCase> _connectionFileCases =
		new Dictionary<string, FailureCase>
		{
			["Unparseable"] = new(
				new ConnectionFileError(ConnectionDirectory, ConnectionFileProblem.Unparseable, []),
				() => Resources.FailureConnectionFileRejectedTitle,
				() => Resources.FormatFailureConnectionFileUnparseableDetail(ConnectionDirectory),
				() => Resources.FailureConnectionFileUnparseableRemedy),
			["MissingField"] = new(
				new ConnectionFileError(
					ConnectionDirectory, ConnectionFileProblem.MissingField, [PostgresConnectionLoader.PasswordKey]),
				() => Resources.FailureConnectionFileRejectedTitle,
				() => Resources.FormatFailureConnectionFileMissingFieldDetail(
					ConnectionDirectory, PostgresConnectionLoader.PasswordKey),
				() => Resources.FailureConnectionFileMissingFieldRemedy),
			["OutOfRange"] = new(
				new ConnectionFileError(
					ConnectionDirectory,
					ConnectionFileProblem.OutOfRange,
					[PostgresConnectionLoader.PortKey, PostgresConnectionLoader.PollIntervalKey]),
				() => Resources.FailureConnectionFileRejectedTitle,
				() => Resources.FormatFailureConnectionFileOutOfRangeDetail(
					ConnectionDirectory,
					$"{PostgresConnectionLoader.PortKey}, {PostgresConnectionLoader.PollIntervalKey}"),
				() => Resources.FormatFailureConnectionFileOutOfRangeRemedy(
					PostgresConnectionLoader.LowestPort,
					PostgresConnectionLoader.HighestPort,
					PostgresConnectionLoader.LowestPollIntervalMs)),
			["HostNotIPv4"] = new(
				new ConnectionFileError(
					ConnectionDirectory, ConnectionFileProblem.HostNotIPv4, [PostgresConnectionLoader.HostKey]),
				() => Resources.FailureConnectionFileRejectedTitle,
				() => Resources.FormatFailureConnectionFileHostNotIPv4Detail(
					ConnectionDirectory, PostgresConnectionLoader.HostKey),
				() => Resources.FailureConnectionFileHostNotIPv4Remedy)
		};

	public static TheoryData<string> StartupArgumentKeys => [.. _startupArgumentCases.Keys];

	public static TheoryData<string> AppSettingsKeys => [.. _appSettingsCases.Keys];

	public static TheoryData<string> SectionKeys => [.. _sectionCases.Keys];

	public static TheoryData<string> ArchiveKeys => [.. _archiveCases.Keys];

	public static TheoryData<string> ConnectionFileKeys => [.. _connectionFileCases.Keys];

	private static string ArchiveName => Resources.FormatFailureArchiveNameFormat(ArchiveDatabase, ArchiveAddress);

	[Theory]
	[MemberData(nameof(StartupArgumentKeys))]
	public void StartupArguments_ReadTheirOwnTitleDetailAndRemedy(string caseName)
	{
		AssertMapsTo(_startupArgumentCases[caseName]);
	}

	[Theory]
	[MemberData(nameof(AppSettingsKeys))]
	public void AppSettings_ReadTheirOwnTitleDetailAndRemedy(string caseName)
	{
		AssertMapsTo(_appSettingsCases[caseName]);
	}

	[Theory]
	[MemberData(nameof(SectionKeys))]
	public void ConfigurationSection_ReadsItsOwnTitleDetailAndRemedy(string caseName)
	{
		AssertMapsTo(_sectionCases[caseName]);
	}

	[Theory]
	[MemberData(nameof(ArchiveKeys))]
	public void ArchiveFault_ReadsItsOwnTitleDetailAndRemedy(string caseName)
	{
		AssertMapsTo(_archiveCases[caseName]);
	}

	[Fact]
	public void ArchiveQueryTimedOut_NamesTheSqlState()
	{
		ArchiveFailureMapper.Map(Archive(ArchiveFault.QueryTimedOut)).Detail.Should().Contain("57014");
	}

	[Fact]
	public void StartupArguments_UnderTheBootstrapCulture_ReadInTheBootstrapLanguage()
	{
		string neutral;
		using (new CultureScope(CultureInfo.InvariantCulture.Name))
		{
			neutral = ArgumentsTitle();
		}

		using var bootstrap = new CultureScope(StartupSequence.CultureFor(StartupSequence.BootstrapLocale).Name);

		ArgumentsTitle().Should().NotBe(neutral);
	}

	[Fact]
	public void ApplyBootstrapCulture_SetsTheLanguageThoseWindowsAreReadIn()
	{
		var previous = CultureInfo.DefaultThreadCurrentUICulture;
		try
		{
			StartupSequence.ApplyBootstrapCulture();

			CultureInfo.DefaultThreadCurrentUICulture.Should()
				.Be(StartupSequence.CultureFor(StartupSequence.BootstrapLocale));
		}
		finally
		{
			CultureInfo.DefaultThreadCurrentUICulture = previous;
		}
	}

	[Fact]
	public void LogFile_NamesThePathAndTheReasonUnderItsOwnRemedy()
	{
		var view = ArchiveFailureMapper.Map(new LogFileError(@"Q:\Logs\semiplot.log", "the drive is missing"));

		view.Title.Should().Be(Resources.FailureLogFileTitle);
		view.Detail.Should().Contain(@"Q:\Logs\semiplot.log").And.Contain("the drive is missing");
		view.Remedy.Should().Be(Resources.FailureLogFileRemedy);
	}

	[Fact]
	public void ExportFile_NamesThePathAndTheReasonUnderItsOwnRemedy()
	{
		var view = ArchiveFailureMapper.Map(new ExportFileError(@"Q:\Out\trend.csv", "the disk is full"));

		view.Title.Should().Be(Resources.FailureExportFileTitle);
		view.Detail.Should().Be(Resources.FormatFailureExportFileDetail(@"Q:\Out\trend.csv", "the disk is full"));
		view.Remedy.Should().Be(Resources.FailureExportFileRemedy);
		view.Severity.Should().Be(MessageSeverity.Error);
	}

	[Fact]
	public void ExportFile_ForAPartialFile_WrapsTheRuntimeReasonInTheNotRemovedText()
	{
		var error = new ExportFileError(@"Q:\Out\trend.csv.partial", "access denied", isPartialFile: true);

		var view = ArchiveFailureMapper.Map(error);

		error.Message.Should().NotContain(Resources.FormatExportPartialNotRemoved("access denied"));
		view.Detail.Should().Be(Resources.FormatFailureExportFileDetail(
			@"Q:\Out\trend.csv.partial", Resources.FormatExportPartialNotRemoved("access denied")));
	}

	[Fact]
	public void InstanceHostUnknown_IsAnErrorWithItsOwnResourceText()
	{
		var view = ArchiveFailureMapper.Map(new InstanceHostUnknownError());

		view.Title.Should().Be(Resources.FailureInstanceHostUnknownTitle);
		view.Detail.Should().Be(Resources.FailureInstanceHostUnknownDetail);
		view.Remedy.Should().Be(Resources.FailureInstanceHostUnknownRemedy);
		view.Severity.Should().Be(MessageSeverity.Error);
	}

	[Theory]
	[MemberData(nameof(ConnectionFileKeys))]
	public void ConnectionFile_ReadsItsOwnTitleDetailAndRemedy(string caseName)
	{
		AssertMapsTo(_connectionFileCases[caseName]);
	}

	[Fact]
	public void StartupReadTimedOut_SeparatesTheCallersBoundFromTheServers()
	{
		var view = ArchiveFailureMapper.Map(
			new StartupReadTimedOutError(StartupRead.PenCatalogue, TimeSpan.FromSeconds(15)));

		view.Title.Should().Be(Resources.FailureStartupReadTimedOutTitle);
		view.Detail.Should().Be(Resources.FormatFailureStartupReadTimedOutDetail(
			Resources.FailureStartupReadPenCatalogue, 15d));
		view.Remedy.Should().Be(Resources.FailureStartupReadTimedOutRemedy);
	}

	[Fact]
	public void StartupReadTimedOut_NamesTheReadInTheOperatorsLanguage()
	{
		using var culture = new CultureScope("ru");

		var view = ArchiveFailureMapper.Map(
			new StartupReadTimedOutError(StartupRead.ArchiveExtent, TimeSpan.FromSeconds(30)));

		view.Detail.Should().Be(Resources.FormatFailureStartupReadTimedOutDetail(
			Resources.FailureStartupReadArchiveExtent, 30d));
	}

	// The exception arm is what stops a throw on the startup path, a data source that cannot be built or
	// a cancelled read, from ending the process with no window at all.
	[Fact]
	public void ThrownException_NamesItsTypeInsteadOfExitingSilently()
	{
		var view = ArchiveFailureMapper.Map(
			new ExceptionalError("no data source", new InvalidOperationException("no data source")));

		view.Title.Should().Be(Resources.FailureThrownTitle);
		view.Detail.Should().Contain(nameof(InvalidOperationException)).And.Contain("no data source");
		view.Remedy.Should().Be(Resources.FailureThrownRemedy);
	}

	[Fact]
	public void UnknownError_FallsToTheGenericState()
	{
		var view = ArchiveFailureMapper.Map(new Error("something this build never named"));

		view.Title.Should().Be(Resources.FailureGenericTitle);
		view.Detail.Should().Be("something this build never named");
		view.Remedy.Should().Be(Resources.FailureUnknownRemedy);
	}

	private static void AssertMapsTo(FailureCase expected)
	{
		var view = ArchiveFailureMapper.Map(expected.Error);

		view.Title.Should().Be(expected.Title());
		view.Detail.Should().Be(expected.Detail());
		view.Remedy.Should().Be(expected.Remedy());
		view.Severity.Should().Be(expected.Severity);
	}

	private static string ArgumentsTitle()
	{
		return ArchiveFailureMapper.Map(
			new StartupArgumentsError(StartupArgumentsProblem.Missing, StartupOptions.ConfigDirKey)).Title;
	}

	private static ArchiveError Archive(ArchiveFault kind, string detail = "")
	{
		return new ArchiveError(kind, "scada-host", 5432, ArchiveDatabase, detail);
	}

	private sealed record FailureCase(
		IError Error,
		Func<string> Title,
		Func<string> Detail,
		Func<string> Remedy,
		MessageSeverity Severity = MessageSeverity.Error);
}
