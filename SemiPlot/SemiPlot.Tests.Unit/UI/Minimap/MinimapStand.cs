using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.Tests.Unit.UI.Chart;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Minimap;

namespace SemiPlot.Tests.Unit.UI.Minimap;

/// <summary>A minimap over a seven-day archive, every scheduler one TestScheduler.</summary>
internal sealed class MinimapStand : IDisposable
{
	public static readonly DateTime ExtentFirst = new(2025, 12, 25, 0, 0, 0, DateTimeKind.Utc);
	public static readonly DateTime ExtentLast = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	private readonly TrendCoordinator _coordinator;

	public MinimapStand(ILogger<MinimapViewModel>? logger = null)
	{
		Provider = new FakeDataProvider(Scheduler, TimeSpan.FromMilliseconds(10.0))
		{
			ArchiveFirstUtc = ExtentFirst,
			ArchiveLastUtc = ExtentLast
		};
		_coordinator = new TrendCoordinator(Provider, Provider.Pens, Scheduler, Scheduler, ChartTestBuilder.BatchWindow);
		Chart = ChartTestBuilder.CreateChart(Scheduler, _coordinator, Panel);
		ViewModel = new MinimapViewModel(
			_coordinator, Chart, Scheduler, Panel, logger ?? NullLogger<MinimapViewModel>.Instance);
	}

	public TestScheduler Scheduler { get; } = new();

	public FakeDataProvider Provider { get; }

	public MessagePanelViewModel Panel { get; } = new();

	public TrendChartViewModel Chart { get; }

	public MinimapViewModel ViewModel { get; }

	public ChartNavigationController Navigation => Chart.Navigation;

	public PenHistoryEnvelope? Band => ViewModel.BandFeed.Band;

	public DateTime WindowCenter => Navigation.From + ((Navigation.To - Navigation.From) / 2.0);

	public void Dispose()
	{
		ViewModel.Dispose();
		Chart.Dispose();
		_coordinator.Dispose();
		Panel.Dispose();
	}

	public static async Task<MinimapStand> WithABandAsync()
	{
		var stand = new MinimapStand();
		stand.ShowPens();
		await stand.LoadExtentAsync();
		stand.LandBandRead();
		stand.Band.Should().NotBeNull();

		return stand;
	}

	/// <summary>Runs the extent read through the UI scheduler's apply step.</summary>
	public async Task<Result<ArchiveExtent>> LoadExtentAsync()
	{
		var load = ViewModel.LoadExtentAsync();
		Scheduler.AdvanceBy(1);

		return await load;
	}

	public void ShowPens()
	{
		Chart.ApplyCatalogue(Provider.Pens);
	}

	/// <summary>A gated read resumes on the dispatcher; the UI scheduler applies one answer per tick.</summary>
	public void LandBandRead()
	{
		Dispatcher.UIThread.RunJobs();
		Scheduler.AdvanceBy(1);
	}

	public void AdvanceToNextBandRead()
	{
		Scheduler.AdvanceBy(MinimapBandFeed.NextReadDelay(ViewModel.ExtentLast - ViewModel.ExtentFirst).Ticks);
		LandBandRead();
	}
}
