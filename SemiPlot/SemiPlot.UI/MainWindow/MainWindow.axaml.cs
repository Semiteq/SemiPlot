using Avalonia.Markup.Xaml;

using ReactiveUI.Avalonia;

namespace SemiPlot.UI.MainWindow;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
	public MainWindow()
	{
		InitializeComponent();
	}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}
}
