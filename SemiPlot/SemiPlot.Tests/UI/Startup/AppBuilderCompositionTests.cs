using Avalonia.Logging;

using AwesomeAssertions;

using SemiPlot.UI;

using Xunit;

namespace SemiPlot.Tests.UI.Startup;

/// <summary>
/// The one link between the application's builder chain and the test suite. <c>TestAppBuilder</c>
/// composes <c>UseHeadless</c>, which registers rendering, windowing and text shaping of its own, so
/// every other test in this project passes against whatever <see cref="App.BuildAvaloniaApp"/> holds —
/// including a chain missing a subsystem the desktop application dies without. Avalonia 12 stopped
/// bringing a text shaper along with Skia, and that gap reached a real launch before anything caught it.
/// <para>
/// <c>AppBuilder.Configure</c> only constructs the builder and each <c>Use*</c> call only stores a
/// delegate, so reading the three initializers back initialises no platform. <c>LogToTrace()</c> is the
/// exception — it writes <c>Logger.Sink</c> when called — so the sink is restored around the call and the
/// process is left as it was found.
/// </para>
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class AppBuilderCompositionTests
{
	[Fact]
	public void BuildAvaloniaApp_RegistersRenderingWindowingAndTextShaping()
	{
		var sink = Logger.Sink;

		try
		{
			var builder = App.BuildAvaloniaApp();

			builder.RenderingSubsystemInitializer.Should().NotBeNull();
			builder.WindowingSubsystemInitializer.Should().NotBeNull();
			builder.TextShapingSubsystemInitializer.Should().NotBeNull();
		}
		finally
		{
			Logger.Sink = sink;
		}
	}
}
