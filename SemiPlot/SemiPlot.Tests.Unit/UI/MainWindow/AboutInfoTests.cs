using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using SemiPlot.UI.Localization;
using SemiPlot.UI.MainWindow;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

/// <summary>What Help > About names: the run's own identity, and the dialog that shows it.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class AboutInfoTests
{
	[AvaloniaFact]
	public void AboutDialog_ShowsTheNameTheVersionAndTheConfigurationDirectory()
	{
		var about = new AboutInfo("SemiPlot", "1.2.3", @"C:\semiplot\config");
		var dialog = new AboutDialog { DataContext = about };
		try
		{
			dialog.Show();
			Dispatcher.UIThread.RunJobs();

			dialog.FindControl<TextBlock>("AboutApplicationName")!.Text.Should().Be("SemiPlot");
			dialog.FindControl<TextBlock>("AboutVersion")!.Text.Should().Be("1.2.3");
			dialog.FindControl<TextBlock>("AboutConfigurationDirectory")!.Text
				.Should().Be(@"C:\semiplot\config");
		}
		finally
		{
			dialog.Close();
		}
	}

	[Fact]
	public void AboutInfo_NamesTheConfigurationDirectoryTheRunRead()
	{
		var about = AboutInfo.For(
			["--config-dir", @"C:\semiplot\config", "--log-file", @"C:\semiplot\semiplot.log", "--logging-level", "info"]);

		about.ConfigurationDirectory.Should().Be(@"C:\semiplot\config");
	}

	// Program.ReportStartupFailure opens this window with its menu when the parse failed, so Help > About is
	// reachable with nothing to name and the row says so rather than standing empty.
	[Fact]
	public void AboutInfo_WithArgumentsThatDoNotParse_SaysTheDirectoryIsNotKnown()
	{
		var about = AboutInfo.For(["--nonsense"]);

		about.ConfigurationDirectory.Should().Be(Resources.AboutConfigurationUnknown);
		about.ConfigurationDirectory.Should().NotBeEmpty();
	}
}
