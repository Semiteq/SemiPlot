using System.Reactive.Disposables;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using ReactiveUI.Avalonia;

using SemiPlot.UI.Legend;
using SemiPlot.UI.PenEditor;

namespace SemiPlot.UI.MainWindow;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
	private readonly CompositeDisposable _requests = [];

	public MainWindow()
	{
		InitializeComponent();
	}

	protected override void OnLoaded(RoutedEventArgs e)
	{
		base.OnLoaded(e);

		if (DataContext is not MainWindowViewModel viewModel)
		{
			return;
		}

		_requests.Add(viewModel.ExitRequests.Subscribe(_ => Close()));
		_requests.Add(viewModel.AboutRequests.Subscribe(
			about => DialogOpener.ShowAbout(this, about, viewModel.ReportFailure)));
		_requests.Add(viewModel.SettingsRequests.Subscribe(
			settings => DialogOpener.ShowSettings(this, settings, viewModel.ReportFailure)));
		_requests.Add(viewModel.PenEditorRequests.Subscribe(ShowPenEditor));
		viewModel.LegendViewModel.FitPanel(MaximumPanelWidth());
	}

	protected override void OnUnloaded(RoutedEventArgs e)
	{
		_requests.Clear();

		base.OnUnloaded(e);
	}

	private void OnPanelResizeHandleDragDelta(object? sender, VectorEventArgs e)
	{
		if (DataContext is MainWindowViewModel viewModel)
		{
			viewModel.LegendViewModel.ResizePanel(e.Vector.X);
		}
	}

	private void OnContentGridSizeChanged(object? sender, SizeChangedEventArgs e)
	{
		if (DataContext is MainWindowViewModel viewModel)
		{
			viewModel.LegendViewModel.FitPanel(MaximumPanelWidth());
		}
	}

	private double MaximumPanelWidth()
	{
		return ContentGrid.Bounds.Width - TrendLegendViewModel.ChartMinWidth - PanelResizeHandle.Width;
	}

	private async void ShowPenEditor(PenEditorViewModel penEditor)
	{
		try
		{
			await new PenEditorWindow { DataContext = penEditor }.ShowDialog(this);
		}
		catch (Exception exception)
		{
			(DataContext as MainWindowViewModel)?.ReportFailure(exception);
		}
		finally
		{
			penEditor.Dispose();
		}
	}
}
