using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Tests.Unit.UI.Chart;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Navigation;

using static SemiPlot.Tests.Unit.UI.ViewProbe;

namespace SemiPlot.Tests.Unit.UI.Navigation;

/// <summary>The bar's two lists.</summary>
internal enum StandList
{
	Markers,
	RecipeRuns
}

/// <summary>A realised bar over a chart whose archive runs 06:00 to 12:00 on 1 June 2026, the window the last hour.</summary>
internal sealed class NavigationBarStand : IDisposable
{
	// The headless shaper advances every glyph by the font size, so the bar runs far past its Inter width.
	private const double HeadlessWindowWidth = 4000.0;

	private static readonly DateTime _day = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

	private readonly FakeMarkerChannel _markers;
	private readonly NavigationBarViewModel _bar;
	private Flyout? _openList;

	private NavigationBarStand(FakeMarkerChannel markers, TrendChartViewModel chart, NavigationBarViewModel bar)
	{
		_markers = markers;
		Chart = chart;
		_bar = bar;
		View = new NavigationBarView { DataContext = bar };
		Window = Realise(new Window { Content = View, Width = HeadlessWindowWidth, Height = 600 });
	}

	public TrendChartViewModel Chart { get; }

	public NavigationBarView View { get; }

	public Window Window { get; }

	public Flyout MarkerList => ListFlyout(StandList.Markers);

	public NavigationListView MarkerListView => ListView(StandList.Markers);

	public TextBox MarkerFilter => Filter(StandList.Markers);

	public static NavigationBarStand Show()
	{
		var markers = new FakeMarkerChannel();
		var (chart, bar) = NavigationBarTestBuilder.CreateBar(markers);
		chart.Navigation.TrackDataExtents(At(6, 0), At(12, 0));

		return new NavigationBarStand(markers, chart, bar);
	}

	public static DateTime At(int hour, int minute)
	{
		return _day.AddHours(hour).AddMinutes(minute);
	}

	public T Named<T>(string name)
		where T : Control
	{
		return ViewProbe.Named<T>(View, name);
	}

	public void Push(params StoredMarker[] markers)
	{
		_markers.Push(markers);
		Dispatcher.UIThread.RunJobs();
	}

	public (DateTime From, DateTime To) WindowOfChart()
	{
		return (Chart.Navigation.From, Chart.Navigation.To);
	}

	public static string ButtonOf(StandList list)
	{
		return list == StandList.Markers ? "MarkersButton" : "RecipeRunsButton";
	}

	public Flyout ListFlyout(StandList list)
	{
		return (Flyout)Named<DropDownButton>(ButtonOf(list)).Flyout!;
	}

	public NavigationListView ListView(StandList list)
	{
		return Named<NavigationListView>(list == StandList.Markers ? "MarkerListContent" : "RecipeRunListContent");
	}

	public TextBox Filter(StandList list)
	{
		return ViewProbe.Named<TextBox>(ListView(list), "ListFilter");
	}

	/// <summary>Opens the marker list with a click and returns its realised entry rows, top to bottom.</summary>
	public IReadOnlyList<Border> OpenMarkerList()
	{
		return OpenList(StandList.Markers);
	}

	/// <summary>Opens the list with a click on its button and returns its realised entry rows, top to bottom.</summary>
	public IReadOnlyList<Border> OpenList(StandList list)
	{
		HeadlessInput.Click(Window, Named<DropDownButton>(ButtonOf(list)));
		_openList = ListFlyout(list);
		_openList.IsOpen.Should().BeTrue();

		return Rows(list);
	}

	/// <summary>The open marker list's realised entry rows, top to bottom.</summary>
	public IReadOnlyList<Border> MarkerRows()
	{
		return Rows(StandList.Markers);
	}

	/// <summary>The list's realised entry rows, top to bottom.</summary>
	public IReadOnlyList<Border> Rows(StandList list)
	{
		return [.. ListView(list).GetVisualDescendants().OfType<Border>().Where(row => row.Name == "EntryRow")];
	}

	public void Dispose()
	{
		_openList?.Hide();
		Dismiss(Window);
		_bar.Dispose();
	}
}
