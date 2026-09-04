using Avalonia;
using Avalonia.Headless;

using ReactiveUI.Avalonia;

using SemiPlot.Tests.Integration.Journeys;

using App = SemiPlot.UI.App;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace SemiPlot.Tests.Integration.Journeys;

// Every journey constructs TrendChartViewModel under [AvaloniaFact], and AvaloniaTestApplication is an
// assembly attribute, so this assembly carries its own builder.
public static class TestAppBuilder
{
	public static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>()
			.UseHeadless(new AvaloniaHeadlessPlatformOptions())
			.UseReactiveUI(_ => { });
	}
}
