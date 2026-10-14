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

	public static void ShowSettings(Window owner, SettingsViewModel settings, Action<Exception> reportFailure)
	{
		ShowOwnedDialog(owner, () => new SettingsDialog { DataContext = settings }, settings, reportFailure);
	}

	public static async void ShowOwnedDialog(
		Window owner, Func<Window> createDialog, IDisposable viewModel, Action<Exception> reportFailure)
	{
		try
		{
			await createDialog().ShowDialog(owner);
		}
		catch (Exception exception)
		{
			reportFailure(exception);
		}
		finally
		{
			viewModel.Dispose();
		}
	}
}
