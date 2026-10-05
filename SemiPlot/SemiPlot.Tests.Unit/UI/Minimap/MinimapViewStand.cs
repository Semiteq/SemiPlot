using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using AwesomeAssertions;

using SemiPlot.UI.Minimap;

namespace SemiPlot.Tests.Unit.UI.Minimap;

internal sealed class MinimapViewStand : IDisposable
{
	private const int WindowWidth = 900;

	private MinimapViewStand()
	{
		View = new MinimapView
		{
			DataContext = Model.ViewModel
		};
		Window = new Window
		{
			Width = WindowWidth,
			SizeToContent = SizeToContent.Height,
			Content = View
		};
	}

	public MinimapStand Model { get; } = new();

	public MinimapView View { get; }

	public Window Window { get; }

	public Canvas StripCanvas => Named<Canvas>("StripCanvas");

	public MinimapBand BandLayer => Named<MinimapBand>("BandLayer");

	/// <summary>Seeds the window to [last - width, last] so a press inside the extent pans without clamping.</summary>
	public static async Task<MinimapViewStand> ShowAsync(bool showPens)
	{
		var stand = new MinimapViewStand();
		var model = stand.Model;
		if (showPens)
		{
			model.ShowPens();
		}

		await model.LoadExtentAsync();
		stand.LandBandRead();
		model.ViewModel.HasExtent.Should().BeTrue("without an extent the strip ignores every pointer position");

		model.Navigation.TrackDataExtents(MinimapStand.ExtentFirst, MinimapStand.ExtentLast);
		stand.Show();
		stand.StripCanvas.Bounds.Width.Should().BeGreaterThan(
			0.0, "the strip divides by its own width and draws no band at zero width");

		return stand;
	}

	public static MinimapViewStand ShowWithoutExtent()
	{
		var stand = new MinimapViewStand();
		stand.Show();

		return stand;
	}

	public T Named<T>(string name)
		where T : Control
	{
		return View.FindControl<T>(name)
			?? throw new InvalidOperationException($"The minimap names no {typeof(T).Name} '{name}'.");
	}

	/// <summary>Whole pixels, so the fraction the view computes back is exactly the one the expectation uses.</summary>
	public Point StripPointAt(double fraction)
	{
		return new Point(Math.Round(fraction * StripCanvas.Bounds.Width), StripCanvas.Bounds.Height / 2.0);
	}

	public Point InWindow(Point stripPoint)
	{
		return StripCanvas.TranslatePoint(stripPoint, Window)
			?? throw new InvalidOperationException("The strip canvas is not in the window's visual tree.");
	}

	public static Rect AreaIn(Visual ancestor, Visual visual)
	{
		var topLeft = visual.TranslatePoint(new Point(0.0, 0.0), ancestor)
			?? throw new InvalidOperationException(
				$"{visual.GetType().Name} is not in the visual tree of {ancestor.GetType().Name}.");

		return new Rect(topLeft, visual.Bounds.Size);
	}

	public Rect AreaInView(Visual visual)
	{
		return AreaIn(View, visual);
	}

	public void MovePointer(Point stripPoint, RawInputModifiers modifiers = RawInputModifiers.None)
	{
		Window.MouseMove(InWindow(stripPoint), modifiers);
		Dispatcher.UIThread.RunJobs();
	}

	public void LandBandRead()
	{
		Model.LandBandRead();
		Dispatcher.UIThread.RunJobs();
	}

	public void Dispose()
	{
		Window.Close();
		Model.Dispose();
	}

	private void Show()
	{
		Window.Show();
		Dispatcher.UIThread.RunJobs();
	}
}
