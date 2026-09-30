using System.Reactive.Disposables;

using Avalonia.Interactivity;

using ReactiveUI.Avalonia;

using SemiPlot.UI.MainWindow;

namespace SemiPlot.UI.Startup;

public partial class StartupFailureWindow : ReactiveWindow<StartupFailureViewModel>
{
	private readonly CompositeDisposable _requests = [];

	public StartupFailureWindow()
	{
		InitializeComponent();
	}

	protected override void OnLoaded(RoutedEventArgs e)
	{
		base.OnLoaded(e);

		if (DataContext is not StartupFailureViewModel viewModel)
		{
			return;
		}

		_requests.Add(viewModel.ExitRequests.Subscribe(_ => Close()));
		_requests.Add(viewModel.AboutRequests.Subscribe(
			about => DialogOpener.ShowAbout(this, about, viewModel.ReportFailure)));
		_requests.Add(viewModel.SettingsRequests.Subscribe(
			settings => DialogOpener.ShowSettings(this, settings, viewModel.ReportFailure)));
	}

	protected override void OnUnloaded(RoutedEventArgs e)
	{
		_requests.Clear();

		base.OnUnloaded(e);
	}
}
