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

	// Clicking anywhere on a row makes its pen the active pen on the chart.
	private void OnRowPressed(object? sender, PointerPressedEventArgs eventArgs)
	{
		if (sender is Control { DataContext: TrendLegendRowViewModel row })
		{
			row.Select();
		}
	}
}
