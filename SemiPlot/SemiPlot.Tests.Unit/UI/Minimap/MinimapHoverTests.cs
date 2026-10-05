using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

using AwesomeAssertions;

using Xunit;

using Point = Avalonia.Point;

namespace SemiPlot.Tests.Unit.UI.Minimap;

[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class MinimapHoverTests
{
	private const double PressFraction = 0.30;
	private const double DragFraction = 0.60;
	private const double HoverLineWidth = 1.0;
	private const double PixelTolerance = 1.0;
	private const double OffStripDistance = 6.0;
	private const double FirstLabelSearchWidth = 400.0;

	[AvaloniaFact]
	public async Task HoveringTheStrip_ShowsALineAndItsTimeAndLeavingHidesThem()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var strip = stand.StripCanvas;
		var (hoverLine, hoverTime) = HoverControls(stand);
		var pointer = stand.StripPointAt(DragFraction);

		stand.MovePointer(pointer);

		hoverLine.IsVisible.Should().BeTrue("a pointer over the strip shows its line");
		var line = stand.AreaInView(hoverLine);
		line.Left.Should().BeApproximately(pointer.X, PixelTolerance, "the line sits under the pointer");
		line.Width.Should().Be(HoverLineWidth);
		line.Height.Should().Be(strip.Bounds.Height, "the line crosses the whole strip");
		stand.Model.ViewModel.HoverFraction.Should().Be(pointer.X / strip.Bounds.Width);
		hoverTime.IsVisible.Should().BeTrue("a pointer over the strip shows its time");
		hoverTime.Text.Should().Be(stand.Model.ViewModel.HoverLabel);
		var time = stand.AreaInView(hoverTime);
		time.Top.Should().BeGreaterThanOrEqualTo(
			stand.AreaInView(strip).Bottom, "the time sits in the row under the strip");
		time.Center.X.Should().BeApproximately(pointer.X, PixelTolerance, "the time is centred under the line");
		EndLabelsShown(stand).Should().Equal([true, true], "a time in the middle covers neither end label");

		stand.MovePointer(new Point(pointer.X, strip.Bounds.Height + OffStripDistance));

		hoverLine.IsVisible.Should().BeFalse("leaving the strip hides the line");
		hoverTime.IsVisible.Should().BeFalse("leaving the strip hides the time");
	}

	[AvaloniaTheory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task AHoverAtAnEnd_KeepsItsTimeInTheRowAndHidesTheEndLabelItCovers(bool atLeftEnd)
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var strip = stand.StripCanvas;
		var (_, hoverTime) = HoverControls(stand);
		var pointerX = atLeftEnd ? 0.0 : strip.Bounds.Width - 1.0;
		var row = stand.AreaInView(stand.Named<Panel>("LabelRow"));

		stand.MovePointer(new Point(pointerX, strip.Bounds.Height / 2.0));

		var time = stand.AreaInView(hoverTime);
		time.Left.Should().BeGreaterThanOrEqualTo(row.Left, "the time never leaves the row");
		time.Right.Should().BeLessThanOrEqualTo(row.Right, "the time never leaves the row");
		EndLabelsShown(stand).Should().Equal(
			[!atLeftEnd, atLeftEnd], "the time hides the end label it covers and only that one");
		var keptLabel = stand.AreaInView(stand.Named<TextBlock>(atLeftEnd ? "ExtentLastLabel" : "ExtentFirstLabel"));
		time.Intersects(keptLabel).Should().BeFalse("the end label left shown must not be covered");

		stand.MovePointer(new Point(pointerX, strip.Bounds.Height + OffStripDistance));

		EndLabelsShown(stand).Should().Equal([true, true], "leaving the strip shows both end labels again");
	}

	[AvaloniaFact]
	public async Task AHoverAtTheRightEdge_KeepsTheLineInsideTheStrip()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var strip = stand.StripCanvas;
		var (hoverLine, _) = HoverControls(stand);

		stand.MovePointer(new Point(strip.Bounds.Width - (HoverLineWidth / 2.0), strip.Bounds.Height / 2.0));

		hoverLine.IsVisible.Should().BeTrue();
		stand.AreaInView(hoverLine).Right.Should().BeLessThanOrEqualTo(
			stand.AreaInView(strip).Right, "the line stops at the strip's right edge");
	}

	[AvaloniaFact]
	public async Task AResizeDuringAHover_KeepsTheLineAtItsFraction()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var strip = stand.StripCanvas;
		var (hoverLine, hoverTime) = HoverControls(stand);
		stand.MovePointer(stand.StripPointAt(DragFraction));
		var fraction = stand.Model.ViewModel.HoverFraction ?? throw new InvalidOperationException("No hover shows.");

		stand.Window.Width = 600;
		Dispatcher.UIThread.RunJobs();

		strip.Bounds.Width.Should().Be(600, "the strip follows the window");
		var lineX = fraction * strip.Bounds.Width;
		stand.AreaInView(hoverLine).Left.Should().BeApproximately(lineX, PixelTolerance, "the line keeps its fraction");
		stand.AreaInView(hoverTime).Center.X.Should().BeApproximately(
			lineX, PixelTolerance, "the time stays centred under the line");
	}

	[AvaloniaFact]
	public async Task ANewerSampleDuringAHover_RelabelsItsTime()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var strip = stand.StripCanvas;
		var viewModel = stand.Model.ViewModel;
		var (_, hoverTime) = HoverControls(stand);
		stand.MovePointer(new Point(strip.Bounds.Width - 1.0, strip.Bounds.Height / 2.0));
		var timeBefore = hoverTime.Text;

		stand.Model.Navigation.OnLiveEdge(MinimapStand.ExtentLast + TimeSpan.FromDays(1.0));
		Dispatcher.UIThread.RunJobs();

		hoverTime.Text.Should().NotBe(timeBefore, "a later right bound moves the time under the pointer")
			.And.Be(viewModel.HoverLabel);
	}

	[AvaloniaFact]
	public async Task ANewFirstSampleDuringAHover_HidesTheWiderFirstLabelItNowCovers()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: false);
		var strip = stand.StripCanvas;
		var (_, hoverTime) = HoverControls(stand);
		var narrowFirst = new DateTime(2025, 12, 9, 12, 0, 0, DateTimeKind.Utc);
		var wideFirst = new DateTime(2025, 12, 10, 12, 0, 0, DateTimeKind.Utc);
		await ReloadWithFirstSampleAsync(stand, wideFirst);
		var (pointerX, timeLeft) = LastPointerHidingTheFirstLabel(stand);
		stand.MovePointer(new Point(pointerX, strip.Bounds.Height + OffStripDistance));
		await ReloadWithFirstSampleAsync(stand, narrowFirst);
		stand.MovePointer(new Point(pointerX, strip.Bounds.Height / 2.0));
		EndLabelsShown(stand)[0].Should().BeTrue("the narrow first label is clear of the time");

		await ReloadWithFirstSampleAsync(stand, wideFirst);

		stand.AreaInView(hoverTime).Left.Should().Be(timeLeft);
		EndLabelsShown(stand)[0].Should().BeFalse("the time covers the first label at its new width");
	}

	[AvaloniaFact]
	public async Task ADrag_KeepsTheLineUnderThePointerUntilItLeavesTheStrip()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var (strip, window) = (stand.StripCanvas, stand.Window);
		var (hoverLine, _) = HoverControls(stand);
		var dragTo = stand.StripPointAt(DragFraction);

		window.MouseDown(stand.InWindow(stand.StripPointAt(PressFraction)), MouseButton.Left);
		stand.MovePointer(dragTo, RawInputModifiers.LeftMouseButton);

		hoverLine.IsVisible.Should().BeTrue("the exit the capture raises does not end the hover");
		stand.AreaInView(hoverLine).Left.Should().BeApproximately(
			dragTo.X, PixelTolerance, "the line follows the drag");

		var offStrip = new Point(dragTo.X, strip.Bounds.Height + OffStripDistance);
		stand.MovePointer(offStrip, RawInputModifiers.LeftMouseButton);

		hoverLine.IsVisible.Should().BeFalse("a captured drag that leaves the strip hides the line");

		window.MouseUp(stand.InWindow(dragTo), MouseButton.Left);
	}

	private static (Control Line, TextBlock Time) HoverControls(MinimapViewStand stand)
	{
		return (stand.Named<Border>("HoverLine"), stand.Named<TextBlock>("HoverTimeLabel"));
	}

	private static bool[] EndLabelsShown(MinimapViewStand stand)
	{
		return
		[
			IsShown(stand.Named<TextBlock>("ExtentFirstLabel")),
			IsShown(stand.Named<TextBlock>("ExtentLastLabel"))
		];
	}

	private static bool IsShown(Visual visual)
	{
		return visual.IsVisible && visual.Opacity > 0.0;
	}

	private static async Task ReloadWithFirstSampleAsync(MinimapViewStand stand, DateTime firstSample)
	{
		stand.Model.Provider.ArchiveFirstUtc = firstSample;
		await stand.Model.LoadExtentAsync();
		Dispatcher.UIThread.RunJobs();
	}

	private static (double PointerX, double TimeLeft) LastPointerHidingTheFirstLabel(MinimapViewStand stand)
	{
		var height = stand.StripCanvas.Bounds.Height / 2.0;
		var hoverTime = stand.Named<TextBlock>("HoverTimeLabel");
		var lastHiding = (PointerX: double.NaN, TimeLeft: double.NaN);

		for (var pointerX = 0.0; pointerX < FirstLabelSearchWidth; pointerX++)
		{
			stand.MovePointer(new Point(pointerX, height));
			if (EndLabelsShown(stand)[0])
			{
				return lastHiding;
			}

			lastHiding = (pointerX, stand.AreaInView(hoverTime).Left);
		}

		throw new InvalidOperationException("No pointer position within the search clears the first label.");
	}
}
