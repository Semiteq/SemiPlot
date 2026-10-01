using System.Diagnostics;

using SemiPlot.UI;
using SemiPlot.UI.Startup;

using Serilog.Events;

namespace SemiPlot.Tests.Unit.UI.Startup;

/// <summary>Launch options and launchers for a test that reaches the code with a configuration directory only.</summary>
internal static class TestLaunch
{
	public const string HostPath = "/opt/semiplot/SemiPlot.UI";

	public static StartupOptions OptionsAt(string configDirectory)
	{
		return new StartupOptions(configDirectory, "semiplot-test.log", LogEventLevel.Information);
	}

	/// <summary>A launcher over a fake host path, so no test starts the test host.</summary>
	public static InstanceLauncher LauncherAt(string configDirectory, Action<ProcessStartInfo>? start = null)
	{
		return new InstanceLauncher(
			OptionsAt(configDirectory), HostPath, entryAssemblyPath: null, start ?? (_ => { }));
	}
}
