using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using ScottPlot;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Messages;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

// The Y-axis click-region range edit at the seam the view composes: ChartAxisRegion classifies the
// pixel, ChartAxisEdit seeds the untouched bound, and the result feeds back through SetAxisLimits /
// AutoscaleAxis.
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartAxisRegionEditTests
{
	private const int PlotWidth = 600;
	private const int PlotHeight = 400;
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33);
	private static readonly DateTime _from = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

	[AvaloniaFact]
	public void UpperRegionEdit_SetsMaxOnTheActivePensAxis_AsManual()
	{
		var viewModel = CreateLoadedViewModel();
		var (region, dataRect) = RenderRegion(viewModel);
		var upperPixel = dataRect.Top + 1f;

		region.IsUpperHalf(upperPixel).Should().BeTrue();
		ApplyEdit(viewModel, typedBound: 999.0, editsMax: region.IsUpperHalf(upperPixel));

		var settings = viewModel.ScaleSettings[1];
		settings.Mode.Should().Be(ScaleMode.Manual);
		settings.ManualMax.Should().Be(999.0);
	}

	[AvaloniaFact]
	public void LowerRegionEdit_SetsMinOnTheActivePensAxis_AsManual()
	{
		var viewModel = CreateLoadedViewModel();
		var (region, dataRect) = RenderRegion(viewModel);
		var lowerPixel = dataRect.Bottom - 1f;

		region.IsUpperHalf(lowerPixel).Should().BeFalse();
		ApplyEdit(viewModel, typedBound: -7.0, editsMax: region.IsUpperHalf(lowerPixel));

		var settings = viewModel.ScaleSettings[1];
		settings.Mode.Should().Be(ScaleMode.Manual);
		settings.ManualMin.Should().Be(-7.0);
	}

	[AvaloniaFact]
	public void DoubleClickOnAxisRegion_RevertsToAutoscale()
	{
		var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Manual);

		viewModel.AutoscaleAxis(viewModel.ActivePenId).Should().BeTrue();

		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Auto);
	}

	[AvaloniaFact]
	public void ApplyingAnAxisEdit_LeavesTheNavigationWindowAndDeltaCursorsUntouched()
	{
		// ChartPressRouterTests covers the press routing; this asserts the apply seam (SetAxisLimits) itself
		// has no pan or delta side effect.
		var viewModel = CreateLoadedViewModel();
		var (region, dataRect) = RenderRegion(viewModel);
		var fromBefore = viewModel.Navigation.From;
		var toBefore = viewModel.Navigation.To;

		ApplyEdit(viewModel, typedBound: 42.0, editsMax: region.IsUpperHalf(dataRect.Top + 1f));

		viewModel.Navigation.From.Should().Be(fromBefore);
		viewModel.Navigation.To.Should().Be(toBefore);
		viewModel.DeltaFirstCursor.Should().BeNull();
		viewModel.DeltaSecondCursor.Should().BeNull();
		viewModel.IsDragging.Should().BeFalse();
	}

	// The axis click editor is the one path that sets a pen's two bounds, and the axis has to carry them.
	[AvaloniaFact]
	public void AxisEditorPath_PutsBothBoundsOnTheRenderedAxis()
	{
		var viewModel = CreateLoadedViewModel();
		var (region, dataRect) = RenderRegion(viewModel);

		ApplyEdit(viewModel, typedBound: 120.0, editsMax: region.IsUpperHalf(dataRect.Top + 1f));
		ApplyEdit(viewModel, typedBound: -20.0, editsMax: region.IsUpperHalf(dataRect.Bottom - 1f));

		viewModel.Plot.RenderInMemory(PlotWidth, PlotHeight);
		var axis = viewModel.ActivePenAxis!;
		axis.Range.Min.Should().Be(-20.0);
		axis.Range.Max.Should().Be(120.0);
	}

	[AvaloniaFact]
	public void ActivePenAxis_ResolvesToTheInstanceThePenRendersAgainst()
	{
		var viewModel = CreateLoadedViewModel();
		var pen = viewModel.FindPen(1)!;

		viewModel.ActivePenAxis.Should().BeSameAs(pen.Line.Axes.YAxis);
	}

	private static void ApplyEdit(TrendChartViewModel viewModel, double typedBound, bool editsMax)
	{
		var currentRange = viewModel.ScaleRangeForPen(viewModel.ActivePenId)!.Value;
		var (min, max) = ChartAxisEdit.SeedManualLimits(typedBound, editsMax, currentRange);
		viewModel.SetAxisLimits(viewModel.ActivePenId, min, max).Should().BeTrue();
	}

	private static (ChartAxisRegion Region, PixelRect DataRect) RenderRegion(TrendChartViewModel viewModel)
	{
		viewModel.Plot.RenderInMemory(PlotWidth, PlotHeight);
		var axis = viewModel.ActivePenAxis!;
		var region = ChartAxisRegion.TryCreate(viewModel.Plot, axis);
		region.Should().NotBeNull();

		return (region, viewModel.Plot.RenderManager.LastRender.Layout.DataRect);
	}

	private static TrendChartViewModel CreateLoadedViewModel()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10));
		var coordinator = new TrendCoordinator(
			provider,
			provider.Pens,
			scheduler,
			ImmediateScheduler.Instance,
			_batchWindow);
		var viewModel = new TrendChartViewModel(
			coordinator,
			scheduler,
			ImmediateScheduler.Instance,
			new MessagePanelViewModel(),
			NullLogger<TrendChartViewModel>.Instance);
		var state = viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		state.LoadHistory(new PenHistoryEnvelope(
			1,
			[_from, _from.AddMinutes(1.0)],
			[1.0, 3.0],
			[5.0, 9.0],
			[2.0, 6.0]));

		return viewModel;
	}
}
