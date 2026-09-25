using System.Globalization;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Configuration;
using SemiPlot.Core.Data.Errors;
using SemiPlot.UI;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Messages;

// The mapper is a pure function over IError and touches no Avalonia type, so these are plain [Fact].
// Every expected string is read from the resource set, so the assertions hold under either locale;
// the substituted arguments are asserted on their own, because no locale may drop one.
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Messages")]
[Trait("Category", "Unit")]
public sealed class ArchiveFailureMapperTests
{
	[Theory]
	[InlineData(
		StartupArgumentsProblem.Missing, nameof(Resources.FailureStartupArgumentsMissingRemedy))]
	[InlineData(
		StartupArgumentsProblem.ValueMissing, nameof(Resources.FailureStartupArgumentsValueMissingRemedy))]
	[InlineData(
		StartupArgumentsProblem.Unknown, nameof(Resources.FailureStartupArgumentsUnknownRemedy))]
	[InlineData(
		StartupArgumentsProblem.ValueInvalid, nameof(Resources.FailureStartupArgumentsValueInvalidRemedy))]
	public void StartupArguments_EveryProblemNamesTheKeyUnderOneTitle(
		StartupArgumentsProblem kind,
		string expectedRemedyKey)
	{
		var view = ArchiveFailureMapper.Map(
			new StartupArgumentsError(kind, "--config-dir", StartupOptions.LoggingLevelValues));

		view.Title.Should().Be(Resources.FailureStartupArgumentsTitle);
		view.Detail.Should().Contain("--config-dir");
		view.Remedy.Should().Be(Text(expectedRemedyKey));
	}

