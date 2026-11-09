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

	/// <summary>
	/// Shows a window over the view model non-modally, or restores and activates the owned window already showing it.
	/// </summary>
	public static void ShowOrActivate(
		Window owner, IDisposable viewModel, Func<Window> createWindow, Action<object> forget,
		Action<Exception> reportFailure)
	{
		if (owner.OwnedWindows.FirstOrDefault(window => ReferenceEquals(window.DataContext, viewModel)) is { } open)
		{
			// Win32 activation only raises the window; a minimised one stays on the taskbar it is hidden from.
			if (open.WindowState == WindowState.Minimized)
			{
				open.WindowState = WindowState.Normal;
			}

			open.Activate();

			return;
		}

		var released = false;
		Window? shown = null;

		try
		{
			shown = createWindow();
			shown.Closed += (_, _) => Release();
			shown.Show(owner);
		}
		catch (Exception exception)
		{
			reportFailure(exception);
			shown?.Close();
			Release();
		}

		void Release()
		{
			if (released)
			{
				return;
			}

			released = true;
			try
			{
				forget(viewModel);
				viewModel.Dispose();
			}
			catch (Exception exception)
			{
				reportFailure(exception);
			}
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
