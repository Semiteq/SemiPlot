using Avalonia;
using Avalonia.Headless;

using ReactiveUI.Avalonia;

using SemiPlot.Tests.Journeys;

using App = SemiPlot.UI.App;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace SemiPlot.Tests.Journeys;

// AvaloniaTestApplication is an assembly attribute, so the one in SemiPlot.Tests does not travel across
// a project reference. Every journey constructs TrendChartViewModel under [AvaloniaFact], which needs
// this builder in this assembly.
public static class TestAppBuilder
{
	public static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>()
			.UseHeadless(new AvaloniaHeadlessPlatformOptions())
			.UseReactiveUI(_ => { });
	}
}
