using System.Reactive.Concurrency;
using System.Reactive.Linq;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.Tests.Unit.UI.PenEditor;
using SemiPlot.UI;
using SemiPlot.UI.Chart;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;
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
	TestScheduler scheduler,
	StartupData data) : IDisposable
{
	public FakeDataProvider Provider { get; } = provider;

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

	// The narrowest column target puts every layer inside the model's 365-day width ceiling; at the widest
	// one the hour ceiling alone is 512 days and the Day layer is unreachable.
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
		IPenCatalogueEditor? penCatalogueEditor = null,
		MessagePanelViewModel? panel = null)
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromSeconds(1), pens);
		var services = new ServiceCollection()
			.AddSingleton<IScheduler>(scheduler)
			.AddSingleton<IDataProvider>(provider)
			.AddSingleton(penCatalogueEditor ?? new FakePenCatalogueEditor())
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

		var startupData = new StartupData(container, provider.Pens, startupExtent);

		return new ArchiveStand(container, provider, scheduler, startupData);
	}

	/// <summary>
	/// A window built through <see cref="TrendWindow.Build"/> over one virtual clock and one provider; the
	/// catalogue read loop has entered its first wait.
	/// </summary>
	public static WindowStand NewWindowStand(
		IReadOnlyList<Pen>? pens = null,
		IPenCatalogueEditor? penCatalogueEditor = null,
		string? configDirectory = null,
		MessagePanelViewModel? panel = null)
	{
		var archive = NewArchiveStand(pens, penCatalogueEditor, panel);
		var window = TrendWindow.Build(archive.Data, configDirectory ?? AppContext.BaseDirectory, archive.Scheduler);

		archive.Scheduler.AdvanceBy(1);

		return new WindowStand(window, archive);
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
