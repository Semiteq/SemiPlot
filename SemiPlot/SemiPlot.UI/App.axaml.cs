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

	private InstanceLauncher? _instanceLauncher;

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
				_startupFailure,
				_instanceLauncher,
				new SerilogLoggerFactory());
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
	}

	internal static void ConfigureFailed(App app, AppSettings? settings, IError failure, StartupOptions? options)
	{
		ApplyAppearance(app, settings);

		app._startupFailure = ArchiveFailureMapper.Map(failure);
		app._instanceLauncher = options is null ? null : new InstanceLauncher(options);
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
