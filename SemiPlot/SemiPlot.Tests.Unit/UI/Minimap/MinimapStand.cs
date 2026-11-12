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

	/// <summary>Rows 07:00-17:00 on Thursday, Friday, Monday, Tuesday and Wednesday; the weekend holds none.</summary>
	public static readonly IReadOnlyList<ArchiveDaySpan> WorkingDays =
		WorkingDaysBetween(new DateOnly(2025, 12, 25), new DateOnly(2025, 12, 31));

	private readonly TrendCoordinator _coordinator;

	public MinimapStand(ILogger<MinimapViewModel>? logger = null, TimeZoneInfo? archiveZone = null)
	{
		Provider = new FakeDataProvider(Scheduler, TimeSpan.FromMilliseconds(10.0))
		{
			ArchiveFirstUtc = ExtentFirst,
			ArchiveLastUtc = ExtentLast
		};
		_coordinator = new TrendCoordinator(Provider, Provider, Provider.Pens, Scheduler, Scheduler);
		Chart = ChartTestBuilder.CreateChart(Scheduler, _coordinator, Panel, MarkerChannel);
		ViewModel = new MinimapViewModel(
			_coordinator,
			Chart,
			Scheduler,
			archiveZone ?? TimeZoneInfo.Utc,
			Panel,
			logger ?? NullLogger<MinimapViewModel>.Instance);
		Provider.RecognizeBandReadsOf(Chart, ViewModel);
	}

	public TestScheduler Scheduler { get; } = new();

	public FakeDataProvider Provider { get; }

	public MessagePanelViewModel Panel { get; } = new();

	public FakeMarkerChannel MarkerChannel { get; } = new();

	public TrendChartViewModel Chart { get; }

	public MinimapViewModel ViewModel { get; }

	public ChartNavigationController Navigation => Chart.Navigation;

	public PenHistoryEnvelope? Band => ViewModel.BandFeed.Band;

	public IReadOnlyList<ArchiveDaySpan> Spans => ViewModel.PresenceFeed.Spans;

	public DateTime WindowCenter => Navigation.From + ((Navigation.To - Navigation.From) / 2.0);

	public TimeSpan PageWidth => ViewModel.Pager.Page.ToUtc - ViewModel.Pager.Page.FromUtc;

	/// <summary>
	/// Reads of one pen at a multiple of the band's column step over any range, filed as band or chart.
	/// </summary>
	public int BandShapedReadCount => Provider.HistoryQueries.Count(
		query => query.PenIds.Count == 1 && query.TargetColumnCount % MinimapBandFeed.ColumnStep == 0);

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
		stand.LandRead();
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

	/// <summary>
	/// A gated band or spans read resumes on the dispatcher; the UI scheduler lands one answer per tick.
	/// </summary>
	public void LandRead()
	{
		Dispatcher.UIThread.RunJobs();
		Scheduler.AdvanceBy(1);
	}

	public void AdvanceToNextBandRead()
	{
		Scheduler.AdvanceBy(MinimapBandFeed.NextReadDelay(PageWidth).Ticks);
		LandRead();
	}

	public static DateTime At(DateOnly day, int hour, int minute = 0)
	{
		return day.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc);
	}

	/// <summary>Rows 07:00-17:00 UTC on every weekday from the first day to the last, both included.</summary>
	public static IReadOnlyList<ArchiveDaySpan> WorkingDaysBetween(DateOnly firstDay, DateOnly lastDay)
	{
		var spans = new List<ArchiveDaySpan>();
		for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
		{
			if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
			{
				spans.Add(new ArchiveDaySpan(day, At(day, 7), At(day, 17)));
			}
		}

		return spans;
	}
}
