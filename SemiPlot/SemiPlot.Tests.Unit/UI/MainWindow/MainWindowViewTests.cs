using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

/// <summary>The realised window: which rows stand, and which flag each of them follows.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class MainWindowViewTests
{
	// Every row of the window, not only the one the failure names: this window has no chart and no services,
	// so a row that defaults to visible renders empty chrome over the one text the operator needs.
	[AvaloniaFact]
	public void MainWindow_WithAStartupFailure_ShowsTheFailureAndNothingElseBelowTheChart()
	{
		var failure = new ArchiveFailureView(
			"No connection to the archive",
			"SemiPlot could not open a connection to 'semiplot' at scada-host:5432.",
			"Check that the PostgreSQL server is running.",
			MessageSeverity.Error);
		using var viewModel = NewViewModel();
		viewModel.StartupFailure = failure;
		var window = new SemiPlot.UI.MainWindow.MainWindow { DataContext = viewModel };

		window.Show();
		Dispatcher.UIThread.RunJobs();

		ReadText(window, "StartupFailureTitle").Should().Be(failure.Title);
		ReadText(window, "StartupFailureDetail").Should().Be(failure.Detail);
		ReadText(window, "StartupFailureRemedy").Should().Be(failure.Remedy);
		RowVisibility(window, "StartupFailurePanel").Should().BeTrue();
		RowVisibility(window, "StatusBar").Should().BeFalse("the failure row speaks for the connection");
		RowVisibility(window, "MessagePanel").Should()
			.BeFalse("nothing has reported into this window's panel, so it carries no row");
		EmptyCatalogueMessage(window).IsVisible.Should()
			.BeFalse("there is no chart on this path, so there is no empty catalogue either");
	}

	// The row binds the same flag the View menu writes and reads back, and a failure opens it: an entry that
	// landed off screen would be a failure the operator is never shown.
	[AvaloniaFact]
	public void MessagePanelRow_StartsClosedAndOpensOnTheFirstFailure()
	{
		using var panel = new MessagePanelViewModel();
		using var viewModel = new MainWindowViewModel(
			panel, NewStatusBar(panel), NullLogger<MainWindowViewModel>.Instance);
		var window = new SemiPlot.UI.MainWindow.MainWindow { DataContext = viewModel };
		window.Show();
		var row = window.FindControl<Border>("MessagePanel");

		row.Should().NotBeNull();
		row!.IsVisible.Should().BeFalse("a session that has not failed is not given the row");

		panel.Report(new ArchiveFailureView("Archive unreachable", "detail", "remedy", MessageSeverity.Warning));
		Dispatcher.UIThread.RunJobs();

		row.IsVisible.Should().BeTrue();

		panel.ToggleCommand.Execute().Subscribe();
		Dispatcher.UIThread.RunJobs();

		row.IsVisible.Should().BeFalse("the operator closed the panel");
	}

	// Read off the realised window: a binding to a valid but wrong property compiles and leaves the row
	// standing, which only the row itself can tell.
	[AvaloniaFact]
	public void EveryViewMenuRow_FollowsItsOwnFlagOnTheRealisedWindow()
	{
		using var viewModel = NewViewModel();
		var window = new SemiPlot.UI.MainWindow.MainWindow { DataContext = viewModel };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		var rows = new (string Name, Action Toggle)[]
		{
			("NavigationBar", () => viewModel.ToggleNavigationBarCommand.Execute().Subscribe()),
			("LegendPanel", () => viewModel.ToggleLegendCommand.Execute().Subscribe()),
			("MinimapRow", () => viewModel.ToggleMinimapCommand.Execute().Subscribe())
		};

		foreach (var (name, toggle) in rows)
		{
			var row = window.FindControl<Border>(name);
			row.Should().NotBeNull("'{0}' is a named row of the window", name);
			row!.IsVisible.Should().BeTrue("'{0}' starts on screen", name);

			toggle();
			Dispatcher.UIThread.RunJobs();

			row.IsVisible.Should().BeFalse("'{0}' follows the flag its command wrote", name);

			toggle();
			Dispatcher.UIThread.RunJobs();

			row.IsVisible.Should().BeTrue("'{0}' comes back", name);
		}
	}

	private static string? ReadText(Window window, string name)
	{
		return window.FindControl<TextBlock>(name)?.Text;
	}

	private static bool RowVisibility(Window window, string name)
	{
		var row = window.FindControl<Border>(name);
		row.Should().NotBeNull("'{0}' is a named row of the window", name);

		return row.IsVisible;
	}

	// The chart view carries its own name scope, so the message is reached through the visual tree.
	private static TextBlock EmptyCatalogueMessage(Window window)
	{
		return window
			.GetVisualDescendants()
			.OfType<TextBlock>()
			.Single(block => block.Name == "EmptyCatalogueMessage");
	}
}
