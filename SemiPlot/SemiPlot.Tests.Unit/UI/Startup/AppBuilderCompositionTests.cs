using Avalonia.Logging;

using AwesomeAssertions;

using SemiPlot.UI;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Startup;

/// <summary>
/// The one place that holds <see cref="App.BuildAvaloniaApp"/> on Win32, Skia and a text shaper: Avalonia 12
/// stopped bringing a shaper along with Skia, and the target framework no longer marks the shipped backend.
/// <c>LogToTrace()</c> writes <c>Logger.Sink</c>, so the sink is restored around the call.
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
