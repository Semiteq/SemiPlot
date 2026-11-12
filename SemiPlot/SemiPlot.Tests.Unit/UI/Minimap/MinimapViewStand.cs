using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Minimap;

namespace SemiPlot.Tests.Unit.UI.Minimap;

internal sealed class MinimapViewStand : IDisposable
{
	private const int WindowWidth = 900;

	private MinimapViewStand(TimeZoneInfo? archiveZone = null)
	{
		Model = new MinimapStand(archiveZone: archiveZone);
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

	public MinimapStand Model { get; }

	public MinimapView View { get; }

	public Window Window { get; }

	public Canvas StripCanvas => Named<Canvas>("StripCanvas");

	public MinimapBand BandLayer => Named<MinimapBand>("BandLayer");

	public Panel CalendarCellRow => Named<Panel>("CalendarCellRow");

	public MinimapDayCalendar Calendar => Named<MinimapDayCalendar>("CalendarCells");

	public DayCalendar ShownCalendar => Model.ViewModel.Calendar(CalendarCellRow.Bounds.Width)
		?? throw new InvalidOperationException("The day calendar needs an extent and a width.");

	/// <summary>Shows the minimap; a null extent is the stand's seven days, a null zone UTC.</summary>
	public static async Task<MinimapViewStand> ShowAsync(
		bool showPens,
		IReadOnlyList<ArchiveDaySpan>? spans = null,
		(DateTime First, DateTime Last)? extent = null,
		TimeZoneInfo? archiveZone = null)
	{
		var stand = new MinimapViewStand(archiveZone);
		var model = stand.Model;
		if (showPens)
		{
			model.ShowPens();
		}

		model.Provider.ArchiveDaySpans = spans ?? [];
		var (first, last) = extent ?? (MinimapStand.ExtentFirst, MinimapStand.ExtentLast);
		(model.Provider.ArchiveFirstUtc, model.Provider.ArchiveLastUtc) = (first, last);

		// The window seeds to [last - width, last], so a press inside the extent pans without clamping.
		model.Navigation.TrackDataExtents(first, last);
		await model.LoadExtentAsync();
		stand.LandRead();
		model.ViewModel.HasExtent.Should().BeTrue("without an extent the strip ignores every pointer position");

		stand.Show();
		stand.StripCanvas.Bounds.Width.Should().BeGreaterThan(
			0.0, "the strip divides by its own width and draws no band at zero width");
		stand.LandRead();
		if (spans is { Count: > 0 })
		{
			stand.LandRead();
			model.Spans.Should().NotBeEmpty("the day calendar draws from the landed day spans");
		}

		return stand;
	}

	public static MinimapViewStand ShowWithoutExtent()
	{
		var stand = new MinimapViewStand();
		stand.Show();

		return stand;
	}

	/// <summary>A part of the minimap or of its day calendar, which keeps its own name scope.</summary>
	public T Named<T>(string name)
		where T : Control
	{
		return View.FindControl<T>(name)
			?? View.FindControl<DayCalendarView>("DayCalendarView")?.FindControl<T>(name)
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

	/// <summary>The whole pixel at the middle of the day's cell, halfway down the cell row.</summary>
	public Point CellPointAt(DateOnly day)
	{
		var calendar = ShownCalendar;
		var cell = calendar.Cell(calendar.IndexOf(day));

		return new Point(Math.Floor(cell.Left + (cell.Width / 2.0)), CalendarCellRow.Bounds.Height / 2.0);
	}

	public Point CalendarInWindow(Point calendarPoint)
	{
		return CalendarCellRow.TranslatePoint(calendarPoint, Window)
			?? throw new InvalidOperationException("The day calendar is not in the window's visual tree.");
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

	public Rect AreaInStrip(Visual visual)
	{
		return AreaIn(StripCanvas, visual);
	}

	public void MovePointer(Point stripPoint, RawInputModifiers modifiers = RawInputModifiers.None)
	{
		Window.MouseMove(InWindow(stripPoint), modifiers);
		Dispatcher.UIThread.RunJobs();
	}

	public void MoveCalendarPointer(Point calendarPoint, RawInputModifiers modifiers = RawInputModifiers.None)
	{
		Window.MouseMove(CalendarInWindow(calendarPoint), modifiers);
		Dispatcher.UIThread.RunJobs();
	}

	public void LandRead()
	{
		Model.LandRead();
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
