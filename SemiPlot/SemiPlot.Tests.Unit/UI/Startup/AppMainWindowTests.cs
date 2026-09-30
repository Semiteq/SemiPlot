using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.UI;
using SemiPlot.UI.MainWindow;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

using MainWindowView = SemiPlot.UI.MainWindow.MainWindow;

namespace SemiPlot.Tests.Unit.UI.Startup;

/// <summary>
/// The window a successful start shows, driven through <c>App</c>: what it builds and when it releases.
/// </summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class AppMainWindowTests
{
	[AvaloniaFact]
	public void AStartShowsTheMainWindowOverTheContainersPanelAndClosingItDisposesTheComposition()
	{
		using var scope = new AppStateScope();
		using var archive = NewArchiveStand();

		App.Configure(scope.App, settings: null, Result.Ok(archive.Data), AppContext.BaseDirectory);

		var window = scope.App.CreateMainWindow();
		var viewModel = window.DataContext.Should().BeOfType<MainWindowViewModel>().Which;
		Dispatcher.UIThread.RunJobs();

		window.Should().BeOfType<MainWindowView>();
		App.ResolveMessagePanel().Should().BeSameAs(
			viewModel.MessagePanel, "the ReactiveUI handler reaches the panel the window shows");
		archive.Provider.OpenLiveSubscriptionCount.Should().Be(1);
		archive.Provider.ConnectionFaultsObserverCount.Should().Be(1);

		window.Show();
		Dispatcher.UIThread.RunJobs();
		window.Close();
		Dispatcher.UIThread.RunJobs();

		archive.Provider.OpenLiveSubscriptionCount.Should().Be(0, "closing the window disposes the chart");
		archive.Provider.ConnectionFaultsObserverCount.Should().Be(0, "closing the window disposes the coordinator");
	}
}
