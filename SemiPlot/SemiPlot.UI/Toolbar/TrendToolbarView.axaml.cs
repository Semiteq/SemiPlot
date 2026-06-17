using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SemiPlot.UI.Toolbar;

public partial class TrendToolbarView : UserControl
{
	public TrendToolbarView()
	{
		InitializeComponent();
	}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}
}
