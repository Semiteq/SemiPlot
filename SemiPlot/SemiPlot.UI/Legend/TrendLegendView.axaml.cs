using Avalonia.Controls;
using Avalonia.Input;

namespace SemiPlot.UI.Legend;

public partial class TrendLegendView : UserControl
{
	public TrendLegendView()
	{
		InitializeComponent();
	}

	private void OnRowPressed(object? sender, PointerPressedEventArgs eventArgs)
	{
		if (sender is Control { DataContext: TrendLegendRowViewModel row })
		{
			row.Select();
		}
	}
}
