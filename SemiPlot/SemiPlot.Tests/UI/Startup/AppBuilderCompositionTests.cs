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
/// delegate, so reading the subsystems back initialises no platform. <c>LogToTrace()</c> is the
/// exception — it writes <c>Logger.Sink</c> when called — so the sink is restored around the call and the
/// process is left as it was found.
/// </para>
/// <para>
/// Windowing and rendering are asserted by name because the projects target plain <c>net10.0</c>: the
/// target framework no longer marks the shipped backend, so this is the only place that holds the
/// application on Win32 and Skia. <c>UseWindowingSubsystem</c> and <c>UseRenderingSubsystem</c> write the
/// initializer and the name in one call, so the name covers both.
/// </para>
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class AppBuilderCompositionTests
{
	[Fact]
	public void BuildAvaloniaApp_RegistersWin32SkiaAndTextShaping()
	{
		var sink = Logger.Sink;

		try
		{
			var builder = App.BuildAvaloniaApp();

			builder.TextShapingSubsystemInitializer.Should().NotBeNull();

			builder.WindowingSubsystemName.Should().Be("Win32");
			builder.RenderingSubsystemName.Should().Be("Skia");
		}
		finally
		{
			Logger.Sink = sink;
		}
	}
}
