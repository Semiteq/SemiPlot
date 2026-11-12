using Avalonia.Controls;

namespace SemiPlot.UI.Navigation;

public partial class NavigationBarView : UserControl
{
	public NavigationBarView()
	{
		InitializeComponent();

		Wire(MarkersButton, MarkerListContent);
		Wire(RecipeRunsButton, RecipeRunListContent);
		DataContextChanged += OnDataContextChanged;
	}

	/// <summary>
	/// Opens the marker list under the Markers button, or under the fallback while the bar is hidden.
	/// </summary>
	public void ShowMarkerList(Control fallbackAnchor)
	{
		ShowList(MarkersButton, fallbackAnchor);
	}

	/// <summary>
	/// Opens the recipe run list under the Recipe run button, or under the fallback while the bar is hidden.
	/// </summary>
	public void ShowRecipeRunList(Control fallbackAnchor)
	{
		ShowList(RecipeRunsButton, fallbackAnchor);
	}

	private static void Wire(DropDownButton button, NavigationListView list)
	{
		var flyout = (Flyout)button.Flyout!;
		flyout.Opening += (_, _) => list.Prepare();
		flyout.Opened += (_, _) => list.FocusFilter();
		list.DismissRequested += (_, _) => flyout.Hide();
	}

	private static void ShowList(DropDownButton button, Control fallbackAnchor)
	{
		button.Flyout?.ShowAt(button.IsEffectivelyVisible ? button : fallbackAnchor);
	}

	/// <summary>Each list keeps the bar's list model at any anchor, the Go menu's header included.</summary>
	private void OnDataContextChanged(object? sender, EventArgs e)
	{
		var bar = DataContext as NavigationBarViewModel;
		MarkerListContent.DataContext = bar?.MarkerList;
		RecipeRunListContent.DataContext = bar?.RecipeRunList;
	}
}
