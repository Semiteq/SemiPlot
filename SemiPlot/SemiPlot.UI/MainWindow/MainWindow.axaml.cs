using System.Reactive.Disposables;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using ReactiveUI.Avalonia;

using SemiPlot.UI.Export;
using SemiPlot.UI.Legend;
using SemiPlot.UI.PenEditor;
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
		_requests.Add(viewModel.AboutRequests.Subscribe(
			about => DialogOpener.ShowAbout(this, about, viewModel.ReportFailure)));
		_requests.Add(viewModel.SettingsRequests.Subscribe(
			settings => DialogOpener.ShowOrActivate(
				this,
				settings,
				() => new SettingsDialog { DataContext = settings },
				viewModel.ForgetClosedWindow,
				viewModel.ReportFailure)));
		_requests.Add(viewModel.PenEditorRequests.Subscribe(
			penEditor => DialogOpener.ShowOrActivate(
				this,
				penEditor,
				() => new PenEditorWindow
				{
					DataContext = penEditor,
					Width = PenEditorWindow.OpeningWidthFor(WorkingAreaWidth())
				},
				viewModel.ForgetClosedWindow,
				viewModel.ReportFailure)));
		_requests.Add(viewModel.ExportRequests.Subscribe(
			export => DialogOpener.ShowOrActivate(
				this,
				export,
				() => new CsvExportWindow { DataContext = export },
				viewModel.ForgetClosedWindow,
				viewModel.ReportFailure)));
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

	/// <summary>The working area of this window's screen in device-independent pixels, unbounded with no screen.</summary>
	private double WorkingAreaWidth()
	{
		return Screens.ScreenFromWindow(this) is { } screen
			? screen.WorkingArea.Width / screen.Scaling
			: double.PositiveInfinity;
	}
}
