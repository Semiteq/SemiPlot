using System.Linq;
using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Legend;

using Xunit;

namespace SemiPlot.Tests.UI.Legend;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendLegendViewModelTests
{
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33);
	private static readonly DateTime _from = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _to = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

	[AvaloniaFact]
	public void Groups_AreKeyedByPenGroup()
	{
		var (chart, _) = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", "Heaters", "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", "Heaters", "#00ff00"));
		chart.AddPen(new Pen(3, "Pen 3", "Pressures", "#0000ff"));

		using var legend = new TrendLegendViewModel(chart);

		legend.Groups.Should().HaveCount(2);
		legend.Groups.Single(group => group.Name == "Heaters").Rows.Should().HaveCount(2);
		legend.Groups.Single(group => group.Name == "Pressures").Rows.Should().HaveCount(1);
	}

	[AvaloniaFact]
	public void TogglingRowCheckbox_FlipsChartPenVisibility()
	{
		var (chart, _) = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", "Heaters", "#ff0000"));
		using var legend = new TrendLegendViewModel(chart);
		var row = SingleRow(legend, 1);

		row.IsVisible = false;

		chart.FindPen(1)!.IsVisible.Should().BeFalse();

		row.IsVisible = true;

		chart.FindPen(1)!.IsVisible.Should().BeTrue();
	}

	[AvaloniaFact]
	public void RowVisibility_MirrorsChartDrivenVisibilityChange()
	{
		var (chart, _) = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", "Heaters", "#ff0000"));
		using var legend = new TrendLegendViewModel(chart);
		var row = SingleRow(legend, 1);

		chart.SetPenVisibility(1, false);

		row.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void SelectingRow_SetsTheActivePenOnChart()
	{
		var (chart, _) = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", "Heaters", "#ff0000"));
		chart.AddPen(new Pen(2, "Pen 2", "Pressures", "#00ff00"));
		using var legend = new TrendLegendViewModel(chart);
		var secondRow = SingleRow(legend, 2);

		secondRow.Select();

		chart.ActivePenId.Should().Be(2);
		secondRow.IsActive.Should().BeTrue();
		SingleRow(legend, 1).IsActive.Should().BeFalse();
	}

	[AvaloniaFact]
	public void CursorValue_ReflectsChartCursorValues()
	{
		var (chart, coordinator) = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", "Heaters", "#ff0000"));
		using var legend = new TrendLegendViewModel(chart);
		var row = SingleRow(legend, 1);
		coordinator.RequestHistory([1], _from, _to);

		chart.MoveCursor(_from);

		row.CursorValue.Should().Be(1.0);

		chart.ClearCursor();

		row.CursorValue.Should().BeNull();
	}

	[AvaloniaFact]
	public void CurrentValue_ReflectsChartHistoryLoad()
	{
		var (chart, coordinator) = CreateChart();
		chart.AddPen(new Pen(1, "Pen 1", "Heaters", "#ff0000"));
		using var legend = new TrendLegendViewModel(chart);
		var row = SingleRow(legend, 1);

		coordinator.RequestHistory([1], _from, _to);

		row.CurrentValue.Should().Be(2.0);
	}

	private static TrendLegendRowViewModel SingleRow(TrendLegendViewModel legend, long penId)
	{
		return legend.Groups
			.SelectMany(group => group.Rows)
			.Single(row => row.Name == $"Pen {penId}");
	}

	private static (TrendChartViewModel Chart, TrendCoordinator Coordinator) CreateChart()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10));
		var coordinator = new TrendCoordinator(
			provider,
			NullLogger<TrendCoordinator>.Instance,
			scheduler,
			ImmediateScheduler.Instance,
			_batchWindow);
		var chart = new TrendChartViewModel(coordinator, ImmediateScheduler.Instance);

		return (chart, coordinator);
	}
}
