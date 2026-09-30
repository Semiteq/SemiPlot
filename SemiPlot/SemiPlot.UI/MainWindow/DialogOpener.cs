using Avalonia.Controls;

using SemiPlot.UI.Settings;

namespace SemiPlot.UI.MainWindow;

internal static class DialogOpener
{
	public static async void ShowAbout(Window owner, AboutInfo about, Action<Exception> reportFailure)
	{
		try
		{
			await new AboutDialog { DataContext = about }.ShowDialog(owner);
		}
		catch (Exception exception)
		{
			// An async void handler: a throw out of this catch reaches the dispatcher and ends the process.
			reportFailure(exception);
		}
	}

	public static async void ShowSettings(Window owner, SettingsViewModel settings, Action<Exception> reportFailure)
	{
		try
		{
			await new SettingsDialog { DataContext = settings }.ShowDialog(owner);
		}
		catch (Exception exception)
		{
			reportFailure(exception);
		}
		finally
		{
			settings.Dispose();
		}
	}
}
