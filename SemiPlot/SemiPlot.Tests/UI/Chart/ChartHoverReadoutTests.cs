using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartHoverReadoutTests
{
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33);
	private static readonly DateTime _cursor = new(2026, 6, 15, 8, 1, 0, DateTimeKind.Utc);

	[AvaloniaFact]
	public void Update_OnHover_ShowsEveryVisiblePenValueAtCursorPlusTimestamp()
	{
		var viewModel = CreateViewModel();
		var pens = AddTwoPens(viewModel);
		var readout = new ChartHoverReadout(viewModel.Plot);
		var values = new Dictionary<long, double?> { [1] = 2.0, [2] = 20.0 };

		readout.Update(_cursor, values, pens, suppress: false);

		readout.IsVisible.Should().BeTrue();
		readout.Content.Should().Contain("Pen 1: 2").And.Contain("Pen 2: 20");
		readout.Content.Should().Contain(LocalTimestamp(_cursor));
	}

	[AvaloniaFact]
	public void BuildContent_PenWithGapAtCursor_RendersDashForThatPen()
	{
		var viewModel = CreateViewModel();
		var pens = AddTwoPens(viewModel);
		// A gap (or out-of-range X) surfaces as a null value in CursorValues for that pen.
		var values = new Dictionary<long, double?> { [1] = 2.0, [2] = null };

		var content = ChartHoverReadout.BuildContent(_cursor, values, pens);

		content.Should().Contain("Pen 1: 2");
		content.Should().Contain("Pen 2: —");
	}

	[AvaloniaFact]
	public void BuildContent_SkipsHiddenPens()
	{
		var viewModel = CreateViewModel();
		var pens = AddTwoPens(viewModel);
		viewModel.SetPenVisibility(2, false);
		var values = new Dictionary<long, double?> { [1] = 2.0, [2] = 20.0 };

		var content = ChartHoverReadout.BuildContent(_cursor, values, pens);

		content.Should().Contain("Pen 1");
		content.Should().NotContain("Pen 2");
	}

	[AvaloniaFact]
	public void Update_WhileSuppressed_HidesTheReadout()
	{
		var viewModel = CreateViewModel();
		var pens = AddTwoPens(viewModel);
		var readout = new ChartHoverReadout(viewModel.Plot);
		var values = new Dictionary<long, double?> { [1] = 2.0, [2] = 20.0 };

		readout.Update(_cursor, values, pens, suppress: false);
		readout.IsVisible.Should().BeTrue();

		readout.Update(_cursor, values, pens, suppress: true);

		readout.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void Update_WithNoCursor_HidesTheReadout()
	{
		var viewModel = CreateViewModel();
		var pens = AddTwoPens(viewModel);
		var readout = new ChartHoverReadout(viewModel.Plot);
		var values = new Dictionary<long, double?> { [1] = 2.0, [2] = 20.0 };

		readout.Update(cursorTime: null, values, pens, suppress: false);

		readout.IsVisible.Should().BeFalse();
	}

	private static string LocalTimestamp(DateTime cursorUtc)
	{
		return cursorUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
	}

	private static IReadOnlyCollection<TrendPenState> AddTwoPens(TrendChartViewModel viewModel)
	{
		viewModel.AddPen(new Pen(1, "Pen 1", "Group A", "#ff0000"));
		viewModel.AddPen(new Pen(2, "Pen 2", "Group B", "#00ff00"));
		return viewModel.Pens;
	}

	private static TrendChartViewModel CreateViewModel()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10));
		var coordinator = new TrendCoordinator(
			provider,
			NullLogger<TrendCoordinator>.Instance,
			scheduler,
			ImmediateScheduler.Instance,
			_batchWindow);

		return new TrendChartViewModel(coordinator, scheduler, ImmediateScheduler.Instance);
	}
}
