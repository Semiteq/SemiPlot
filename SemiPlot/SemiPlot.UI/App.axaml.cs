using System.Globalization;
using System.Reactive.Concurrency;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ReactiveUI.Avalonia;

using Semi.Avalonia;

using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Settings;
using SemiPlot.UI.Startup;

using Serilog.Extensions.Logging;

namespace SemiPlot.UI;

public class App : Application
{
	// Process lifetime by design: ReactiveUI takes its exception handler once, before any window exists,
	// so the observer outlives every window and reads the shown window's panel on each exception.
	private static readonly UnhandledErrorObserver _unhandledErrors = new(
		ResolveMessagePanel,
		AvaloniaScheduler.Instance,
		new SerilogLoggerFactory().CreateLogger(nameof(UnhandledErrorObserver)));

	private static readonly ILogger _themeLogger = new SerilogLoggerFactory().CreateLogger(nameof(AppSectionWatcher));

	private TrendWindow? _trendWindow;

	private ArchiveFailureView? _startupFailure;

	private MessagePanelViewModel? _messagePanel;

	private InstanceLauncher? _instanceLauncher;

	private IDisposable? _themeWatch;

	/// <summary>Theme failures raised before the first window has a panel; null once that window has taken them.</summary>
	private IReadOnlyList<IReadOnlyList<IError>>? _themeFailuresBeforeWindow = [];

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.MainWindow = CreateMainWindow();
			desktop.Exit += (_, _) => _themeWatch?.Dispose();
		}

		base.OnFrameworkInitializationCompleted();
	}

	internal Window CreateMainWindow()
	{
		if (_startupFailure is not null)
		{
			var startupFailure = new StartupFailureViewModel(
				_startupFailure,
				_instanceLauncher,
				new SerilogLoggerFactory());
			var startupFailureWindow = new StartupFailureWindow { DataContext = startupFailure };

			_messagePanel = startupFailure.MessagePanel;
			ReportThemeFailuresBeforeWindow(_messagePanel);
			startupFailureWindow.Closed += (_, _) =>
			{
				_messagePanel = null;
				startupFailure.Dispose();
			};

			return startupFailureWindow;
		}

		if (_trendWindow is not { } trendWindow)
		{
			throw new InvalidOperationException(
				"The trend window is not built. Call Run() before starting the app.");
		}

		var mainWindow = new MainWindow.MainWindow { DataContext = trendWindow.ViewModel };

		// The dispatcher loop ends with the window, so the composition is disposed here, on the UI thread,
		// rather than after RunStarted returns.
		mainWindow.Closed += (_, _) => trendWindow.Dispose();

		return mainWindow;
	}

	public static void RunStarted(AppSettings? settings, StartupData startup, StartupOptions options)
	{
		BuildAvaloniaApp()
			.AfterSetup(builder => ConfigureStarted((App)builder.Instance!, settings, startup, options))
			.StartWithClassicDesktopLifetime([]);
	}

	/// <summary>
	/// <paramref name="settings"/> is null only when the settings load itself failed; that window renders
	/// on the variant <c>App.axaml</c> declares. <paramref name="options"/> is null when the launch keys did
	/// not parse: no copy to start.
	/// </summary>
	public static void RunFailed(AppSettings? settings, IError failure, StartupOptions? options)
	{
		BuildAvaloniaApp()
			.AfterSetup(builder => ConfigureFailed((App)builder.Instance!, settings, failure, options))
			.StartWithClassicDesktopLifetime([]);
	}

	internal static void ConfigureStarted(
		App app,
		AppSettings? settings,
		StartupData startup,
		StartupOptions options)
	{
		ApplyAppearance(app, settings);

		app._messagePanel = startup.ServiceProvider.GetRequiredService<MessagePanelViewModel>();
		app._trendWindow = TrendWindow.Build(startup, new InstanceLauncher(options), AvaloniaScheduler.Instance);
		app._themeWatch = WatchTheme(app, options.ConfigDir, settings);
	}

	internal static void ConfigureFailed(App app, AppSettings? settings, IError failure, StartupOptions? options)
	{
		ApplyAppearance(app, settings);

		app._startupFailure = ArchiveFailureMapper.Map(failure);
		app._instanceLauncher = options is null ? null : new InstanceLauncher(options);
		app._themeWatch = options is null ? null : WatchTheme(app, options.ConfigDir, settings);
	}

	// docs/architecture/overview.md#the-live-theme
	private static IDisposable? WatchTheme(App app, string configDirectory, AppSettings? settings)
	{
		var sectionDirectory = Path.Combine(configDirectory, StartupSequence.SettingsDirectoryName);

		// The start has already reported a missing folder.
		if (!Directory.Exists(sectionDirectory))
		{
			return null;
		}

		var watch = AppSectionWatcher.Watch(() => AppSectionWatcher.Open(sectionDirectory), DefaultScheduler.Instance);

		return AppSectionWatcher.ThemeChanges(
				watch,
				settings?.Theme,
				() => AppSettingsLoader.Load(sectionDirectory),
				DefaultScheduler.Instance,
				AvaloniaScheduler.Instance)
			.Subscribe(app.ApplyTheme, exception => app.ReportThemeFailure([new ExceptionalError(exception)]));
	}

	private void ApplyTheme(Result<AppThemeVariant> theme)
	{
		try
		{
			if (theme.IsFailed)
			{
				ReportThemeFailure(theme.Errors);

				return;
			}

			RequestedThemeVariant = VariantFor(theme.Value);
		}
		catch (Exception exception)
		{
			ReportThemeFailure([new ExceptionalError(exception)]);
		}
	}

	/// <summary>The shown window's panel, the first window's once it exists, or the log alone after it closed.</summary>
	private void ReportThemeFailure(IReadOnlyList<IError> errors)
	{
		if (_messagePanel is { } panel)
		{
			panel.TryReportFailure(errors, _themeLogger);

			return;
		}

		if (_themeFailuresBeforeWindow is { } held)
		{
			_themeFailuresBeforeWindow = [.. held, errors];

			return;
		}

		foreach (var error in errors)
		{
			_themeLogger.LogWarning(
				(error as IExceptionalError)?.Exception,
				"The theme reload failed with no window open: {Error}",
				error.Message);
		}
	}

	private void ReportThemeFailuresBeforeWindow(MessagePanelViewModel panel)
	{
		foreach (var errors in _themeFailuresBeforeWindow ?? [])
		{
			panel.TryReportFailure(errors, _themeLogger);
		}

		_themeFailuresBeforeWindow = null;
	}

	private static void ApplyAppearance(App app, AppSettings? settings)
	{
		if (settings is not null)
		{
			app.RequestedThemeVariant = VariantFor(settings.Theme);
		}

		// A settings failure has no configured locale, and the bootstrap one is what its window is
		// already read in.
		SemiTheme.OverrideLocaleResources(
			app, SemiLocaleFor(settings?.Locale ?? StartupSequence.BootstrapLocale));
	}

	internal static CultureInfo SemiLocaleFor(UiLanguage locale)
	{
		return SettingsVocabulary.Of(locale).SemiCulture;
	}

	internal static ThemeVariant VariantFor(AppThemeVariant theme)
	{
		return SettingsVocabulary.Of(theme).Variant;
	}

	internal static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>()
			.UseWin32()
			.UseSkia()
			// Avalonia 12: Skia no longer brings a text shaper with it. Without UseHarfBuzz the desktop
			// application fails at AppBuilder.Setup with "No text shaping system configured".
			.UseHarfBuzz()
			// docs/architecture/overview.md#where-a-failure-goes
			.UseReactiveUI(builder => builder.WithExceptionHandler(_unhandledErrors))
			.LogToTrace();
	}

	/// <summary>The panel of the window shown; with no window a failure is only logged.</summary>
	internal static MessagePanelViewModel? ResolveMessagePanel()
	{
		return (Current as App)?._messagePanel;
	}
}
