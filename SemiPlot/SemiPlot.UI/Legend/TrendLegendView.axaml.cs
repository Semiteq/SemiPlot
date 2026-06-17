using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace SemiPlot.UI.Legend;

public partial class TrendLegendView : UserControl
{
	public TrendLegendView()
	{
		InitializeComponent();
	}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}

	private void OnRowPressed(object? sender, PointerPressedEventArgs eventArgs)
	{
		if (sender is Control { DataContext: TrendLegendRowViewModel row })
		{
			row.Select();
		}
	}
}
