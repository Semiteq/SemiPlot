using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Toolbar;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Toolbar;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendToolbarViewModelTests
{
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33);

	[AvaloniaFact]
	public void SetLimitsCommand_SwitchesActivePenAxisToManual()
	{
		var (chart, toolbar) = CreateToolbar();
		chart.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		toolbar.ManualMin = 5.0;
		toolbar.ManualMax = 50.0;

		toolbar.SetActiveAxisLimitsCommand.Execute().Subscribe();

		var settings = chart.ScaleSettings[1];
		settings.Mode.Should().Be(ScaleMode.Manual);
		settings.ManualMin.Should().Be(5.0);
		settings.ManualMax.Should().Be(50.0);
	}

	[AvaloniaFact]
	public void AutoscaleCommand_RevertsActivePenAxisToAuto()
	{
		var (chart, toolbar) = CreateToolbar();
		chart.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		chart.SetAxisLimits(1, 5.0, 50.0);

		toolbar.AutoscaleActiveAxisCommand.Execute().Subscribe();

		chart.ScaleSettings[1].Mode.Should().Be(ScaleMode.Auto);
	}

	[AvaloniaFact]
	public void ToggleStickyCommand_FlipsStickyOnNavigation()
	{
		var (chart, toolbar) = CreateToolbar();
		toolbar.IsSticky.Should().BeTrue();

		toolbar.ToggleStickyCommand.Execute().Subscribe();

		toolbar.IsSticky.Should().BeFalse();
		chart.Navigation.IsSticky.Should().BeFalse();
	}

	[AvaloniaFact]
	public void JumpToNowCommand_ReattachesStickyOnNavigation()
	{
		var (chart, toolbar) = CreateToolbar();
		toolbar.ToggleStickyCommand.Execute().Subscribe();
		toolbar.IsSticky.Should().BeFalse();

		toolbar.JumpToNowCommand.Execute().Subscribe();

		toolbar.IsSticky.Should().BeTrue();
		chart.Navigation.IsSticky.Should().BeTrue();
	}

	[AvaloniaFact]
	public void PanPastLiveEdge_AutoDetachesStickyOnToolbar_JumpToNowReattaches()
	{
		var (chart, toolbar) = CreateToolbar();
		var liveEdge = DateTime.UtcNow;
		chart.Navigation.TrackDataExtents(liveEdge - TimeSpan.FromDays(7.0), liveEdge);
		toolbar.IsSticky.Should().BeTrue();

		chart.Navigation.PanBy(TimeSpan.FromHours(-2.0));

		toolbar.IsSticky.Should().BeFalse();
		chart.Navigation.IsSticky.Should().BeFalse();

		toolbar.JumpToNowCommand.Execute().Subscribe();

		toolbar.IsSticky.Should().BeTrue();
		chart.Navigation.IsSticky.Should().BeTrue();
	}

	[AvaloniaFact]
	public void ToggleDeltaModeCommand_EntersAndExitsDeltaModeOnChart()
	{
		var (chart, toolbar) = CreateToolbar();
		toolbar.IsDeltaModeEnabled.Should().BeFalse();

		toolbar.ToggleDeltaModeCommand.Execute().Subscribe();

		toolbar.IsDeltaModeEnabled.Should().BeTrue();
		chart.IsDeltaModeEnabled.Should().BeTrue();
		chart.ActiveLeftButtonTool.Should().Be(LeftButtonTool.DeltaPlacement);

		toolbar.ToggleDeltaModeCommand.Execute().Subscribe();

		toolbar.IsDeltaModeEnabled.Should().BeFalse();
		chart.IsDeltaModeEnabled.Should().BeFalse();
		chart.ActiveLeftButtonTool.Should().Be(LeftButtonTool.Pan);
	}

	[AvaloniaFact]
	public void ActiveLayer_ReflectsTheLayerAutoSelectedFromZoomWidth()
	{
		var (chart, toolbar) = CreateToolbar();
		toolbar.ActiveLayer.Should().Be(AggregationLayer.Raw);

		chart.Navigation.ZoomAt(48.0, chart.Navigation.To);

		toolbar.ActiveLayer.Should().Be(chart.Navigation.ActiveLayer);
		toolbar.ActiveLayer.Should().NotBe(AggregationLayer.Raw);
	}

	[AvaloniaFact]
	public void AutoscaleCommand_OnEmptyChart_DoesNotThrow()
	{
		var (_, toolbar) = CreateToolbar();

		var act = () => toolbar.AutoscaleActiveAxisCommand.Execute().Subscribe();

		act.Should().NotThrow();
	}

	[AvaloniaFact]
	public void SetLimitsCommand_OnEmptyChart_DoesNotThrow()
	{
		var (_, toolbar) = CreateToolbar();
		toolbar.ManualMin = 1.0;
		toolbar.ManualMax = 2.0;

		var act = () => toolbar.SetActiveAxisLimitsCommand.Execute().Subscribe();

		act.Should().NotThrow();
	}

	[AvaloniaFact]
	public void Dispose_UnsubscribesFromNavigation()
	{
		var (chart, toolbar) = CreateToolbar();

		toolbar.Dispose();
		chart.Navigation.ZoomAt(48.0, chart.Navigation.To);

		toolbar.ActiveLayer.Should().Be(AggregationLayer.Raw);
	}

	private static (TrendChartViewModel Chart, TrendToolbarViewModel Toolbar) CreateToolbar()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10));
		var coordinator = new TrendCoordinator(
			provider,
			provider.Pens,
			scheduler,
			ImmediateScheduler.Instance,
			_batchWindow);
		var chart = new TrendChartViewModel(
			coordinator, scheduler, ImmediateScheduler.Instance, NullLogger<TrendChartViewModel>.Instance);
		var toolbar = new TrendToolbarViewModel(chart);

		return (chart, toolbar);
	}
}
