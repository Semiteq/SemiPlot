using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SemiPlot.UI.MainWindow;

public partial class AboutDialog : Window
{
	public AboutDialog()
	{
		InitializeComponent();
	}

	private void OnCloseClick(object? sender, RoutedEventArgs e)
	{
		Close();
	}
}
