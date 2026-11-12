using Avalonia.Controls;
using Avalonia.Input;

namespace SemiPlot.UI.MainWindow;

public partial class AppMenuBar : UserControl
{
	public AppMenuBar()
	{
		InitializeComponent();
	}

	/// <summary>
	/// The Go menu's header on the strip, where the marker and run lists open while the navigation bar is hidden.
	/// </summary>
	public Control GoMenuHeader => GoMenu;

	/// <summary>The Go item whose gesture the key is, or null; the window runs that item's command.</summary>
	public MenuItem? GoItemFor(KeyEventArgs key)
	{
		return GoMenu.Items.OfType<MenuItem>().FirstOrDefault(item => item.InputGesture?.Matches(key) == true);
	}
}
