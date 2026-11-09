using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.Postgres;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.Tests.Unit.UI.Export;
using SemiPlot.Tests.Unit.UI.Markers;
using SemiPlot.Tests.Unit.UI.PenEditor;
using SemiPlot.Tests.Unit.UI.Startup;
using SemiPlot.UI;
using SemiPlot.UI.Chart;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;
using SemiPlot.UI.PenEditor;
using SemiPlot.UI.Settings;
using SemiPlot.UI.Startup;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

/// <summary>
/// What <see cref="MainWindowTestBuilder.NewWindowStand"/> builds: the window as
/// <see cref="TrendWindow.Build"/> composes it, with its provider and its clock.
/// </summary>
internal sealed class WindowStand(TrendWindow window, ArchiveStand archive) : IDisposable
{
	public MainWindowViewModel ViewModel => window.ViewModel;

	public FakeDataProvider Provider => archive.Provider;

	public FakeMarkerStore MarkerStore => archive.MarkerStore;

	public TestScheduler Scheduler => archive.Scheduler;

	public void Dispose()
	{
		window.Dispose();
		archive.Dispose();
	}
}

internal sealed class ArchiveStand(
	ServiceProvider container,
	FakeDataProvider provider,
	FakeMarkerStore markerStore,
	TestScheduler scheduler,
	StartupData data) : IDisposable
{
	public FakeDataProvider Provider { get; } = provider;

	public FakeMarkerStore MarkerStore { get; } = markerStore;

	public TestScheduler Scheduler { get; } = scheduler;

	public StartupData Data { get; } = data;

	public void Dispose()
	{
		container.Dispose();
	}
}

/// <summary>The window and its process services as a test builds them, plus the layer drive.</summary>
internal static class MainWindowTestBuilder
{
	public static AppStatusBarViewModel NewStatusBar(
		MessagePanelViewModel panel,
		IObservable<ArchiveConnectionState>? connectionStates = null,
		ChartNavigationController? navigation = null)
	{
		return new AppStatusBarViewModel(
			panel,
			connectionStates ?? Observable.Never<ArchiveConnectionState>(),
			navigation ?? NavigationAtRawLayer(),
			NullLogger<AppStatusBarViewModel>.Instance);
	}

	public static ChartNavigationController NavigationAtRawLayer()
	{
		var navigation = new ChartNavigationController();
		navigation.SetTargetColumnCount(HistoryColumnTarget.MinColumns);
		navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);

		return navigation;
	}

	/// <summary>
	/// The process services a start hands the window: one container over one virtual clock and one provider,
	/// and the startup data read from them.
	/// </summary>
	public static ArchiveStand NewArchiveStand(
		IReadOnlyList<Pen>? pens = null,
		PenCatalogue? stored = null,
		IPenCatalogueEditor? penCatalogueEditor = null,
		MessagePanelViewModel? panel = null,
		IArchiveRowReader? rowReader = null,
		ILiveSource? liveSource = null)
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromSeconds(1), pens);

		if (stored is not null)
		{
			provider.Stored = stored;
		}
		var markerStore = new FakeMarkerStore();
		var services = new ServiceCollection()
			.AddSingleton<IScheduler>(scheduler)
			.AddSingleton<IDataProvider>(provider)
			.AddSingleton(liveSource ?? provider)
			.AddSingleton(penCatalogueEditor ?? new FakePenCatalogueEditor())
			.AddSingleton(rowReader ?? new FakeArchiveRowReader())
			.AddSingleton<IMarkerStore>(markerStore)
			.AddSingleton(ConnectionSettingsFactory.Create())
			.AddUi();

		if (panel is not null)
		{
			services.AddSingleton(panel);
		}

		services.AddLogging();

		var container = services.BuildServiceProvider();
		var startupExtent = provider.Pens.Count == 0
			? ArchiveExtent.Empty
			: new ArchiveExtent(provider.ArchiveFirstUtc, provider.ArchiveLastUtc);

		var startupData = new StartupData(container, provider.Catalogue, startupExtent);

		return new ArchiveStand(container, provider, markerStore, scheduler, startupData);
	}

	/// <summary>
	/// A window built through <see cref="TrendWindow.Build"/> over one virtual clock and one provider; the
	/// catalogue read loop has made its first read.
	/// </summary>
	public static WindowStand NewWindowStand(
		IReadOnlyList<Pen>? pens = null,
		PenCatalogue? stored = null,
		IPenCatalogueEditor? penCatalogueEditor = null,
		string? configDirectory = null,
		MessagePanelViewModel? panel = null,
		Action<ProcessStartInfo>? start = null,
		IArchiveRowReader? rowReader = null)
	{
		var archive = NewArchiveStand(pens, stored, penCatalogueEditor, panel, rowReader);
		var launcher = TestLaunch.LauncherAt(configDirectory ?? AppContext.BaseDirectory, start);
		var window = TrendWindow.Build(
			archive.Data, launcher, archive.Scheduler, App.CsvDialectFor(UiLanguage.En), TimeZoneInfo.Utc);

		archive.Scheduler.AdvanceBy(1);

		return new WindowStand(window, archive);
	}

	public static MenuItem MenuItemNamed(Window window, string name)
	{
		var item = ViewProbe.Named<AppMenuBar>(window, "MenuBar").FindControl<MenuItem>(name);
		item.Should().NotBeNull("'{0}' is a named item of the menu bar", name);

		return item;
	}

	/// <summary>Opens the named menu of the window and clicks the item on the menu's popup.</summary>
	public static void ClickMenuItem(Window window, string menuName, string itemName)
	{
		HeadlessInput.Click(window, MenuItemNamed(window, menuName));
		var item = MenuItemNamed(window, itemName);
		HeadlessInput.Click(
			TopLevel.GetTopLevel(item) ?? throw new InvalidOperationException($"'{menuName}' opened no popup."),
			item);
	}

	/// <summary>A row reader whose every count runs until its token is cancelled.</summary>
	public static FakeArchiveRowReader HeldCountReader()
	{
		return new FakeArchiveRowReader
		{
			OnCount = async call =>
			{
				await Task.Delay(Timeout.Infinite, call.Token);

				return Result.Ok(0L);
			}
		};
	}

	public static async Task<SettingsDialog> OpenSettings(WindowStand stand, Window window)
	{
		await stand.ViewModel.ShowSettingsCommand.Execute();
		await HeadlessWait.Until(() => window.OwnedWindows.OfType<SettingsDialog>().Any());
		Dispatcher.UIThread.RunJobs();

		return window.OwnedWindows.OfType<SettingsDialog>().Single();
	}

	// A disposed command stops following its inputs: the test left the probe's input blank before the close.
	public static void ShouldBeDisposed(SettingsViewModel settings)
	{
		settings.Password = "secret";
		((ICommand)settings.SaveCommand).CanExecute(null).Should()
			.BeFalse("the closed window disposes its view model");
	}

	public static void ShouldBeDisposed(PenEditorViewModel penEditor)
	{
		penEditor.Groups.NewGroupName = "Gas";
		((ICommand)penEditor.Groups.CreateGroupCommand).CanExecute(null).Should()
			.BeFalse("the closed window disposes its view model");
	}

	public static void DriveToLayer(ChartNavigationController navigation, AggregationLayer layer)
	{
		for (var step = 0; step < 200 && navigation.ActiveLayer != layer; step++)
		{
			navigation.ZoomAt(navigation.ActiveLayer < layer ? 2.0 : 0.5, navigation.To);
		}

		navigation.ActiveLayer.Should().Be(layer);
	}
}
