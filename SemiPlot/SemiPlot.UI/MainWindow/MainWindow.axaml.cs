using System.Reactive.Disposables;

using Avalonia.Interactivity;

using ReactiveUI.Avalonia;

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
	}

	protected override void OnUnloaded(RoutedEventArgs e)
	{
		_requests.Clear();

		base.OnUnloaded(e);
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
}
