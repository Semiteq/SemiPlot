using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace SemiPlot.UI.Windows;

/// <summary>How an owned window closes: Escape closes it, and its own work holds a close until the work ends.</summary>
internal sealed class OwnedWindowClosing(Window window, Func<Task?> pendingWork)
{
	private Task? _held;
	private Window? _ownerToClose;

	/// <summary>
	/// False after cancelling the close; the window, or the owner that asked, closes when the work ends.
	/// </summary>
	public bool Lets(WindowClosingEventArgs e)
	{
		var owner = e.CloseReason != WindowCloseReason.WindowClosing ? window.Owner as Window : null;

		if (_held is { IsCompleted: false })
		{
			_ownerToClose ??= owner;
			e.Cancel = true;

			return false;
		}

		if (pendingWork() is not { } work)
		{
			return true;
		}

		_ownerToClose = owner;
		_held = CloseAfterAsync(work);
		e.Cancel = true;

		return false;
	}

	public void CloseOnEscape(KeyEventArgs e)
	{
		if (e is { Key: Key.Escape, KeyModifiers: KeyModifiers.None, Handled: false })
		{
			e.Handled = true;
			window.Close();
		}
	}

	private async Task CloseAfterAsync(Task work)
	{
		await work.ConfigureAwait(
			ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);

		var closing = _ownerToClose ?? window;
		_ownerToClose = null;
		Dispatcher.UIThread.Post(() => CloseIfOpen(closing));
	}

	// Two held windows of one owner can both post its close in one turn.
	private static void CloseIfOpen(Window closing)
	{
		if (closing.IsVisible)
		{
			closing.Close();
		}
	}
}
