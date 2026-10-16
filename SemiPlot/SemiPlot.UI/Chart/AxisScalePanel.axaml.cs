using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

	/// <summary>A bound the keyboard reaches is typed over; a click places the caret.</summary>
	protected override void OnGotFocus(FocusChangedEventArgs e)
	{
		base.OnGotFocus(e);

		if (e.Source is TextBox bound && e.NavigationMethod != NavigationMethod.Pointer)
		{
			bound.SelectAll();
		}
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
