using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using ReactiveUI;

using SemiPlot.Core.Configuration;
using SemiPlot.DataSource.OpcUa.Configuration;
using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Settings;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Settings;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class SettingsViewModelTests : IDisposable
{
	private readonly TemporaryDirectory _configDirectory = new("semiplot-settings-vm");

	private readonly string _appDirectory;

	private readonly string _connectionDirectory;

	private readonly string _opcDirectory;

	private readonly MessagePanelViewModel _messagePanel = new();

	public SettingsViewModelTests()
	{
		_appDirectory = Path.Combine(_configDirectory, StartupSequence.SettingsDirectoryName);
		_connectionDirectory = Path.Combine(_configDirectory, StartupProbe.ConnectionDirectoryName);
		_opcDirectory = Path.Combine(_configDirectory, StartupProbe.OpcDirectoryName);
		ShippedConfiguration.CopyTo(_configDirectory);
	}

	public void Dispose()
	{
		_messagePanel.Dispose();
		_configDirectory.Dispose();
	}

	[AvaloniaFact]
	public void ADirectoryPopulatesEveryField()
	{
		WriteFile(_appDirectory, "app.yaml", "locale: en\ntheme: dark\n");
		WriteFile(
			_connectionDirectory,
			"connection.yaml",
			"host: 10.20.30.40\nport: 5433\ndatabase: archive\nuser: viewer\npassword: secret\n");
		WriteFile(_opcDirectory, "opc.yaml", "host: 10.20.30.50\nport: 55875\n");

		using var viewModel = Build();

		viewModel.SelectedLanguage!.Token.Should().Be("en");
		viewModel.SelectedTheme!.Token.Should().Be("dark");
		viewModel.Host.Should().Be("10.20.30.40");
		viewModel.Port.Should().Be(5433);
		viewModel.Database.Should().Be("archive");
		viewModel.User.Should().Be("viewer");
		viewModel.Password.Should().Be("secret");
		viewModel.OpcHost.Should().Be("10.20.30.50");
		viewModel.OpcPort.Should().Be(55875);
		_messagePanel.Entries.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void TheShippedSetPopulatesEveryFieldAndLeavesThePasswordEmpty()
	{
		using var viewModel = Build();

		viewModel.SelectedLanguage!.Token.Should().Be("ru");
		viewModel.SelectedTheme!.Token.Should().Be("light");
		viewModel.Host.Should().Be("127.0.0.1");
		viewModel.Port.Should().Be(5432);
		viewModel.Database.Should().Be("semiplot");
		viewModel.User.Should().Be("semiplot");
		viewModel.Password.Should().BeEmpty();
		viewModel.OpcHost.Should().Be("127.0.0.1");
		viewModel.OpcPort.Should().Be(49875);
		_messagePanel.Entries.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void ALocaleOutsideTheVocabularySelectsNoLanguageAndPopulatesTheRest()
	{
		WriteFile(_appDirectory, "app.yaml", "locale: de\ntheme: light\n");

		using var viewModel = Build();

		viewModel.SelectedLanguage.Should().BeNull();
		viewModel.SelectedTheme!.Token.Should().Be("light");
		viewModel.Host.Should().Be("127.0.0.1");
		viewModel.Port.Should().Be(5432);
	}

	[AvaloniaFact]
	public void ACapitalisedThemeSelectsItsEntry()
	{
		WriteFile(_appDirectory, "app.yaml", "locale: ru\ntheme: Dark\n");

		using var viewModel = Build();

		viewModel.SelectedTheme.Should().BeSameAs(viewModel.Themes.Single(choice => choice.Token == "dark"));
	}

	[AvaloniaFact]
	public void ASectionThatCannotBeReadIsReportedOnceAndBlocksTheSaveWithAStatedReason()
	{
		WriteFile(_connectionDirectory, "connection.yaml", "host: [unclosed\n");

		using var viewModel = Build();

		_messagePanel.Entries.Should().ContainSingle();
		viewModel.Host.Should().BeEmpty();
		viewModel.SelectedLanguage!.Token.Should().Be("ru");
		CanSave(viewModel).Should().BeFalse();
		viewModel.ValidationMessage.Should().Be(
			Resources.FormatSettingsSectionUnreadable(Resources.SettingsConnectionHeader));
	}

	[AvaloniaFact]
	public void ASectionThatCannotBeReadAgainCountsAgainstTheSameEntry()
	{
		WriteFile(_connectionDirectory, "connection.yaml", "host: [unclosed\n");

		Build().Dispose();

		Build().Dispose();

		_messagePanel.Entries.Should().ContainSingle().Which.RepeatCount.Should().Be(2);
	}

	[AvaloniaFact]
	public void ASectionWithADuplicateKeyBlocksTheSaveAndNamesTheSection()
	{
		WriteFile(_appDirectory, "app.yaml", "locale: ru\nlocale: en\ntheme: light\n");

		using var viewModel = Build();

		CanSave(viewModel).Should().BeFalse();
		viewModel.ValidationMessage.Should().Be(
			Resources.FormatSettingsSectionUnreadable(Resources.SettingsInterfaceHeader));
	}

	[AvaloniaFact]
	public void AnUnreadableOpcSectionIsReportedOnceAndBlocksTheSave()
	{
		WriteFile(_opcDirectory, "opc.yaml", "host: [unclosed\n");

		using var viewModel = Build();
		viewModel.Password = "secret";

		_messagePanel.Entries.Should().ContainSingle().Which.View.Detail.Should().Contain(_opcDirectory);
		viewModel.OpcHost.Should().BeEmpty();
		viewModel.OpcPort.Should().BeNull();
		viewModel.Host.Should().Be("127.0.0.1");
		CanSave(viewModel).Should().BeFalse();
		viewModel.ValidationMessage.Should().Be(Resources.FormatSettingsSectionUnreadable(Resources.SettingsOpcHeader));
	}

	[AvaloniaTheory]
	[InlineData(ConfigurationSectionName.App)]
	[InlineData(ConfigurationSectionName.Connection)]
	[InlineData(ConfigurationSectionName.Opc)]
	public void AMissingSectionFolderIsReportedAndBlocksTheSaveWithAStatedReason(ConfigurationSectionName section)
	{
		var (directory, _, header) = SectionOf(section);
		Directory.Delete(directory, recursive: true);

		using var viewModel = Build();
		viewModel.Password = "secret";

		_messagePanel.Entries.Should().ContainSingle();
		CanSave(viewModel).Should().BeFalse();
		viewModel.ValidationMessage.Should().Be(Resources.FormatSettingsSectionUnreadable(header));
	}

	[AvaloniaFact]
	public void AnAppFileWithoutALocaleRefusesTheSaveAndNamesTheAbsentKey()
	{
		WriteFile(_appDirectory, "app.yaml", "theme: light\n");

		using var viewModel = Build();
		viewModel.SelectedLanguage = viewModel.Languages[0];
		viewModel.Password = "secret";

		CanSave(viewModel).Should().BeFalse();
		viewModel.ValidationMessage.Should().Be(
			Resources.FormatSettingsKeyAbsent(Resources.SettingsInterfaceHeader, AppSettingsLoader.LocaleKey));
	}

	[AvaloniaFact]
	public void AConnectionFileWithoutAPortRefusesTheSaveAndNamesTheAbsentKey()
	{
		WriteFile(
			_connectionDirectory,
			"connection.yaml",
			"host: 127.0.0.1\ndatabase: semiplot\nuser: semiplot\npassword: secret\n");

		using var viewModel = Build();
		viewModel.Port = 5432;

		CanSave(viewModel).Should().BeFalse();
		viewModel.ValidationMessage.Should().Be(
			Resources.FormatSettingsKeyAbsent(Resources.SettingsConnectionHeader, PostgresConnectionLoader.PortKey));
	}

	[AvaloniaFact]
	public void AnOpcFileWithoutAHostRefusesTheSaveAndNamesTheSectionAndTheKey()
	{
		WriteFile(_opcDirectory, "opc.yaml", "port: 49875\n");

		using var viewModel = Build();
		viewModel.Password = "secret";

		CanSave(viewModel).Should().BeFalse();
		viewModel.ValidationMessage.Should().Be(
			Resources.FormatSettingsKeyAbsent(Resources.SettingsOpcHeader, OpcUaConnectionLoader.HostKey));
		_messagePanel.Entries.Should().BeEmpty();
	}

	[AvaloniaTheory]
	[InlineData(ConfigurationSectionName.App, ConfigurationSectionName.Connection)]
	[InlineData(ConfigurationSectionName.Connection, ConfigurationSectionName.Opc)]
	public void OfTwoUnreadableSectionsTheMessageNamesTheEarlier(
		ConfigurationSectionName earlier, ConfigurationSectionName later)
	{
		var (earlierDirectory, earlierFile, header) = SectionOf(earlier);
		var (laterDirectory, laterFile, _) = SectionOf(later);
		WriteFile(earlierDirectory, earlierFile, "host: [unclosed\n");
		WriteFile(laterDirectory, laterFile, "host: [unclosed\n");

		using var viewModel = Build();

		viewModel.ValidationMessage.Should().Be(Resources.FormatSettingsSectionUnreadable(header));
	}

	[AvaloniaTheory]
	[InlineData(ConfigurationSectionName.App, ConfigurationSectionName.Connection)]
	[InlineData(ConfigurationSectionName.Connection, ConfigurationSectionName.Opc)]
	public void OfTwoSectionsLackingAKeyTheMessageNamesTheEarlier(
		ConfigurationSectionName earlier, ConfigurationSectionName later)
	{
		var (header, key) = RewriteWithoutOneEditedKey(earlier);
		RewriteWithoutOneEditedKey(later);

		using var viewModel = Build();

		viewModel.ValidationMessage.Should().Be(Resources.FormatSettingsKeyAbsent(header, key));
	}

	[AvaloniaFact]
	public void AnUnreadableSectionIsNamedBeforeAKeyAnEarlierSectionLacks()
	{
		RewriteWithoutOneEditedKey(ConfigurationSectionName.Connection);
		WriteFile(_opcDirectory, "opc.yaml", "host: [unclosed\n");

		using var viewModel = Build();

		viewModel.ValidationMessage.Should().Be(Resources.FormatSettingsSectionUnreadable(Resources.SettingsOpcHeader));
	}

	[AvaloniaFact]
	public async Task ARefusedSaveAddsOnePanelEntryAndNoRestartNotice()
	{
		using var viewModel = Build();
		viewModel.Password = "secret";
		viewModel.Port = 70000;
		var before = File.ReadAllBytes(Path.Combine(_connectionDirectory, "connection.yaml"));

		await viewModel.SaveCommand.Execute();

		_messagePanel.Entries.Should().ContainSingle().Which.View.Severity.Should().Be(MessageSeverity.Error);
		viewModel.IsRestartPending.Should().BeFalse();
		File.ReadAllBytes(Path.Combine(_connectionDirectory, "connection.yaml")).Should().Equal(before);
	}

	[AvaloniaFact]
	public async Task ChangingOnlyTheTheme_LeavesTheConnectionFileByteIdenticalAndSetsNoRestartNotice()
	{
		ShippedConfiguration.FillPassword(_configDirectory, "secret");
		var before = File.ReadAllBytes(Path.Combine(_connectionDirectory, "connection.yaml"));
		var opcBefore = File.ReadAllBytes(Path.Combine(_opcDirectory, "opc.yaml"));
		using var viewModel = Build();
		viewModel.SelectedTheme = viewModel.Themes.Single(choice => choice.Token == "dark");

		await viewModel.SaveCommand.Execute();

		_messagePanel.Entries.Should().BeEmpty();
		viewModel.IsRestartPending.Should().BeFalse("every process applies a saved theme live");
		AppSettingsLoader.Load(_appDirectory).Value.Theme.Should().Be(AppThemeVariant.Dark);
		File.ReadAllBytes(Path.Combine(_connectionDirectory, "connection.yaml")).Should().Equal(before);
		File.ReadAllBytes(Path.Combine(_opcDirectory, "opc.yaml")).Should().Equal(opcBefore);
	}

	[AvaloniaFact]
	public async Task ALanguageSaveSetsTheRestartNotice()
	{
		ShippedConfiguration.FillPassword(_configDirectory, "secret");
		using var viewModel = Build();
		var other = viewModel.Languages.Single(choice => choice.Token != viewModel.SelectedLanguage!.Token);
		viewModel.SelectedLanguage = other;

		await viewModel.SaveCommand.Execute();

		viewModel.IsRestartPending.Should().BeTrue();
		viewModel.RestartNotice.Should().Be(Resources.SettingsRestartNotice);
	}

	[AvaloniaFact]
	public async Task AThemeAndLanguageSaveSetsTheRestartNotice()
	{
		ShippedConfiguration.FillPassword(_configDirectory, "secret");
		using var viewModel = Build();
		var other = viewModel.Languages.Single(choice => choice.Token != viewModel.SelectedLanguage!.Token);
		viewModel.SelectedLanguage = other;
		viewModel.SelectedTheme = viewModel.Themes.Single(choice => choice.Token == "dark");

		await viewModel.SaveCommand.Execute();

		viewModel.IsRestartPending.Should().BeTrue();
		viewModel.RestartNotice.Should().Be(Resources.SettingsRestartNoticeThemeApplied, "the theme half applies live");
	}

	[AvaloniaFact]
	public async Task AConnectionSaveSetsTheRestartNotice()
	{
		using var viewModel = Build();
		viewModel.Password = "secret";

		await viewModel.SaveCommand.Execute();

		viewModel.IsRestartPending.Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task AnOpcSaveSetsTheRestartNotice()
	{
		ShippedConfiguration.FillPassword(_configDirectory, "secret");
		var connectionBefore = File.ReadAllBytes(Path.Combine(_connectionDirectory, "connection.yaml"));
		using var viewModel = Build();
		viewModel.OpcPort = 55875;

		await viewModel.SaveCommand.Execute();

		_messagePanel.Entries.Should().BeEmpty();
		viewModel.IsRestartPending.Should().BeTrue("the OPC UA session reads its address at the start");
		viewModel.RestartNotice.Should().Be(Resources.SettingsRestartNotice);
		OpcUaConnectionLoader.Load(_opcDirectory).Value.Port.Should().Be(55875);
		File.ReadAllBytes(Path.Combine(_connectionDirectory, "connection.yaml")).Should().Equal(connectionBefore);
	}

	[AvaloniaFact]
	public async Task RestartNow_RunsTheHandedRestartOnlyWhileTheNoticeShows()
	{
		var restarts = 0;
		using var viewModel = Build(() => restarts++);
		CanRestartNow(viewModel).Should().BeFalse();
		viewModel.Password = "secret";

		await viewModel.SaveCommand.Execute();

		await HeadlessWait.Until(() => CanRestartNow(viewModel));
		await viewModel.RestartNowCommand.Execute();
		restarts.Should().Be(1);
	}

	[AvaloniaFact]
	public async Task RestartNow_CannotExecuteWhileASaveRuns()
	{
		using var viewModel = Build();
		viewModel.Password = "secret";
		await viewModel.SaveCommand.Execute();
		await HeadlessWait.Until(() => CanRestartNow(viewModel));
		viewModel.SelectedTheme = viewModel.Themes.Single(choice => choice.Token == "dark");

		var running = viewModel.SaveCommand.Execute().ToTask();

		CanRestartNow(viewModel).Should().BeFalse("the restart would end the process in the middle of the save");
		await running;
		await HeadlessWait.Until(() => CanRestartNow(viewModel));
	}

	[AvaloniaFact]
	public async Task EveryFieldTheWindowEditsReachesTheFileTheLoadersRead()
	{
		using var viewModel = Build();
		viewModel.SelectedLanguage = viewModel.Languages.Single(choice => choice.Token == "en");
		viewModel.SelectedTheme = viewModel.Themes.Single(choice => choice.Token == "dark");
		viewModel.Host = "10.20.30.40";
		viewModel.Port = 5433;
		viewModel.Database = "archive";
		viewModel.User = "viewer";
		viewModel.Password = "secret";
		viewModel.OpcHost = "10.20.30.50";
		viewModel.OpcPort = 55875;

		await viewModel.SaveCommand.Execute();

		_messagePanel.Entries.Should().BeEmpty();
		var app = AppSettingsLoader.Load(_appDirectory).Value;
		app.Locale.Should().Be(UiLanguage.En);
		app.Theme.Should().Be(AppThemeVariant.Dark);
		var connection = PostgresConnectionLoader.Load(_connectionDirectory).Value;
		connection.Host.Should().Be("10.20.30.40");
		connection.Port.Should().Be(5433);
		connection.Database.Should().Be("archive");
		connection.Username.Should().Be("viewer");
		connection.Password.Should().Be("secret");
		var opc = OpcUaConnectionLoader.Load(_opcDirectory).Value;
		opc.Host.Should().Be("10.20.30.50");
		opc.Port.Should().Be(55875);
	}

	[AvaloniaFact]
	public async Task ASecondSaveWritesOnlyWhatChangedSinceTheFirst()
	{
		using var viewModel = Build();
		viewModel.Password = "secret";
		await viewModel.SaveCommand.Execute();
		WriteFile(_connectionDirectory, "connection.yaml", File.ReadAllText(
			Path.Combine(_connectionDirectory, "connection.yaml")).Replace("secret", "changed-elsewhere"));
		viewModel.SelectedTheme = viewModel.Themes.Single(choice => choice.Token == "dark");

		await viewModel.SaveCommand.Execute();

		_messagePanel.Entries.Should().BeEmpty();
		File.ReadAllText(Path.Combine(_connectionDirectory, "connection.yaml")).Should().Contain("changed-elsewhere");
	}

	[AvaloniaFact]
	public async Task ASaveSendsOnlyTheChangedKeyOfASection()
	{
		ShippedConfiguration.FillPassword(_configDirectory, "secret");
		using var viewModel = Build();
		WriteFile(_connectionDirectory, "connection.yaml", File.ReadAllText(
			Path.Combine(_connectionDirectory, "connection.yaml")).Replace("127.0.0.1", "10.0.0.9"));
		viewModel.Port = 5433;

		await viewModel.SaveCommand.Execute();

		_messagePanel.Entries.Should().BeEmpty();
		var connection = PostgresConnectionLoader.Load(_connectionDirectory).Value;
		connection.Host.Should().Be("10.0.0.9");
		connection.Port.Should().Be(5433);
	}

	[AvaloniaTheory]
	[InlineData(nameof(SettingsViewModel.Host), nameof(SettingsViewModel.IsHostValid))]
	[InlineData(nameof(SettingsViewModel.Database), nameof(SettingsViewModel.IsDatabaseValid))]
	[InlineData(nameof(SettingsViewModel.User), nameof(SettingsViewModel.IsUserValid))]
	[InlineData(nameof(SettingsViewModel.Password), nameof(SettingsViewModel.IsPasswordValid))]
	[InlineData(nameof(SettingsViewModel.OpcHost), nameof(SettingsViewModel.IsOpcHostValid))]
	public void TheSaveCannotExecuteWhileATextFieldIsBlank(string field, string validity)
	{
		using var viewModel = Build();
		viewModel.Password = "secret";
		CanSave(viewModel).Should().BeTrue();

		typeof(SettingsViewModel).GetProperty(field)!.SetValue(viewModel, " ");

		CanSave(viewModel).Should().BeFalse();
		typeof(SettingsViewModel).GetProperty(validity)!.GetValue(viewModel).Should().Be(false);
	}

	[AvaloniaTheory]
	[InlineData(null)]
	[InlineData(0d)]
	[InlineData(65536d)]
	[InlineData(5432.5)]
	public void TheSaveCannotExecuteWhileThePortIsNotAWholeNumberInRange(double? port)
	{
		using var viewModel = Build();
		viewModel.Password = "secret";

		viewModel.Port = (decimal?)port;

		viewModel.IsPortValid.Should().BeFalse();
		CanSave(viewModel).Should().BeFalse();
	}

	[AvaloniaTheory]
	[InlineData(null)]
	[InlineData(0d)]
	[InlineData(65536d)]
	[InlineData(49875.5)]
	public void TheSaveCannotExecuteWhileTheOpcPortIsNotAWholeNumberInRange(double? port)
	{
		using var viewModel = Build();
		viewModel.Password = "secret";
		CanSave(viewModel).Should().BeTrue();

		viewModel.OpcPort = (decimal?)port;

		viewModel.IsOpcPortValid.Should().BeFalse();
		viewModel.IsPortValid.Should().BeTrue("the archive port is a field of its own");
		CanSave(viewModel).Should().BeFalse();
	}

	[AvaloniaTheory]
	[InlineData("port: not-a-number")]
	[InlineData("port: 70000")]
	public void APortTheRuleRejectsLoadsAsAnEmptyField(string port)
	{
		WriteFile(
			_connectionDirectory,
			"connection.yaml",
			$"host: 127.0.0.1\n{port}\ndatabase: archive\nuser: viewer\npassword: secret\n");

		using var viewModel = Build();

		viewModel.Port.Should().BeNull();
		CanSave(viewModel).Should().BeFalse();
		viewModel.Host.Should().Be("127.0.0.1");
	}

	[AvaloniaFact]
	public void AHostNameInTheFileLoadsIntoAnInvalidHostField()
	{
		WriteFile(
			_connectionDirectory,
			"connection.yaml",
			"host: scada-01\nport: 5432\ndatabase: archive\nuser: viewer\npassword: secret\n");

		using var viewModel = Build();

		viewModel.Host.Should().Be("scada-01");
		viewModel.IsHostValid.Should().BeFalse();
		CanSave(viewModel).Should().BeFalse();
		_messagePanel.Entries.Should().BeEmpty();
	}

	[AvaloniaTheory]
	[InlineData("port: not-a-number")]
	[InlineData("port: 70000")]
	public void AnOpcPortTheRuleRejectsLoadsAsAnEmptyField(string port)
	{
		WriteFile(_opcDirectory, "opc.yaml", $"host: 127.0.0.1\n{port}\n");

		using var viewModel = Build();
		viewModel.Password = "secret";

		viewModel.OpcPort.Should().BeNull();
		viewModel.IsOpcPortValid.Should().BeFalse();
		CanSave(viewModel).Should().BeFalse();
		viewModel.OpcHost.Should().Be("127.0.0.1");
		_messagePanel.Entries.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void AHostNameInTheOpcFileLoadsIntoAnInvalidOpcHostField()
	{
		WriteFile(_opcDirectory, "opc.yaml", "host: scada-01\nport: 49875\n");

		using var viewModel = Build();
		viewModel.Password = "secret";

		viewModel.OpcHost.Should().Be("scada-01");
		viewModel.IsOpcHostValid.Should().BeFalse();
		CanSave(viewModel).Should().BeFalse();
		viewModel.IsHostValid.Should().BeTrue("the archive host is a field of its own");
		_messagePanel.Entries.Should().BeEmpty();
	}

	[AvaloniaTheory]
	[InlineData(nameof(SettingsViewModel.SelectedLanguage))]
	[InlineData(nameof(SettingsViewModel.SelectedTheme))]
	public void TheSaveCannotExecuteWithoutAChoice(string choice)
	{
		using var viewModel = Build();
		viewModel.Password = "secret";
		CanSave(viewModel).Should().BeTrue();

		typeof(SettingsViewModel).GetProperty(choice)!.SetValue(viewModel, null);

		CanSave(viewModel).Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task ASaveWithNoEditShowsNoRestartNotice()
	{
		ShippedConfiguration.FillPassword(_configDirectory, "secret");
		var before = File.ReadAllBytes(Path.Combine(_connectionDirectory, "connection.yaml"));
		using var viewModel = Build();

		await viewModel.SaveCommand.Execute();

		_messagePanel.Entries.Should().BeEmpty();
		viewModel.IsRestartPending.Should().BeFalse();
		File.ReadAllBytes(Path.Combine(_connectionDirectory, "connection.yaml")).Should().Equal(before);
	}

	[AvaloniaFact]
	public async Task AKeyTheFileSpellsInAnotherCaseIsRewrittenUnderTheLoadersSpelling()
	{
		ShippedConfiguration.FillPassword(_configDirectory, "secret");
		WriteFile(_connectionDirectory, "connection.yaml", File.ReadAllText(
			Path.Combine(_connectionDirectory, "connection.yaml")).Replace("host:", "Host:"));
		PostgresConnectionLoader.Load(_connectionDirectory).IsFailed.Should().BeTrue();
		using var viewModel = Build();
		viewModel.Host.Should().Be("127.0.0.1");

		await viewModel.SaveCommand.Execute();

		_messagePanel.Entries.Should().BeEmpty();
		PostgresConnectionLoader.Load(_connectionDirectory).Value.Host.Should().Be("127.0.0.1");
	}

	[AvaloniaFact]
	public void TheValidationMessageNamesTheFirstInvalidFieldInFormOrder()
	{
		using var viewModel = Build();
		viewModel.ValidationMessage.Should().Be(
			Resources.FormatSettingsFieldRequired(Resources.SettingsPasswordLabel), "the shipped password is empty");

		viewModel.OpcHost = "scada-01";
		viewModel.OpcPort = null;

		viewModel.ValidationMessage.Should().Be(
			Resources.FormatSettingsFieldRequired(Resources.SettingsPasswordLabel), "the OPC UA block follows the archive");

		viewModel.Database = string.Empty;
		viewModel.Host = "scada-01";

		viewModel.ValidationMessage.Should().Be(Resources.SettingsHostInvalid);

		viewModel.Host = "127.0.0.1";

		viewModel.ValidationMessage.Should().Be(Resources.FormatSettingsFieldRequired(Resources.SettingsDatabaseLabel));

		viewModel.Database = "archive";

		viewModel.ValidationMessage.Should().Be(Resources.FormatSettingsFieldRequired(Resources.SettingsPasswordLabel));

		viewModel.Password = "secret";

		viewModel.ValidationMessage.Should().Be(Resources.SettingsOpcHostInvalid);

		viewModel.OpcHost = "127.0.0.1";

		viewModel.ValidationMessage.Should().Be(Resources.SettingsOpcPortInvalid);

		viewModel.OpcPort = 49875;

		viewModel.ValidationMessage.Should().BeEmpty();
	}

	[AvaloniaTheory]
	[InlineData(nameof(SettingsViewModel.Port), nameof(Resources.SettingsPortInvalid))]
	[InlineData(nameof(SettingsViewModel.OpcPort), nameof(Resources.SettingsOpcPortInvalid))]
	public void TheValidationMessageNotifiesWithTheFieldItReads(string field, string message)
	{
		using var viewModel = Build();
		viewModel.Password = "secret";
		var notified = new List<string?>();
		viewModel.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

		typeof(SettingsViewModel).GetProperty(field)!.SetValue(viewModel, null);

		notified.Should().Contain(nameof(SettingsViewModel.ValidationMessage));
		viewModel.ValidationMessage.Should().Be(Resources.ResourceManager.GetString(message, Resources.Culture));
	}

	private SettingsViewModel Build(Action? restartApplication = null)
	{
		return SettingsViewModelStand.Build(_configDirectory, _messagePanel, restartApplication);
	}

	private static bool CanSave(SettingsViewModel viewModel)
	{
		return LatestOf(viewModel.SaveCommand);
	}

	private static bool CanRestartNow(SettingsViewModel viewModel)
	{
		return LatestOf(viewModel.RestartNowCommand);
	}

	private static bool LatestOf<TInput, TOutput>(ReactiveCommand<TInput, TOutput> command)
	{
		bool? latest = null;

		using (command.CanExecute.Subscribe(value => latest = value))
		{
			return latest ?? throw new InvalidOperationException("The command replayed no execute state.");
		}
	}

	private (string Directory, string File, string Header) SectionOf(ConfigurationSectionName section)
	{
		return section switch
		{
			ConfigurationSectionName.App => (_appDirectory, "app.yaml", Resources.SettingsInterfaceHeader),
			ConfigurationSectionName.Connection =>
				(_connectionDirectory, "connection.yaml", Resources.SettingsConnectionHeader),
			ConfigurationSectionName.Opc => (_opcDirectory, "opc.yaml", Resources.SettingsOpcHeader),
			_ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
		};
	}

	private (string Header, string Key) RewriteWithoutOneEditedKey(ConfigurationSectionName section)
	{
		var (directory, file, header) = SectionOf(section);
		var (content, key) = section switch
		{
			ConfigurationSectionName.App => ("theme: light\n", AppSettingsLoader.LocaleKey),
			ConfigurationSectionName.Connection => (
				"host: 127.0.0.1\ndatabase: semiplot\nuser: semiplot\npassword: secret\n",
				PostgresConnectionLoader.PortKey),
			ConfigurationSectionName.Opc => ("port: 49875\n", OpcUaConnectionLoader.HostKey),
			_ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
		};
		WriteFile(directory, file, content);

		return (header, key);
	}

	private static void WriteFile(string directory, string name, string content)
	{
		File.WriteAllText(Path.Combine(directory, name), content);
	}
}