	[Fact]
	public void StartupArguments_UnderTheBootstrapCulture_ReadInTheBootstrapLanguage()
	{
		var previous = CultureInfo.CurrentUICulture;
		try
		{
			CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
			var neutral = ArgumentsTitle();

			CultureInfo.CurrentUICulture = StartupSequence.CultureFor(StartupSequence.BootstrapLocale);

			ArgumentsTitle().Should().NotBe(neutral);
		}
		finally
		{
			CultureInfo.CurrentUICulture = previous;
		}
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
	public void StartupArguments_EveryProblemCarriesItsOwnRemedy()
	{
		StartupArgumentsProblem[] problems =
		[
			StartupArgumentsProblem.Missing,
			StartupArgumentsProblem.ValueMissing,
			StartupArgumentsProblem.Unknown,
			StartupArgumentsProblem.ValueInvalid
		];

		var remedies = problems
			.Select(kind => ArchiveFailureMapper.Map(new StartupArgumentsError(kind, "--log-file")).Remedy)
			.ToList();

		remedies.Should().OnlyHaveUniqueItems();
	}

	[Fact]
	public void StartupArgumentsValueInvalid_CarriesTheAcceptedSetIntoTheText()
	{
		var view = ArchiveFailureMapper.Map(
			new StartupArgumentsError(
				StartupArgumentsProblem.ValueInvalid,
				StartupOptions.LoggingLevelKey,
				StartupOptions.LoggingLevelValues));

		view.Detail.Should()
			.Contain(StartupOptions.LoggingLevelKey)
			.And.Contain(StartupOptions.LoggingLevelValues);
	}

	[Fact]
	public void StartupArgumentsUnknown_NamesWhatWasGivenRatherThanAKnownKey()
	{
		var view = ArchiveFailureMapper.Map(
			new StartupArgumentsError(StartupArgumentsProblem.Unknown, "--nonsense"));

		view.Detail.Should().Contain("--nonsense");
		view.Remedy.Should().Be(Resources.FailureStartupArgumentsUnknownRemedy);
	}

	[Fact]
	public void LogFile_NamesThePathAndTheReasonUnderItsOwnRemedy()
	{
		var view = ArchiveFailureMapper.Map(new LogFileError(@"Q:\Logs\semiplot.log", "the drive is missing"));

		view.Title.Should().Be(Resources.FailureLogFileTitle);
		view.Detail.Should().Contain(@"Q:\Logs\semiplot.log").And.Contain("the drive is missing");
		view.Remedy.Should().Be(Resources.FailureLogFileRemedy);
	}

	[Theory]
	[InlineData(ConfigurationSectionName.App, nameof(Resources.FailureConfigurationSectionAppTitle))]
	[InlineData(
		ConfigurationSectionName.Connection,
		nameof(Resources.FailureConfigurationSectionConnectionTitle))]
	public void ConfigurationSection_TitleNamesTheSection(ConfigurationSectionName section, string expectedKey)
	{
		var view = ArchiveFailureMapper.Map(
			new ConfigurationSectionError(section, @"C:\Config\app", SectionProblem.DirectoryMissing));

		view.Title.Should().Be(Text(expectedKey));
	}

	[Fact]
	public void ConfigurationSectionDirectoryMissing_NamesTheFolderAndTheKeysItMustCarry()
	{
		var view = ArchiveFailureMapper.Map(
			new ConfigurationSectionError(
				ConfigurationSectionName.App, @"C:\Config\app", SectionProblem.DirectoryMissing));

		view.Detail.Should().Contain(@"C:\Config\app");
		view.Remedy.Should().Be(Resources.FailureConfigurationSectionAppDirectoryMissingRemedy);
	}

	[Fact]
	public void ConfigurationSectionDirectoryMissing_TheRemedyDiffersBetweenTheSections()
	{
		var app = ArchiveFailureMapper.Map(
			new ConfigurationSectionError(
				ConfigurationSectionName.App, @"C:\Config\app", SectionProblem.DirectoryMissing));
		var connection = ArchiveFailureMapper.Map(
			new ConfigurationSectionError(
				ConfigurationSectionName.Connection,
				@"C:\Config\connection",
				SectionProblem.DirectoryMissing));

		app.Remedy.Should().NotBe(connection.Remedy);
	}

	[Theory]
	[InlineData(
		ConfigurationSectionName.App,
		@"C:\Config\app",
		nameof(Resources.FailureConfigurationSectionAppNoFilesRemedy))]
	[InlineData(
		ConfigurationSectionName.Connection,
		@"C:\Config\connection",
		nameof(Resources.FailureConfigurationSectionConnectionNoFilesRemedy))]
	public void ConfigurationSectionNoFiles_SendsTheOperatorToTheFileTheFolderNeeds(
		ConfigurationSectionName section,
		string directory,
		string expectedKey)
	{
		var view = ArchiveFailureMapper.Map(
			new ConfigurationSectionError(section, directory, SectionProblem.NoFiles));

		view.Detail.Should().Contain(directory);
		view.Remedy.Should().Be(Text(expectedKey));
	}

	[Fact]
	public void ConfigurationSectionDuplicateKey_NamesTheKeyAndTheOneFile()
	{
		var view = ArchiveFailureMapper.Map(
			new ConfigurationSectionError(
				ConfigurationSectionName.App,
				@"C:\Config\app",
				SectionProblem.DuplicateKey,
				"locale",
				["app.yaml"]));

		view.Detail.Should().Contain("locale").And.Contain("app.yaml").And.Contain(@"C:\Config\app");
		view.Remedy.Should().Be(Resources.FailureConfigurationSectionDuplicateKeyRemedy);
	}

	[Fact]
	public void ConfigurationSectionKeyConflict_NamesTheKeyAndBothFiles()
	{
		var view = ArchiveFailureMapper.Map(
			new ConfigurationSectionError(
				ConfigurationSectionName.App,
				@"C:\Config\app",
				SectionProblem.KeyConflict,
				"locale",
				["app.yaml", "site.yaml"]));

		view.Detail.Should()
			.Contain("locale")
			.And.Contain("app.yaml")
			.And.Contain("site.yaml")
			.And.Contain(@"C:\Config\app");
		view.Remedy.Should().Be(Resources.FailureConfigurationSectionKeyConflictRemedy);
	}

	[Fact]
	public void ConfigurationSectionUnreadable_NamesTheFileThatFailed()
	{
		var view = ArchiveFailureMapper.Map(
			new ConfigurationSectionError(
				ConfigurationSectionName.Connection,
				@"C:\Config\connection",
				SectionProblem.Unreadable,
				fileNames: ["connection.yaml"]));

		view.Detail.Should().Contain("connection.yaml").And.Contain(@"C:\Config\connection");
		view.Remedy.Should().Be(Resources.FailureConfigurationSectionUnreadableRemedy);
	}

	[Fact]
	public void ConfigurationSectionUnlistable_TalksAboutTheFolderRatherThanAFile()
	{
		var view = ArchiveFailureMapper.Map(
			new ConfigurationSectionError(
				ConfigurationSectionName.Connection, @"C:\Config\connection", SectionProblem.Unlistable));

		view.Detail.Should().Be(
			Resources.FormatFailureConfigurationSectionUnlistableDetail(@"C:\Config\connection"));
		view.Remedy.Should().Be(Resources.FailureConfigurationSectionUnreadableRemedy);
	}

	[Fact]
	public void ConfigurationSectionUnwritable_NamesTheFileThatRefusedTheWrite()
	{
		var view = ArchiveFailureMapper.Map(
			new ConfigurationSectionError(
				ConfigurationSectionName.Connection,
				@"C:\Config\connection",
				SectionProblem.Unwritable,
				fileNames: ["connection.yaml"]));

		view.Detail.Should().Be(
			Resources.FormatFailureConfigurationSectionUnwritableDetail(@"C:\Config\connection", "connection.yaml"));
		view.Detail.Should().Contain("connection.yaml").And.Contain(@"C:\Config\connection");
		view.Remedy.Should().Be(Resources.FailureConfigurationSectionUnwritableRemedy);
	}

	[Fact]
	public void ConfigurationSectionKeyAbsent_NamesTheKeyAndTheFolder()
	{
		var view = ArchiveFailureMapper.Map(
			new ConfigurationSectionError(
				ConfigurationSectionName.App, @"C:\Config\app", SectionProblem.KeyAbsent, "theme"));

		view.Detail.Should().Be(Resources.FormatFailureConfigurationSectionKeyAbsentDetail("theme", @"C:\Config\app"));
		view.Detail.Should().Contain("theme").And.Contain(@"C:\Config\app");
		view.Remedy.Should().Be(Resources.FailureConfigurationSectionKeyAbsentRemedy);
	}

	[Fact]
	public void AppSettingsUnreadable_SendsTheOperatorToTheSectionFolder()
	{
		var view = ArchiveFailureMapper.Map(
			new AppSettingsError(@"C:\Config\app", AppSettingsProblem.Unreadable));

		view.Title.Should().Be(Resources.FailureAppSettingsRejectedTitle);
		view.Detail.Should().Contain(@"C:\Config\app");
		view.Remedy.Should().Be(Resources.FailureAppSettingsUnreadableRemedy);
	}

	[Fact]
	public void AppSettingsKeyMissing_NamesTheKey()
	{
		var view = ArchiveFailureMapper.Map(
			new AppSettingsError(@"C:\Config\app", AppSettingsProblem.KeyMissing, AppSettingsLoader.ThemeKey));

		view.Title.Should().Be(Resources.FailureAppSettingsRejectedTitle);
		view.Detail.Should().Contain(@"C:\Config\app").And.Contain(AppSettingsLoader.ThemeKey);
		view.Remedy.Should().Be(Resources.FailureAppSettingsKeyMissingRemedy);
	}

	[Fact]
	public void AppSettingsValueInvalid_NamesTheKeyAndItsAcceptedValues()
	{
		var view = ArchiveFailureMapper.Map(
			new AppSettingsError(
				@"C:\Config\app",
				AppSettingsProblem.ValueInvalid,
				AppSettingsLoader.LocaleKey,
				"ru, en"));

		view.Title.Should().Be(Resources.FailureAppSettingsRejectedTitle);
		view.Detail.Should().Contain(AppSettingsLoader.LocaleKey).And.Contain("ru, en");
		view.Remedy.Should().Be(Resources.FailureAppSettingsValueInvalidRemedy);
	}

	[Theory]
	[InlineData(ConnectionFileProblem.Unparseable, nameof(Resources.FailureConnectionFileUnparseableRemedy))]
	[InlineData(ConnectionFileProblem.MissingField, nameof(Resources.FailureConnectionFileMissingFieldRemedy))]
	[InlineData(ConnectionFileProblem.OutOfRange, nameof(Resources.FailureConnectionFileOutOfRangeRemedy))]
	[InlineData(ConnectionFileProblem.UnknownTimeZone, nameof(Resources.FailureConnectionFileUnknownTimeZoneRemedy))]
	[InlineData(ConnectionFileProblem.HostNotIPv4, nameof(Resources.FailureConnectionFileHostNotIPv4Remedy))]
	public void ConnectionFileInvalid_RemedyFollowsTheProblem(ConnectionFileProblem kind, string expectedKey)
	{
		var view = ArchiveFailureMapper.Map(
			new ConnectionFileError(@"C:\Config\connection", kind, "the reason"));

		view.Title.Should().Be(Resources.FailureConnectionFileRejectedTitle);
		view.Detail.Should().Contain(@"C:\Config\connection").And.Contain("the reason");
		view.Remedy.Should().Be(Text(expectedKey));
	}

	[Fact]
	public void ArchiveUnreachable_SendsTheOperatorToTheNetwork()
	{
		var view = ArchiveFailureMapper.Map(Archive(ArchiveFault.Unreachable));

		view.Title.Should().Be(Resources.FailureArchiveUnreachableTitle);
		view.Detail.Should().Contain("scada-host:5432").And.Contain("semiplot");
		view.Remedy.Should().Be(Resources.FailureArchiveUnreachableRemedy);
	}

	[Fact]
	public void ArchiveAccessDenied_SendsTheOperatorToTheCredentials()
	{
		var view = ArchiveFailureMapper.Map(Archive(ArchiveFault.AccessDenied, "scada_reader"));

		view.Title.Should().Be(Resources.FailureArchiveAccessDeniedTitle);
		view.Detail.Should().Contain("scada_reader").And.Contain("scada-host:5432").And.Contain("semiplot");
		view.Remedy.Should().Be(Resources.FailureArchiveAccessDeniedRemedy);
	}

	[Fact]
	public void ArchiveDatabaseMissing_SendsTheOperatorToSemibaseSite()
	{
		var view = ArchiveFailureMapper.Map(Archive(ArchiveFault.DatabaseMissing));

		view.Title.Should().Be(Resources.FailureArchiveNotProvisionedTitle);
		view.Detail.Should().Contain("scada-host:5432").And.Contain("semiplot");
		view.Remedy.Should().Be(Resources.FailureArchiveDatabaseMissingRemedy);
	}

	[Theory]
	[InlineData("trends")]
	[InlineData("semiplot_tags")]
	public void ArchiveTableMissing_NamesTheTableAndSendsTheOperatorToSemibaseSite(string table)
	{
		var view = ArchiveFailureMapper.Map(Archive(ArchiveFault.TableMissing, table));

		view.Title.Should().Be(Resources.FailureArchiveNotProvisionedTitle);
		view.Detail.Should().Contain(table);
		view.Remedy.Should().Be(Resources.FormatFailureArchiveTableMissingRemedy(table));
	}

	// Both tables arrive from the same provisioning run, so the remedy may not branch on which one is
	// absent. Substituting the table name out of each remedy leaves two strings that must be equal.
	[Fact]
	public void ArchiveTableMissing_TheRemedyDoesNotDependOnWhichTableIsAbsent()
	{
		var trendsRemedy = RemedyWithTableNameElided("trends");
		var tagTableRemedy = RemedyWithTableNameElided("semiplot_tags");

		trendsRemedy.Should().Be(tagTableRemedy);
	}

	private static string RemedyWithTableNameElided(string table)
	{
		var view = ArchiveFailureMapper.Map(Archive(ArchiveFault.TableMissing, table));

		return view.Remedy.Replace(table, "<table>", StringComparison.Ordinal);
	}

	[Fact]
	public void ArchiveQueryTimedOut_NamesTheSqlStateAndTheReaderRolesBound()
	{
		var view = ArchiveFailureMapper.Map(Archive(ArchiveFault.QueryTimedOut));

		view.Title.Should().Be(Resources.FailureArchiveQueryTimedOutTitle);
		view.Detail.Should().Contain("scada-host:5432").And.Contain("57014");
		view.Remedy.Should().Be(Resources.FailureArchiveQueryTimedOutRemedy);
	}

	[Fact]
	public void ArchiveReadFailed_WithASqlState_NamesItForTheReport()
	{
		var view = ArchiveFailureMapper.Map(Archive(ArchiveFault.ReadFailed, "22003"));

		view.Title.Should().Be(Resources.FailureArchiveReadFailedTitle);
		view.Detail.Should().Contain("22003");
		view.Remedy.Should().Be(Resources.FormatFailureArchiveReadFailedRemedy("22003"));
	}

	[Fact]
	public void ArchiveReadFailed_WithoutASqlState_PointsAtTheClientSide()
	{
		var view = ArchiveFailureMapper.Map(Archive(ArchiveFault.ReadFailed));

		view.Detail.Should().Be(Resources.FormatFailureArchiveReadUnnamedDetail(ArchiveName));
		view.Remedy.Should().Be(Resources.FailureArchiveReadUnnamedRemedy);
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
		var previous = CultureInfo.CurrentUICulture;
		try
		{
			CultureInfo.CurrentUICulture = new CultureInfo("ru");

			var view = ArchiveFailureMapper.Map(
				new StartupReadTimedOutError(StartupRead.ArchiveExtent, TimeSpan.FromSeconds(30)));

			view.Detail.Should().Be(Resources.FormatFailureStartupReadTimedOutDetail(
				Resources.FailureStartupReadArchiveExtent, 30d));
			view.Detail.Should().NotContain("archive extent").And.NotContain("pen catalogue");
		}
		finally
		{
			CultureInfo.CurrentUICulture = previous;
		}
	}

	// A lost live edge is drawn as a banner over a chart that keeps its history, so the words say what is
	// still true as well as what failed.
	[Fact]
	public void ArchiveConnectionLost_NamesTheRunAndLeavesTheHistoryStanding()
	{
		var view = ArchiveFailureMapper.Map(
			new ArchiveError(ArchiveFault.ConnectionLost, "bench.example", 5432, "semiplot_dev", "3"));

		view.Title.Should().Be(Resources.FailureArchiveConnectionLostTitle);
		view.Detail.Should().Contain("semiplot_dev").And.Contain("bench.example:5432").And.Contain("3");
		view.Remedy.Should().Be(Resources.FailureArchiveConnectionLostRemedy);
	}

	// The words stop where the knowledge stops: no shape is held on this side, so the detail quotes the
	// server and the remedy points at the provisioning that owns the table.
	[Fact]
	public void ArchiveShapeUnexpected_QuotesTheServerAndSendsTheOperatorToTheProvisioning()
	{
		var view = ArchiveFailureMapper.Map(Archive(ArchiveFault.ShapeUnexpected, "column \"v\" does not exist"));

		view.Title.Should().Be(Resources.FailureArchiveShapeUnexpectedTitle);
		view.Detail.Should().Contain("scada-host:5432").And.Contain("column \"v\" does not exist");
		view.Remedy.Should().Be(Resources.FailureArchiveShapeUnexpectedRemedy);
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

	private static string ArgumentsTitle()
	{
		return ArchiveFailureMapper.Map(
			new StartupArgumentsError(StartupArgumentsProblem.Missing, StartupOptions.ConfigDirKey)).Title;
	}

	private static string ArchiveName => Resources.FormatFailureArchiveNameFormat("semiplot", "scada-host:5432");

	private static string Text(string key)
	{
		var value = Resources.ResourceManager.GetString(key, Resources.Culture);
		value.Should().NotBeNullOrWhiteSpace("'{0}' is read by the operator", key);

		return value;
	}

	private static ArchiveError Archive(ArchiveFault kind, string detail = "")
	{
		return new ArchiveError(kind, "scada-host", 5432, "semiplot", detail);
	}
}
