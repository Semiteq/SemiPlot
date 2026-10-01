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

/// <summary>One key the dialog edits and how to read its current text off the view model.</summary>
internal sealed record EditedKey(string Key, Func<SettingsViewModel, string?> Read);

/// <summary>
/// The settings window's state, populated from the section files as they are, never from the typed loaders,
/// so the window opens with the values even when the start failed on them.
/// </summary>
public sealed class SettingsViewModel : ReactiveObject, IDisposable
{
	private readonly string _configDirectory;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly ILogger<SettingsViewModel> _logger;
	private readonly string _sectionRefusal;
	private Dictionary<string, string> _loadedApp;
	private Dictionary<string, string> _loadedConnection;

	public SettingsViewModel(
		string configDirectory,
		Result<OwnedSection> app,
		Result<OwnedSection> connection,
		MessagePanelViewModel messagePanel,
		ILogger<SettingsViewModel> logger,
		Action restartApplication)
	{
		_configDirectory = configDirectory;
		_messagePanel = messagePanel;
		_logger = logger;

		Languages = [.. SettingsVocabulary.Languages.Select(ChoiceOf)];
		Themes = [.. SettingsVocabulary.Themes.Select(ChoiceOf)];

		var appValues = ValuesOf(app);
		var connectionValues = ValuesOf(connection);
		_sectionRefusal = RefusalOf(app, connection);

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

		var canWriteSections = _sectionRefusal.Length == 0;
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
				canWriteSections
				&& language is not null
				&& theme is not null
				&& host
				&& port
				&& database
				&& user
				&& password
				&& pollInterval);

		SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, canSave);
		var canRestartNow = this.WhenAnyValue(vm => vm.IsRestartPending)
			.CombineLatest(SaveCommand.IsExecuting, (pending, saving) => pending && !saving);

		RestartNowCommand = ReactiveCommand.Create(restartApplication, canRestartNow);
	}

	/// <summary>The keys the dialog edits per section, with the field each reads; null means no value to write.</summary>
	internal static IReadOnlyList<EditedKey> AppKeys { get; } =
	[
		new(AppSettingsLoader.LocaleKey, vm => vm.SelectedLanguage?.Token),
		new(AppSettingsLoader.ThemeKey, vm => vm.SelectedTheme?.Token)
	];

	internal static IReadOnlyList<EditedKey> ConnectionKeys { get; } =
	[
		new(PostgresConnectionLoader.HostKey, vm => vm.Host),
		new(PostgresConnectionLoader.PortKey, vm => TextOf(vm.Port)),
		new(PostgresConnectionLoader.DatabaseKey, vm => vm.Database),
		new(PostgresConnectionLoader.UserKey, vm => vm.User),
		new(PostgresConnectionLoader.PasswordKey, vm => vm.Password),
		new(PostgresConnectionLoader.PollIntervalKey, vm => TextOf(vm.PollInterval))
	];

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
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(ValidationMessage));
		}
	}

	public SettingsChoice? SelectedTheme
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(ValidationMessage));
		}
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

	/// <summary>
	/// The section a file cannot be read from or a key it lacks, else the rule the first invalid field breaks in form
	/// order; empty while the form can save.
	/// </summary>
	public string ValidationMessage => FirstBrokenRule();

	/// <summary>Set by a save that wrote a key other than the theme, which every process applies live.</summary>
	public bool IsRestartPending
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>The notice for the last save, which names the theme as applied when that save also wrote it.</summary>
	public string RestartNotice
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = Resources.SettingsRestartNotice;

	public ReactiveCommand<Unit, Unit> SaveCommand { get; }

	public ReactiveCommand<Unit, Unit> RestartNowCommand { get; }

	/// <summary>Reads the section files off the UI thread and builds the view model over what they hold.</summary>
	public static async Task<SettingsViewModel> OpenAsync(
		string configDirectory,
		MessagePanelViewModel messagePanel,
		ILoggerFactory loggerFactory,
		Action restartApplication)
	{
		var (app, connection) = await Task.Run(() => SettingsSave.ReadOwned(configDirectory));

		return new SettingsViewModel(
			configDirectory,
			app,
			connection,
			messagePanel,
			loggerFactory.CreateLogger<SettingsViewModel>(),
			restartApplication);
	}

	public void Dispose()
	{
		SaveCommand.Dispose();
		RestartNowCommand.Dispose();
	}

	/// <summary>Moves the loaded theme, and an untouched selection, onto the theme the process applied.</summary>
	internal void FollowAppliedTheme(AppThemeVariant theme)
	{
		var token = SettingsVocabulary.Of(theme).YamlToken;

		if (!_loadedApp.TryGetValue(AppSettingsLoader.ThemeKey, out var loaded) || loaded == token)
		{
			return;
		}

		if (SelectedTheme?.Token == loaded)
		{
			SelectedTheme = Match(Themes, token);
		}

		_loadedApp[AppSettingsLoader.ThemeKey] = token;
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

		if (appEdits.Keys.Any(key => key != AppSettingsLoader.ThemeKey) || connectionEdits.Count > 0)
		{
			IsRestartPending = true;
		}

		RestartNotice = appEdits.ContainsKey(AppSettingsLoader.ThemeKey)
			? Resources.SettingsRestartNoticeThemeApplied
			: Resources.SettingsRestartNotice;
	}

	private string FirstBrokenRule()
	{
		if (_sectionRefusal.Length > 0)
		{
			return _sectionRefusal;
		}

		if (SelectedLanguage is null)
		{
			return Resources.FormatSettingsFieldRequired(Resources.SettingsLanguageLabel);
		}

		if (SelectedTheme is null)
		{
			return Resources.FormatSettingsFieldRequired(Resources.SettingsThemeLabel);
		}

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
		return Current(AppKeys);
	}

	private Dictionary<string, string> CurrentConnection()
	{
		return Current(ConnectionKeys);
	}

	private Dictionary<string, string> Current(IReadOnlyList<EditedKey> keys)
	{
		var current = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var key in keys)
		{
			if (key.Read(this) is { } text)
			{
				current[key.Key] = text;
			}
		}

		return current;
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

	private static string RefusalOf(Result<OwnedSection> app, Result<OwnedSection> connection)
	{
		if (app.IsFailed)
		{
			return Resources.FormatSettingsSectionUnreadable(Resources.SettingsInterfaceHeader);
		}

		if (connection.IsFailed)
		{
			return Resources.FormatSettingsSectionUnreadable(Resources.SettingsConnectionHeader);
		}

		var absentKey = SettingsSave.FirstAbsentKey(app.Value, connection.Value);

		return absentKey is null ? string.Empty : Resources.FormatSettingsKeyAbsent(absentKey);
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
