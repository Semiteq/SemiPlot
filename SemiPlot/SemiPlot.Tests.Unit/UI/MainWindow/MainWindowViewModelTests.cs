using System.Reactive.Linq;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Settings;
using SemiPlot.UI.Startup;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class MainWindowViewModelTests
{
	[AvaloniaFact]
	public void SetChart_BuildsTheNavigationBarAndTheLegend()
	{
		using var viewModel = NewViewModel();
		var chart = CreateChartWithPens();

		viewModel.SetChart(chart);

		viewModel.NavigationBarViewModel.Should().NotBeNull();
		viewModel.LegendViewModel.Should().NotBeNull();
	}

	[AvaloniaFact]
	public void SetChart_WithNull_DropsTheNavigationBarAndTheLegend()
	{
		using var viewModel = NewViewModel();
		viewModel.SetChart(CreateChartWithPens());

		viewModel.SetChart(null);

		viewModel.NavigationBarViewModel.Should().BeNull();
		viewModel.LegendViewModel.Should().BeNull();
	}

	[AvaloniaFact]
	public void SetChart_WithTheSameInstance_KeepsTheChartAlive()
	{
		using var viewModel = NewViewModel();
		var chart = CreateChartWithPens();
		viewModel.SetChart(chart);
		var activePenId = chart.ActivePenId;
		var navigationBar = viewModel.NavigationBarViewModel;

		viewModel.SetChart(chart);

		// Every mutating member throws ObjectDisposedException once the chart is disposed.
		chart.SetActivePen(activePenId).Should().BeTrue();
		viewModel.NavigationBarViewModel.Should().BeSameAs(navigationBar);
	}

	// The status bar outlives the chart, so the bar follows whichever chart is in force rather than being
	// rebuilt with it: a rebuilt bar would lose the connection stream bound once at startup.
	[AvaloniaFact]
	public void SetChart_PointsTheStatusBarAtItsLayer()
	{
		using var panel = new MessagePanelViewModel();
		var statusBar = NewStatusBar(panel);
		using var viewModel = new MainWindowViewModel(
			panel, statusBar, AppContext.BaseDirectory, NullLoggerFactory.Instance);
		var chart = CreateChartWithPens();

		viewModel.SetChart(chart);

		statusBar.ActiveLayer.Should().Be(chart.Navigation.ActiveLayer);
		viewModel.StatusBar.Should().BeSameAs(statusBar);
	}

	[AvaloniaFact]
	public void StartupFailure_WhenSet_MakesThePanelVisibleAndTheChartNull()
	{
		var failure = new ArchiveFailureView(
			"Startup failed",
			"detail",
			"remedy",
			MessageSeverity.Error);

		using var viewModel = NewViewModel();
		viewModel.StartupFailure = failure;

		viewModel.HasStartupFailure.Should().BeTrue();
		viewModel.ChartViewModel.Should().BeNull();
	}

	// The code-behind route: the About dialog's throw cannot escape an async void handler, so it reports
	// through the view model rather than through a logger the view would have to hold itself.
	[AvaloniaFact]
	public void ReportFailure_PutsTheThrowInThePanel()
	{
		using var panel = new MessagePanelViewModel();
		using var viewModel = new MainWindowViewModel(
			panel, NewStatusBar(panel), AppContext.BaseDirectory, NullLoggerFactory.Instance);

		viewModel.ReportFailure(new InvalidOperationException("the dialog refused"));

		panel.Entries.Should().ContainSingle();
		panel.Entries[0].View.Detail.Should().Contain("the dialog refused");
		panel.IsVisible.Should().BeTrue();
	}

	[AvaloniaFact]
	public void ShowSettings_WithNoDirectory_CannotExecute()
	{
		using var viewModel = NewViewModel(configDirectory: null);

		viewModel.ShowSettingsCommand.Should().NotBeNull();
		CanShowSettings(viewModel).Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task ShowSettings_WithADirectory_EmitsOneViewModelHoldingItsValues()
	{
		var configDirectory = Directory.CreateTempSubdirectory("semiplot-main-settings-").FullName;
		try
		{
			WriteSection(configDirectory, StartupSequence.SettingsDirectoryName, "app.yaml", "locale: en\ntheme: dark\n");
			WriteSection(
				configDirectory,
				StartupProbe.ConnectionDirectoryName,
				"connection.yaml",
				"host: 10.20.30.40\nport: 5433\ndatabase: archive\nuser: viewer\npassword: secret\n"
				+ "poll_interval_ms: 250\n");
			using var viewModel = NewViewModel(configDirectory);
			var requests = new List<SettingsViewModel>();
			using var subscription = viewModel.SettingsRequests.Subscribe(requests.Add);

			CanShowSettings(viewModel).Should().BeTrue();
			await viewModel.ShowSettingsCommand.Execute();

			using var settings = requests.Should().ContainSingle().Which;
			settings.SelectedLanguage!.Token.Should().Be("en");
			settings.SelectedTheme!.Token.Should().Be("dark");
			settings.Host.Should().Be("10.20.30.40");
			settings.Port.Should().Be(5433);
			settings.Database.Should().Be("archive");
			settings.User.Should().Be("viewer");
			settings.Password.Should().Be("secret");
			settings.PollInterval.Should().Be(250);
			viewModel.MessagePanel.Entries.Should().BeEmpty();
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	private static bool CanShowSettings(MainWindowViewModel viewModel)
	{
		bool? latest = null;

		using (viewModel.ShowSettingsCommand.CanExecute.Subscribe(value => latest = value))
		{
			return latest ?? throw new InvalidOperationException("The command replayed no execute state.");
		}
	}

	private static void WriteSection(string configDirectory, string section, string name, string content)
	{
		var directory = Directory.CreateDirectory(Path.Combine(configDirectory, section)).FullName;
		File.WriteAllText(Path.Combine(directory, name), content);
	}
}
