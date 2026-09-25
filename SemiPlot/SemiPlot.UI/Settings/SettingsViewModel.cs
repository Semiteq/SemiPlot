using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiPlot.Core.Configuration;
using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Startup;

namespace SemiPlot.UI.Settings;

/// <summary>One entry of a settings combo box: the token the file carries and the label the operator reads.</summary>
public sealed record SettingsChoice(string Token, string Label);

/// <summary>
/// The settings window's state, populated from the section files as they are, never from the typed loaders,
/// so the window opens with the values even when the start failed on them.
/// </summary>
public sealed class SettingsViewModel : ReactiveObject, IDisposable
{
	private readonly string _configDirectory;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly ILogger<SettingsViewModel> _logger;
	private Dictionary<string, string> _loadedApp;
	private Dictionary<string, string> _loadedConnection;

	public SettingsViewModel(
		string configDirectory,
		Result<OwnedSection> app,
		Result<OwnedSection> connection,
		MessagePanelViewModel messagePanel,
		ILogger<SettingsViewModel> logger)
	{
		_configDirectory = configDirectory;
		_messagePanel = messagePanel;
		_logger = logger;

		Languages = [.. SettingsVocabulary.Languages.Select(ChoiceOf)];
		Themes = [.. SettingsVocabulary.Themes.Select(ChoiceOf)];

		var appValues = ValuesOf(app);
		var connectionValues = ValuesOf(connection);

		SelectedLanguage = Match(Languages, appValues.GetValueOrDefault(AppSettingsLoader.LocaleKey));
		SelectedTheme = Match(Themes, appValues.GetValueOrDefault(AppSettingsLoader.ThemeKey));
		Host = connectionValues.GetValueOrDefault(PostgresConnectionLoader.HostKey, string.Empty);
		Port = NumberOf(connectionValues, PostgresConnectionLoader.PortKey, LowestPort, HighestPort);
		Database = connectionValues.GetValueOrDefault(PostgresConnectionLoader.DatabaseKey, string.Empty);
		User = connectionValues.GetValueOrDefault(PostgresConnectionLoader.UserKey, string.Empty);
		Password = connectionValues.GetValueOrDefault(PostgresConnectionLoader.PasswordKey, string.Empty);
		PollInterval = NumberOf(
			connectionValues, PostgresConnectionLoader.PollIntervalKey, LowestPollInterval, HighestPollInterval);

		_loadedApp = SpelledAsInTheFile(CurrentApp(), appValues);
		_loadedConnection = SpelledAsInTheFile(CurrentConnection(), connectionValues);

		var bothLoaded = app.IsSuccess && connection.IsSuccess;
		var canSave = this.WhenAnyValue(
			vm => vm.SelectedLanguage,
			vm => vm.SelectedTheme,
			vm => vm.IsHostValid,
			vm => vm.IsPortValid,
			vm => vm.IsDatabaseValid,
			vm => vm.IsUserValid,
			vm => vm.IsPasswordValid,
			vm => vm.IsPollIntervalValid,
			(language, theme, host, port, database, user, password, pollInterval) =>
				bothLoaded
				&& language is not null
				&& theme is not null
				&& host
				&& port
				&& database
				&& user
				&& password
				&& pollInterval);

		SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, canSave);
	}

	public static decimal LowestPort => PostgresConnectionLoader.LowestPort;

	public static decimal HighestPort => PostgresConnectionLoader.HighestPort;

	public static decimal LowestPollInterval => PostgresConnectionLoader.LowestPollIntervalMs;

	public static decimal HighestPollInterval => int.MaxValue;

	public IReadOnlyList<SettingsChoice> Languages { get; }

	public IReadOnlyList<SettingsChoice> Themes { get; }

	/// <summary>Null for a token outside the vocabulary, which keeps the save disabled.</summary>
	public SettingsChoice? SelectedLanguage
	{
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public SettingsChoice? SelectedTheme
	{
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public string Host
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(IsHostValid));
			this.RaisePropertyChanged(nameof(ValidationMessage));
		}
	}

	/// <summary>Null while the field holds no whole number in range, which keeps the save disabled.</summary>
	public decimal? Port
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(IsPortValid));
			this.RaisePropertyChanged(nameof(ValidationMessage));
		}
	}

	public string Database
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(IsDatabaseValid));
			this.RaisePropertyChanged(nameof(ValidationMessage));
		}
	}

	public string User
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(IsUserValid));
			this.RaisePropertyChanged(nameof(ValidationMessage));
		}
	}

	public string Password
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(IsPasswordValid));
			this.RaisePropertyChanged(nameof(ValidationMessage));
		}
	}

	public decimal? PollInterval
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(IsPollIntervalValid));
			this.RaisePropertyChanged(nameof(ValidationMessage));
		}
	}

	public bool IsHostValid => PostgresConnectionLoader.IsIPv4Address(Host);

	public bool IsPortValid => IsWholeNumberIn(Port, LowestPort, HighestPort);

	public bool IsDatabaseValid => !string.IsNullOrWhiteSpace(Database);

	public bool IsUserValid => !string.IsNullOrWhiteSpace(User);

	public bool IsPasswordValid => !string.IsNullOrWhiteSpace(Password);

	public bool IsPollIntervalValid => IsWholeNumberIn(PollInterval, LowestPollInterval, HighestPollInterval);

	/// <summary>The rule the first invalid field breaks, in form order; empty while every field is valid.</summary>
	public string ValidationMessage => FirstBrokenRule();

	/// <summary>Set by a save that wrote something: what it wrote takes effect at the next start.</summary>
	public bool IsRestartPending
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public ReactiveCommand<Unit, Unit> SaveCommand { get; }

	public void Dispose()
	{
		SaveCommand.Dispose();
	}

	private async Task SaveAsync()
	{
		var app = CurrentApp();
		var connection = CurrentConnection();
		var appEdits = EditsOf(app, _loadedApp);
		var connectionEdits = EditsOf(connection, _loadedConnection);
		var edits = new Dictionary<ConfigurationSectionName, IReadOnlyDictionary<string, string>>
		{
			[ConfigurationSectionName.App] = appEdits,
			[ConfigurationSectionName.Connection] = connectionEdits
		};

		var saved = await Task.Run(() => SettingsSave.Save(_configDirectory, edits, _logger));

		if (saved.IsFailed)
		{
			_messagePanel.ReportFailure(saved, _logger);

			return;
		}

		_loadedApp = app;
		_loadedConnection = connection;

		if (appEdits.Count > 0 || connectionEdits.Count > 0)
		{
			IsRestartPending = true;
		}
	}

	private string FirstBrokenRule()
	{
		if (!IsHostValid)
		{
			return Resources.SettingsHostInvalid;
		}

		if (!IsPortValid)
		{
			return Resources.SettingsPortInvalid;
		}

		if (!IsDatabaseValid)
		{
			return Resources.FormatSettingsFieldRequired(Resources.SettingsDatabaseLabel);
		}

		if (!IsUserValid)
		{
			return Resources.FormatSettingsFieldRequired(Resources.SettingsUserLabel);
		}

		if (!IsPasswordValid)
		{
			return Resources.FormatSettingsFieldRequired(Resources.SettingsPasswordLabel);
		}

		return IsPollIntervalValid ? string.Empty : Resources.SettingsPollIntervalInvalid;
	}

	private Dictionary<string, string> CurrentApp()
	{
		var current = new Dictionary<string, string>(StringComparer.Ordinal);

		if (SelectedLanguage is not null)
		{
			current[AppSettingsLoader.LocaleKey] = SelectedLanguage.Token;
		}

		if (SelectedTheme is not null)
		{
			current[AppSettingsLoader.ThemeKey] = SelectedTheme.Token;
		}

		return current;
	}

	private Dictionary<string, string> CurrentConnection()
	{
		return new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[PostgresConnectionLoader.HostKey] = Host,
			[PostgresConnectionLoader.PortKey] = TextOf(Port),
			[PostgresConnectionLoader.DatabaseKey] = Database,
			[PostgresConnectionLoader.UserKey] = User,
			[PostgresConnectionLoader.PasswordKey] = Password,
			[PostgresConnectionLoader.PollIntervalKey] = TextOf(PollInterval)
		};
	}

	// docs/architecture/overview.md#the-settings-window
	private static Dictionary<string, string> SpelledAsInTheFile(
		Dictionary<string, string> current, IReadOnlyDictionary<string, string> values)
	{
		return current
			.Where(pair => values.Keys.Contains(pair.Key, StringComparer.Ordinal))
			.ToDictionary(StringComparer.Ordinal);
	}

	private static Dictionary<string, string> EditsOf(
		Dictionary<string, string> current, Dictionary<string, string> loaded)
	{
		return current
			.Where(pair => !(loaded.TryGetValue(pair.Key, out var text) && text == pair.Value))
			.ToDictionary(StringComparer.Ordinal);
	}

	private IReadOnlyDictionary<string, string> ValuesOf(Result<OwnedSection> section)
	{
		if (section.IsFailed)
		{
			_messagePanel.ReportFailure(section, _logger);

			return new Dictionary<string, string>();
		}

		return section.Value.Values;
	}

	private static decimal? NumberOf(
		IReadOnlyDictionary<string, string> values, string key, decimal lowest, decimal highest)
	{
		var parsed = int.TryParse(
			values.GetValueOrDefault(key), NumberStyles.None, CultureInfo.InvariantCulture, out var number);

		return parsed && IsWholeNumberIn(number, lowest, highest) ? number : null;
	}

	private static bool IsWholeNumberIn(decimal? value, decimal lowest, decimal highest)
	{
		return value is { } number && number == decimal.Truncate(number) && number >= lowest && number <= highest;
	}

	private static string TextOf(decimal? value)
	{
		return value is { } number ? number.ToString("0", CultureInfo.InvariantCulture) : string.Empty;
	}

	private static SettingsChoice? Match(IReadOnlyList<SettingsChoice> choices, string? value)
	{
		return choices.FirstOrDefault(choice => AppSettingsLoader.MatchesToken(choice.Token, value));
	}

	private static SettingsChoice ChoiceOf(UiLanguageEntry entry)
	{
		var label = entry.Language switch
		{
			UiLanguage.Ru => Resources.SettingsLanguageRussian,
			UiLanguage.En => Resources.SettingsLanguageEnglish,
			_ => throw new ArgumentOutOfRangeException(nameof(entry), entry.Language, null)
		};

		return new SettingsChoice(entry.YamlToken, label);
	}

	private static SettingsChoice ChoiceOf(AppThemeEntry entry)
	{
		var label = entry.Theme switch
		{
			AppThemeVariant.Light => Resources.SettingsThemeLight,
			AppThemeVariant.Dark => Resources.SettingsThemeDark,
			_ => throw new ArgumentOutOfRangeException(nameof(entry), entry.Theme, null)
		};

		return new SettingsChoice(entry.YamlToken, label);
	}
}
