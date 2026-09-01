using System.Reactive.Concurrency;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using ReactiveUI.Avalonia;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Minimap;

using Xunit;

using Point = Avalonia.Point;

namespace SemiPlot.Tests.UI.Minimap;

// The minimap's half of the input guard. MinimapViewModelTests drive NavigateToFraction directly, so the
// path from a pointer position to that call — hit testing on StripCanvas, pointer capture, the drag flag
// and the pixel-to-fraction division against the canvas bounds — is exercised by nothing else.
//
// Input comes from Avalonia.Headless 11.3.8 as Avalonia.Headless.HeadlessWindowExtensions: MouseDown,
// MouseMove and MouseUp on the TopLevel, whose points are window-client coordinates. Every position is
// therefore translated out of StripCanvas's own space, which is the space MinimapView measures against.
//
// The assertion is on the chart's navigation window, because moving it is the minimap's whole job. The
// controller under test is the one TrendChartViewModel owns, wired the way App.axaml.cs wires it: the
// minimap gets chartViewModel.Navigation, not a controller of its own.
[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class MinimapPointerInputTests
{
	private const int WindowWidth = 900;
	private const int WindowHeight = 36;
	private const double PressFraction = 0.30;
	private const double DragFraction = 0.60;
	private const double HoverFraction = 0.80;
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33.0);
	private static readonly TimeSpan _timeTolerance = TimeSpan.FromSeconds(1.0);
	private static readonly DateTime _extentFirst = new(2025, 12, 25, 0, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _extentLast = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	[AvaloniaFact]
	public async Task PressThenDrag_MovesTheChartWindowToEachPointerFraction()
	{
		var scheduler = new TestScheduler();
		var coordinator = CreateCoordinator(scheduler);
		using var chartViewModel = CreateChartViewModel(coordinator, scheduler);
		var navigation = chartViewModel.Navigation;
		var (window, stripCanvas) = await ShowMinimapAsync(coordinator, navigation);
		var pressAt = StripPointAt(stripCanvas, PressFraction);
		var dragTo = StripPointAt(stripCanvas, DragFraction);
		var widthBefore = navigation.To - navigation.From;

		window.MouseDown(ToWindow(stripCanvas, window, pressAt), MouseButton.Left);

		WindowCenter(navigation).Should().BeCloseTo(
			ExpectedCenter(stripCanvas, pressAt),
			_timeTolerance,
			"the press must reach the strip and recenter the chart there");

		window.MouseMove(ToWindow(stripCanvas, window, dragTo));
		window.MouseUp(ToWindow(stripCanvas, window, dragTo), MouseButton.Left);

		WindowCenter(navigation).Should().BeCloseTo(
			ExpectedCenter(stripCanvas, dragTo),
			_timeTolerance,
			"a move while the drag holds must keep recentering the chart");
		(navigation.To - navigation.From).Should().Be(
			widthBefore, "dragging the minimap moves the window without resizing it");
	}

	[AvaloniaFact]
	public async Task MoveAfterRelease_LeavesTheChartWindowWhereTheDragEndedIt()
	{
		var scheduler = new TestScheduler();
		var coordinator = CreateCoordinator(scheduler);
		using var chartViewModel = CreateChartViewModel(coordinator, scheduler);
		var navigation = chartViewModel.Navigation;
		var (window, stripCanvas) = await ShowMinimapAsync(coordinator, navigation);
		var pressAt = StripPointAt(stripCanvas, PressFraction);
		var dragTo = StripPointAt(stripCanvas, DragFraction);
		var hoverTo = StripPointAt(stripCanvas, HoverFraction);

		window.MouseDown(ToWindow(stripCanvas, window, pressAt), MouseButton.Left);
		window.MouseMove(ToWindow(stripCanvas, window, dragTo));
		window.MouseUp(ToWindow(stripCanvas, window, dragTo), MouseButton.Left);
		var fromAfterRelease = navigation.From;
		var movesAfterRelease = MovesReachingStrip(stripCanvas);

		window.MouseMove(ToWindow(stripCanvas, window, hoverTo));

		movesAfterRelease.Should().ContainSingle(
			"a layer that stopped delivering moves would pass the check below without routing anything")
			.Which.X.Should().BeApproximately(
				hoverTo.X, 1.0, "the delivered move must be the one aimed at the hover position");
		navigation.From.Should().Be(
			fromAfterRelease, "the release ends the drag, so a later move is a hover and navigates nothing");
		ExpectedCenter(stripCanvas, hoverTo).Should().NotBeCloseTo(
			WindowCenter(navigation),
			_timeTolerance,
			"the hover position must differ from the drag's, or the assertion above proves nothing");
	}

	private static TrendCoordinator CreateCoordinator(TestScheduler scheduler)
	{
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10.0))
		{
			ArchiveFirstUtc = _extentFirst,
			ArchiveLastUtc = _extentLast
		};

		return new TrendCoordinator(
			provider,
			provider.Pens,
			scheduler,
			ImmediateScheduler.Instance,
			_batchWindow);
	}

	// The chart view model is here for its navigation controller alone, which is the instance the minimap
	// receives in production. Its UI scheduler is AvaloniaScheduler for the reason ChartPointerInputTests
	// records: TrendChartViewModel samples its redraw stream on it, and ImmediateScheduler cannot defer, so
	// it blocks there. The 33 ms timer that scheduler starts is why each test disposes the view model.
	private static TrendChartViewModel CreateChartViewModel(TrendCoordinator coordinator, TestScheduler scheduler)
	{
		return new TrendChartViewModel(
			coordinator, scheduler, AvaloniaScheduler.Instance, NullLogger<TrendChartViewModel>.Instance);
	}

	private static async Task<(Window Window, Canvas StripCanvas)> ShowMinimapAsync(
		TrendCoordinator coordinator, ChartNavigationController navigation)
	{
		var viewModel = new MinimapViewModel(
			coordinator,
			navigation,
			ImmediateScheduler.Instance,
			NullLogger<MinimapViewModel>.Instance);
		await viewModel.LoadExtentAsync();
		viewModel.HasExtent.Should().BeTrue("without an extent the strip ignores every pointer position");

		// Seeds the window to [last - width, last], so both drag targets sit inside the extent and neither
		// pan clamps at an edge.
		navigation.TrackDataExtents(_extentFirst, _extentLast);

		var view = new MinimapView
		{
			DataContext = viewModel
		};
		var window = new Window
		{
			Width = WindowWidth,
			Height = WindowHeight,
			Content = view
		};

		window.Show();
		Dispatcher.UIThread.RunJobs();

		var stripCanvas = view.GetVisualDescendants()
			.OfType<Canvas>()
			.Single(canvas => canvas.Name == "StripCanvas");
		stripCanvas.Bounds.Width.Should().BeGreaterThan(
			0.0, "the strip divides by its own width, so a zero-width canvas navigates nowhere");

		return (window, stripCanvas);
	}

	// The witness that a move was routed at all: the strip's own handler navigates nothing once the drag is
	// over, so an unchanged navigation window alone cannot tell a delivered hover from a swallowed event.
	private static IReadOnlyList<Point> MovesReachingStrip(Canvas stripCanvas)
	{
		var moves = new List<Point>();

		stripCanvas.AddHandler(
			InputElement.PointerMovedEvent,
			(_, eventArgs) => moves.Add(eventArgs.GetPosition(stripCanvas)),
			RoutingStrategies.Bubble,
			handledEventsToo: true);

		return moves;
	}

	// Whole pixels, so the fraction the view computes back is exactly the one the expectation uses.
	private static Point StripPointAt(Canvas stripCanvas, double fraction)
	{
		return new Point(Math.Round(fraction * stripCanvas.Bounds.Width), stripCanvas.Bounds.Height / 2.0);
	}

	private static Point ToWindow(Canvas stripCanvas, Window window, Point stripPoint)
	{
		return stripCanvas.TranslatePoint(stripPoint, window)
			?? throw new InvalidOperationException("The strip canvas is not in the window's visual tree.");
	}

	// Mirrors MinimapView.NavigateToPointer and MinimapViewModel.NavigateToFraction: the pointer's X over the
	// canvas width is the fraction, and the window's center lands on that fraction of the extent.
	private static DateTime ExpectedCenter(Canvas stripCanvas, Point stripPoint)
	{
		return MinimapGeometry.TimeAtFraction(
			_extentFirst, _extentLast, stripPoint.X / stripCanvas.Bounds.Width);
	}

	private static DateTime WindowCenter(ChartNavigationController navigation)
	{
		return navigation.From + ((navigation.To - navigation.From) / 2.0);
	}
}
