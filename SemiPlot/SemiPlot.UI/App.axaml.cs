using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using ReactiveUI.Avalonia;

using Semi.Avalonia;

using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;
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

	private TrendWindow? _trendWindow;

	private ArchiveFailureView? _startupFailure;

	private MessagePanelViewModel? _messagePanel;

	private string? _configDirectory;

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.MainWindow = CreateMainWindow();
		}

		base.OnFrameworkInitializationCompleted();
	}

	internal Window CreateMainWindow()
	{
		if (_startupFailure is not null)
		{
			var startupFailure = new StartupFailureViewModel(
				_startupFailure, _configDirectory, new SerilogLoggerFactory());
			var startupFailureWindow = new StartupFailureWindow { DataContext = startupFailure };

			_messagePanel = startupFailure.MessagePanel;
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
		// rather than after App.Run returns.
		mainWindow.Closed += (_, _) => trendWindow.Dispose();

		return mainWindow;
	}

	/// <summary>
	/// <paramref name="settings"/> is null only when the settings load itself failed; that window renders
	/// on the variant <c>App.axaml</c> declares, and every other window follows the configured one.
	/// <paramref name="configDirectory"/> is null when the startup failure leaves the settings window nothing to fix.
	/// </summary>
	public static void Run(AppSettings? settings, Result<StartupData> startup, string? configDirectory)
	{
		BuildAvaloniaApp()
			.AfterSetup(builder => Configure((App)builder.Instance!, settings, startup, configDirectory))
			.StartWithClassicDesktopLifetime([]);
	}

	internal static void Configure(
		App app,
		AppSettings? settings,
		Result<StartupData> startup,
		string? configDirectory)
	{
		// Above the failure return, so an archive failure still renders on the configured variant.
		if (settings is not null)
		{
			app.RequestedThemeVariant = VariantFor(settings.Theme);
		}

		// A settings failure has no configured locale, and the bootstrap one is what its window is
		// already read in.
		SemiTheme.OverrideLocaleResources(
			app, SemiLocaleFor(settings?.Locale ?? StartupSequence.BootstrapLocale));

		if (startup.IsFailed)
		{
			app._startupFailure = ArchiveFailureMapper.Map(startup.Errors[0]);
			app._configDirectory = configDirectory;

			return;
		}

		app._messagePanel = startup.Value.ServiceProvider.GetRequiredService<MessagePanelViewModel>();
		app._trendWindow = TrendWindow.Build(
			startup.Value,
			configDirectory
				?? throw new InvalidOperationException("A started window needs its configuration directory."),
			AvaloniaScheduler.Instance);
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
