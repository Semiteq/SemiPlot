using System.Diagnostics;

using Avalonia.Threading;

namespace SemiPlot.Tests.Unit.UI;

/// <summary>
/// Waits for a condition a headless view test cannot advance itself, pumping the dispatcher meanwhile.
/// </summary>
internal static class HeadlessWait
{
	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

	public static async Task Until(Func<bool> condition)
	{
		var clock = Stopwatch.StartNew();

		while (!condition())
		{
			if (clock.Elapsed > _timeout)
			{
				throw new TimeoutException("The awaited condition did not hold in time.");
			}

			await Task.Delay(10);
			Dispatcher.UIThread.RunJobs();
		}
	}
}
