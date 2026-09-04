using Avalonia;
using Avalonia.Headless;

using ReactiveUI.Avalonia;

using SemiPlot.Tests.Unit;

using App = SemiPlot.UI.App;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace SemiPlot.Tests.Unit;

public static class TestAppBuilder
{
	public static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>()
			.UseHeadless(new AvaloniaHeadlessPlatformOptions())
			.UseReactiveUI(_ => { });
	}
}
