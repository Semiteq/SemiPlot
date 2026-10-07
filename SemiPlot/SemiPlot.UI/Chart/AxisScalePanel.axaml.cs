using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SemiPlot.UI.Chart;

public partial class AxisScalePanel : UserControl
{
	private IDisposable? _boundsFocus;

	public AxisScalePanel()
	{
		InitializeComponent();
	}

	protected override void OnLoaded(RoutedEventArgs e)
	{
		base.OnLoaded(e);

		_boundsFocus = AxisScaleBounds.GetObservable(IsKeyboardFocusWithinProperty)
			.Subscribe(OnBoundsFocusWithinChanged);
	}

	protected override void OnUnloaded(RoutedEventArgs e)
	{
		_boundsFocus?.Dispose();
		_boundsFocus = null;

		base.OnUnloaded(e);
	}

	// docs/architecture/trend-interaction.md#the-axis-scale-panel
	private void OnBoundsFocusWithinChanged(bool isFocusWithin)
	{
		if (isFocusWithin || DataContext is not AxisScalePanelViewModel viewModel)
		{
			return;
		}

		try
		{
			viewModel.CommitBounds();
		}
		catch (Exception exception)
		{
			viewModel.ReportFailure(exception);
		}
	}
}
