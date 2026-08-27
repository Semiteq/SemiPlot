using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Tests.UI.Bridge;
using SemiPlot.UI;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.UI.Startup;

/// <summary>
/// The last leg of the health warning's route: what <see cref="StartupProbe"/> carried out of the archive
/// becomes the main window's health row, written by <c>App.InitializeServices</c> and by nothing else.
/// These drive that body rather than a look-alike, so they cover the wiring the running application
/// executes — which is why they are <c>[AvaloniaFact]</c> and <c>StartupProbeTests</c> is not.
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class ArchiveHealthBannerStartupTests
{
	private static readonly ArchiveDefaultPartitionNotEmptyError _defaultPartitionHoldsRows =
		new("bench", 5432, "semiplot_dev", "public.tpdefault");

	// The whole point of the warning channel: a fault the operator must act on reaches the banner while the
	// chart is built and drawn exactly as it would be over a healthy archive.
	[AvaloniaFact]
	public async Task AHealthWarning_ReachesTheBannerAndStartsTheChartAnyway()
	{
		var scheduler = new TestScheduler();
		using var container = BuildContainer(scheduler);

		var probe = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);
		probe.IsSuccess.Should().BeTrue();

		App.InitializeServices(probe.Value with { HealthWarnings = new IError[] { _defaultPartitionHoldsRows } });

		var mainWindowViewModel = container.GetRequiredService<MainWindowViewModel>();

		// The remedy, not only the state: the rows were written by the SCADA, so an operator told just
		// "the default partition holds rows" has nowhere to go.
		mainWindowViewModel.ArchiveHealthMessage.Should()
			.Contain("public.tpdefault")
			.And.Contain("the remedy is on that side: find out why the daily partition was missing at "
				+ "write time");
		mainWindowViewModel.ArchiveHealthMessage.Should().NotBe(_defaultPartitionHoldsRows.Message);
		mainWindowViewModel.HasArchiveHealthMessage.Should().BeTrue();
		mainWindowViewModel.ChartViewModel.Should().NotBeNull();
	}

	[AvaloniaFact]
	public async Task NoHealthWarning_LeavesTheRowHidden()
	{
		var scheduler = new TestScheduler();
		using var container = BuildContainer(scheduler);

		var probe = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		App.InitializeServices(probe.Value);

		var mainWindowViewModel = container.GetRequiredService<MainWindowViewModel>();

		mainWindowViewModel.ArchiveHealthMessage.Should().BeNull();
		mainWindowViewModel.HasArchiveHealthMessage.Should().BeFalse();
	}

	// Two warnings are one row, not two: the row states facts the operator acts on, and each warning already
	// carries a whole sentence naming its own fault.
	[AvaloniaFact]
	public async Task SeveralHealthWarnings_AreJoinedIntoTheOneRow()
	{
		var scheduler = new TestScheduler();
		using var container = BuildContainer(scheduler);
		var second = new ArchiveDefaultPartitionNotEmptyError("bench", 5432, "semiplot_other", "public.tpdefault");

		var probe = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		App.InitializeServices(
			probe.Value with { HealthWarnings = new IError[] { _defaultPartitionHoldsRows, second } });

		var mainWindowViewModel = container.GetRequiredService<MainWindowViewModel>();

		mainWindowViewModel.ArchiveHealthMessage.Should()
			.Contain(StartupFailureMapper.Describe(_defaultPartitionHoldsRows))
			.And.Contain(StartupFailureMapper.Describe(second));
	}

	// A TestScheduler, not CurrentThreadScheduler: InitializeServices calls TrendCoordinator.Start, and a
	// recurring realtime subscription on the current thread's trampoline never returns control.
	private static ServiceProvider BuildContainer(TestScheduler scheduler)
	{
		var services =
			new ServiceCollection()
				.AddSingleton<IScheduler>(scheduler)
				.AddSingleton<IDataProvider>(new FakeDataProvider(scheduler, TimeSpan.FromSeconds(1)))
				.AddUi();

		services.AddLogging();

		return services.BuildServiceProvider();
	}
}
