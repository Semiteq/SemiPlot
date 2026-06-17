using Avalonia;
using Avalonia.Headless;

using ReactiveUI.Avalonia;

using SemiPlot.Tests;

using App = SemiPlot.UI.App;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace SemiPlot.Tests;

public static class TestAppBuilder
{
	public static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>()
			.UseHeadless(new AvaloniaHeadlessPlatformOptions())
			.UseReactiveUI(_ => { });
	}
}
