using System.Reactive.Linq;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

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
			+ "source_time_zone: Europe/Berlin\npoll_interval_ms: 250\n");

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
	public void ASectionThatFailedToReadIsReportedOnceAndBlocksTheSave()
	{
		Directory.Delete(_connectionDirectory, recursive: true);

		using var viewModel = Build();

		_messagePanel.Entries.Should().ContainSingle();
		viewModel.Host.Should().BeEmpty();
		viewModel.SelectedLanguage!.Token.Should().Be("ru");
		CanSave(viewModel).Should().BeFalse();
	}

	[AvaloniaFact]
	public void ASectionThatFailedToReadAgainCountsAgainstTheSameEntry()
	{
		Directory.Delete(_connectionDirectory, recursive: true);

		Build().Dispose();

		Build().Dispose();

		_messagePanel.Entries.Should().ContainSingle().Which.RepeatCount.Should().Be(2);
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
		viewModel.IsRestartPending.Should().BeTrue();
		AppSettingsLoader.Load(_appDirectory).Value.Theme.Should().Be(AppThemeVariant.Dark);
		File.ReadAllBytes(Path.Combine(_connectionDirectory, "connection.yaml")).Should().Equal(before);
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

	[AvaloniaFact]
	public async Task ASaveThatRewritesTheConnectionFileKeepsTheTimeZone()
	{
		ShippedConfiguration.FillPassword(_configDirectory, "secret");
		using var viewModel = Build();
		viewModel.Port = 5433;

		await viewModel.SaveCommand.Execute();

		_messagePanel.Entries.Should().BeEmpty();
		var connection = SettingsSave.ReadOwned(_configDirectory).Connection.Value.Values;
		connection[PostgresConnectionLoader.PortKey].Should().Be("5433");
		connection[PostgresConnectionLoader.SourceTimeZoneKey].Should().Be("Europe/Moscow");
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
			+ $"source_time_zone: Europe/Berlin\n{pollInterval}\n");

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
			+ "source_time_zone: Europe/Berlin\npoll_interval_ms: 1000\n");

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

	private SettingsViewModel Build()
	{
		var (app, connection) = SettingsSave.ReadOwned(_configDirectory);

		return new SettingsViewModel(
			_configDirectory, app, connection, _messagePanel, NullLogger<SettingsViewModel>.Instance);
	}

	private static bool CanSave(SettingsViewModel viewModel)
	{
		bool? latest = null;

		using (viewModel.SaveCommand.CanExecute.Subscribe(value => latest = value))
		{
			return latest ?? throw new InvalidOperationException("The command replayed no execute state.");
		}
	}

	private static void WriteFile(string directory, string name, string content)
	{
		File.WriteAllText(Path.Combine(directory, name), content);
	}
}
