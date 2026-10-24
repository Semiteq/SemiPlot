using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.ChartTestBuilder;
using static SemiPlot.Tests.Unit.UI.Chart.ChartViewTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartMarkerReadingsTests
{
	private const double FirstRowValue = 2.0;
	private const double HeldValue = 6.0;
	private const double SecondPenValue = 42.0;

	private static readonly DateTime _windowFrom = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _windowTo = _windowFrom.AddHours(2);
	private static readonly DateTime _heldLastRow = From.AddMinutes(1.0);
	private static readonly DateTime _insideTheHeldSpan = From.AddMinutes(3.0);

	private readonly ChartMarkerReadings _readings = new();
	private readonly TrendPenState _pen = new(FakeDataProvider.FirstPen, new EnvelopeLine());

	[Fact]
	public void Update_ReadsThePenAtEveryMarkerInsideTheWindowThatHasAValue()
	{
		var withValue = new StoredMarker(1, _windowFrom.AddMinutes(30.0), "1");
		var inAGap = new StoredMarker(2, _windowFrom.AddMinutes(60.0), "2");
		var outside = new StoredMarker(3, _windowTo.AddMinutes(1.0), "3");
		var values = new Dictionary<DateTime, double?>
		{
			[withValue.TimeUtc] = 12.5,
			[inAGap.TimeUtc] = null,
			[outside.TimeUtc] = 40.0
		};

		_readings.Update([withValue, inAGap, outside], _pen, time => values[time], _windowFrom, _windowTo);

		_readings.SnapshotReadings().Should().Equal(new MarkerReading(
			withValue.Id,
			LocalTimeAxis.ToAxis(withValue.TimeUtc),
			12.5,
			PenValueFormat.Format(12.5, _pen.Pen.Format)));
	}

	[Fact]
	public void Update_WithNoActivePen_ShowsNoReading()
	{
		var marker = new StoredMarker(1, _windowFrom.AddMinutes(30.0), "1");
		_readings.Update([marker], _pen, _ => 12.5, _windowFrom, _windowTo);

		_readings.Update([marker], null, _ => 12.5, _windowFrom, _windowTo);

		_readings.SnapshotReadings().Should().BeEmpty();
	}

	[AvaloniaFact]
	public void ActivePenReadingAt_IsTheCursorsValueOfTheActivePen_AndNullOnceThePenIsHidden()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From, To);
		var moment = From.AddMinutes(30.0);
		viewModel.MoveCursor(moment);

		var reading = viewModel.ActivePenReadingAt(moment);

		reading.Should().NotBeNull().And.Be(viewModel.CursorValues[viewModel.ActivePenId]);

		viewModel.SetPenVisibility(viewModel.ActivePenId, false);

		viewModel.ActivePenReadingAt(moment).Should().BeNull("a hidden pen draws no reading");
		viewModel.Dispose();
	}

	[AvaloniaFact]
	public void ActivePenReadingAt_AMarkerInsideTheHeldSpan_IsTheCurrentValueTheCursorReadsThere()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		using var chart = viewModel;
		var edge = From.AddMinutes(5.0);
		var held = AddHeldPen(chart);
		chart.AddPen(FakeDataProvider.SecondPen);
		provider.PushLiveSamples(
		[
			new Sample(held.Pen.PenId, From, HeldValue),
			new Sample(FakeDataProvider.SecondPen.PenId, edge, SecondPenValue)
		]);
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);
		chart.MoveCursor(_insideTheHeldSpan);

		var reading = chart.ActivePenReadingAt(_insideTheHeldSpan);

		chart.ActivePenId.Should().Be(held.Pen.PenId);
		reading.Should().NotBeNull().And.Be(held.CurrentValue);
		reading.Should().Be(chart.CursorValues[held.Pen.PenId], "the marker reading equals the hover value there");
	}

	[AvaloniaFact]
	public void AFrame_ReadsTheCurrentValueAtAMarkerInsideTheHeldSpan()
	{
		var channel = new FakeMarkerChannel();
		var scheduler = new TestScheduler();
		var (viewModel, _, _, provider) = CreateViewModel(
			scheduler: scheduler,
			uiScheduler: scheduler,
			markerChannel: channel);
		using var chart = viewModel;
		var held = AddHeldPen(chart);
		chart.AddPen(FakeDataProvider.SecondPen);
		chart.Navigation.TrackDataExtents(From, To);
		provider.PushLiveSamples([new Sample(held.Pen.PenId, From, HeldValue)]);
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);
		using var shown = ShowChart(chart);
		var marker = new StoredMarker(1, _insideTheHeldSpan, "1");

		channel.Push(marker);
		DriveOneFrame();

		held.Line.ReadHeldSpan().Should().NotBeNull();
		shown.View.MarkerReadings!.SnapshotReadings().Should().ContainSingle(
			"the frame reads the held span under the readings lock, then the line's")
			.Which.Value.Should().Be(held.CurrentValue!.Value);
	}

	[AvaloniaFact]
	public void AFrame_DrawsTheActivePensReadingAtAMarkerInTheWindow()
	{
		var channel = new FakeMarkerChannel();
		var scheduler = new TestScheduler();
		var (viewModel, _, _, _) = CreateViewModel(scheduler: scheduler, uiScheduler: scheduler, markerChannel: channel);
		viewModel.AddPen(FakeDataProvider.FirstPen);
		LoadInitialHistory(viewModel, scheduler, From, To);
		using var shown = ShowChart(viewModel);
		var marker = new StoredMarker(1, viewModel.Navigation.From.AddMinutes(1.0), "1");

		channel.Push(marker);
		DriveOneFrame();

		shown.View.MarkerReadings!.SnapshotReadings().Should().ContainSingle()
			.Which.Value.Should().Be(viewModel.ActivePenReadingAt(marker.TimeUtc)!.Value);
		var render = () => viewModel.Plot.RenderInMemory(900, 400);
		render.Should().NotThrow("the dot and the number draw through the pen's own axes");
		viewModel.Dispose();
	}

	// The pen's last archived row holds HeldValue, so a live value at its first row is rejected and confirms it.
	private static TrendPenState AddHeldPen(TrendChartViewModel chart)
	{
		var held = chart.AddPen(FakeDataProvider.FirstPen);
		held.LoadHistory(
			new PenHistoryEnvelope(
				held.Pen.PenId,
				[From, _heldLastRow],
				[FirstRowValue, HeldValue],
				[FirstRowValue, HeldValue],
				[FirstRowValue, HeldValue]),
			_heldLastRow);

		return held;
	}
}
