using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using ReactiveUI;

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
	private readonly string _configDirectory = Directory.CreateTempSubdirectory("semiplot-settings-vm-").FullName;

	private readonly string _appDirectory;

	private readonly string _connectionDirectory;

	private readonly MessagePanelViewModel _messagePanel = new();

	public SettingsViewModelTests()
	{
		_appDirectory = Path.Combine(_configDirectory, StartupSequence.SettingsDirectoryName);
		_connectionDirectory = Path.Combine(_configDirectory, StartupProbe.ConnectionDirectoryName);
		ShippedConfiguration.CopyTo(_configDirectory);
	}

	public void Dispose()
	{
		_messagePanel.Dispose();
		Directory.Delete(_configDirectory, recursive: true);
	}

	[AvaloniaFact]
	public void ADirectoryPopulatesEveryField()
	{
		WriteFile(_appDirectory, "app.yaml", "locale: en\ntheme: dark\n");
		WriteFile(
			_connectionDirectory,
			"connection.yaml",
			"host: 10.20.30.40\nport: 5433\ndatabase: archive\nuser: viewer\npassword: secret\n"
			+ "poll_interval_ms: 250\n");

		using var viewModel = Build();

		viewModel.SelectedLanguage!.Token.Should().Be("en");
		viewModel.SelectedTheme!.Token.Should().Be("dark");
		viewModel.Host.Should().Be("10.20.30.40");
		viewModel.Port.Should().Be(5433);
		viewModel.Database.Should().Be("archive");
		viewModel.User.Should().Be("viewer");
		viewModel.Password.Should().Be("secret");
		viewModel.PollInterval.Should().Be(250);
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
		viewModel.PollInterval.Should().Be(1000);
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

	[AvaloniaTheory]
	[InlineData(true, false)]
	[InlineData(false, true)]
	public void AMissingSectionFolderIsReportedAndBlocksTheSaveWithAStatedReason(bool appAbsent, bool connectionAbsent)
	{
		if (appAbsent)
		{
			Directory.Delete(_appDirectory, recursive: true);
		}

		if (connectionAbsent)
		{
			Directory.Delete(_connectionDirectory, recursive: true);
		}

		using var viewModel = Build();
		viewModel.Password = "secret";

		_messagePanel.Entries.Should().ContainSingle();
		CanSave(viewModel).Should().BeFalse();
		viewModel.ValidationMessage.Should().Be(Resources.FormatSettingsSectionUnreadable(
			appAbsent ? Resources.SettingsInterfaceHeader : Resources.SettingsConnectionHeader));
	}

	[AvaloniaFact]
	public void AnAppFileWithoutALocaleRefusesTheSaveAndNamesTheAbsentKey()
	{
		WriteFile(_appDirectory, "app.yaml", "theme: light\n");

		using var viewModel = Build();
		viewModel.SelectedLanguage = viewModel.Languages[0];
		viewModel.Password = "secret";

		CanSave(viewModel).Should().BeFalse();
		viewModel.ValidationMessage.Should().Be(Resources.FormatSettingsKeyAbsent(AppSettingsLoader.LocaleKey));
	}

	[AvaloniaFact]
	public void AConnectionFileWithoutAPortRefusesTheSaveAndNamesTheAbsentKey()
	{
		WriteFile(
			_connectionDirectory,
			"connection.yaml",
			"host: 127.0.0.1\ndatabase: semiplot\nuser: semiplot\npassword: secret\npoll_interval_ms: 1000\n");

		using var viewModel = Build();
		viewModel.Port = 5432;

		CanSave(viewModel).Should().BeFalse();
		viewModel.ValidationMessage.Should().Be(Resources.FormatSettingsKeyAbsent(PostgresConnectionLoader.PortKey));
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
	public async Task ChangingOnlyTheThemeLeavesTheConnectionFileByteIdentical()
	{
		ShippedConfiguration.FillPassword(_configDirectory, "secret");
		var before = File.ReadAllBytes(Path.Combine(_connectionDirectory, "connection.yaml"));
		using var viewModel = Build();
		viewModel.SelectedTheme = viewModel.Themes.Single(choice => choice.Token == "dark");

		await viewModel.SaveCommand.Execute();

		_messagePanel.Entries.Should().BeEmpty();
		viewModel.IsRestartPending.Should().BeTrue("a saved theme takes effect at the next start");
		AppSettingsLoader.Load(_appDirectory).Value.Theme.Should().Be(AppThemeVariant.Dark);
		File.ReadAllBytes(Path.Combine(_connectionDirectory, "connection.yaml")).Should().Equal(before);
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
		viewModel.PollInterval = 250;

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
		connection.PollInterval.Should().Be(TimeSpan.FromMilliseconds(250));
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
	public void TheSaveCannotExecuteWhileThePollIntervalIsNotAPositiveWholeNumber(double? pollInterval)
	{
		using var viewModel = Build();
		viewModel.Password = "secret";

		viewModel.PollInterval = (decimal?)pollInterval;

		viewModel.IsPollIntervalValid.Should().BeFalse();
		CanSave(viewModel).Should().BeFalse();
	}

	[AvaloniaTheory]
	[InlineData("port: not-a-number", "poll_interval_ms: 1000", nameof(SettingsViewModel.Port))]
	[InlineData("port: 70000", "poll_interval_ms: 1000", nameof(SettingsViewModel.Port))]
	[InlineData("port: 5432", "poll_interval_ms: -5", nameof(SettingsViewModel.PollInterval))]
	public void ANumberTheRuleRejectsLoadsAsAnEmptyField(string port, string pollInterval, string field)
	{
		WriteFile(
			_connectionDirectory,
			"connection.yaml",
			$"host: 127.0.0.1\n{port}\ndatabase: archive\nuser: viewer\npassword: secret\n"
			+ $"{pollInterval}\n");

		using var viewModel = Build();

		typeof(SettingsViewModel).GetProperty(field)!.GetValue(viewModel).Should().BeNull();
		CanSave(viewModel).Should().BeFalse();
		viewModel.Host.Should().Be("127.0.0.1");
	}

	[AvaloniaFact]
	public void AHostNameInTheFileLoadsIntoAnInvalidHostField()
	{
		WriteFile(
			_connectionDirectory,
			"connection.yaml",
			"host: scada-01\nport: 5432\ndatabase: archive\nuser: viewer\npassword: secret\n"
			+ "poll_interval_ms: 1000\n");

		using var viewModel = Build();

		viewModel.Host.Should().Be("scada-01");
		viewModel.IsHostValid.Should().BeFalse();
		CanSave(viewModel).Should().BeFalse();
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

		viewModel.PollInterval = null;
		viewModel.Database = string.Empty;
		viewModel.Host = "scada-01";

		viewModel.ValidationMessage.Should().Be(Resources.SettingsHostInvalid);

		viewModel.Host = "127.0.0.1";

		viewModel.ValidationMessage.Should().Be(Resources.FormatSettingsFieldRequired(Resources.SettingsDatabaseLabel));

		viewModel.Database = "archive";
		viewModel.Password = "secret";

		viewModel.ValidationMessage.Should().Be(Resources.SettingsPollIntervalInvalid);

		viewModel.PollInterval = 1000;

		viewModel.ValidationMessage.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void TheValidationMessageNotifiesWithTheFieldItReads()
	{
		using var viewModel = Build();
		var notified = new List<string?>();
		viewModel.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

		viewModel.Port = null;

		notified.Should().Contain(nameof(SettingsViewModel.ValidationMessage));
		viewModel.ValidationMessage.Should().Be(Resources.SettingsPortInvalid);
	}

	private SettingsViewModel Build(Action? restartApplication = null)
	{
		var (app, connection) = SettingsSave.ReadOwned(_configDirectory);

		return new SettingsViewModel(
			_configDirectory,
			app,
			connection,
			_messagePanel,
			NullLogger<SettingsViewModel>.Instance,
			restartApplication ?? (() => { }));
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

	private static void WriteFile(string directory, string name, string content)
	{
		File.WriteAllText(Path.Combine(directory, name), content);
	}
}
