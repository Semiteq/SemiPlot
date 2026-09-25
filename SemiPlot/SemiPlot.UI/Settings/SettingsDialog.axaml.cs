using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SemiPlot.UI.Settings;

public partial class SettingsDialog : Window
{
	public SettingsDialog()
	{
		InitializeComponent();
	}

	private void OnCloseClick(object? sender, RoutedEventArgs e)
	{
		Close();
	}
}
