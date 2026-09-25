using System.Reactive.Disposables;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using ReactiveUI;
using ReactiveUI.Avalonia;

using SemiPlot.UI.Legend;
using SemiPlot.UI.Settings;

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
		_requests.Add(viewModel.AboutRequests.Subscribe(ShowAbout));
		_requests.Add(viewModel.SettingsRequests.Subscribe(ShowSettings));
		_requests.Add(viewModel
			.WhenAnyValue(window => window.LegendViewModel)
			.Subscribe(legend => legend?.FitPanel(MaximumPanelWidth())));
	}

	protected override void OnUnloaded(RoutedEventArgs e)
	{
		_requests.Clear();

		base.OnUnloaded(e);
	}

	private void OnPanelResizeHandleDragDelta(object? sender, VectorEventArgs e)
	{
		if (DataContext is MainWindowViewModel { LegendViewModel: { } legend })
		{
			legend.ResizePanel(e.Vector.X);
		}
	}

	private void OnContentGridSizeChanged(object? sender, SizeChangedEventArgs e)
	{
		if (DataContext is MainWindowViewModel { LegendViewModel: { } legend })
		{
			legend.FitPanel(MaximumPanelWidth());
		}
	}

	private double MaximumPanelWidth()
	{
		return ContentGrid.Bounds.Width - TrendLegendViewModel.ChartMinWidth - PanelResizeHandle.Width;
	}

	private async void ShowAbout(AboutInfo about)
	{
		try
		{
			await new AboutDialog { DataContext = about }.ShowDialog(this);
		}
		catch (Exception exception)
		{
			// An async void handler: a throw out of this catch reaches the dispatcher and ends the process.
			(DataContext as MainWindowViewModel)?.ReportFailure(exception);
		}
	}

	private async void ShowSettings(SettingsViewModel settings)
	{
		try
		{
			await new SettingsDialog { DataContext = settings }.ShowDialog(this);
		}
		catch (Exception exception)
		{
			(DataContext as MainWindowViewModel)?.ReportFailure(exception);
		}
		finally
		{
			settings.Dispose();
		}
	}
}
