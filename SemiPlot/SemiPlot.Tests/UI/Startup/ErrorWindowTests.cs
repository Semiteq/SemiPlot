using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.UI.Startup;

// The window is the only thing a failed startup draws, so the check is that all three strings of the
// state reach it. Constructing a Window needs the Avalonia application, hence [AvaloniaFact].
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class ErrorWindowTests
{
	private static readonly StartupFailureView _failure = new(
		"No connection to the archive",
		"SemiPlot could not open a connection to 'semiplot' at scada-host:5432.",
		"Check that the PostgreSQL server is running.");

	[AvaloniaFact]
	public void Window_ShowsTitleDetailAndRemedy()
	{
		var window = new ErrorWindow(_failure);
		window.Show();

		ReadText(window, "TitleText").Should().Be(_failure.Title);
		ReadText(window, "DetailText").Should().Be(_failure.Detail);
		ReadText(window, "RemedyText").Should().Be(_failure.Remedy);
	}

	[AvaloniaFact]
	public void Window_ResolvesNoService()
	{
		// It opens exactly when the container could not be brought up, so it must not need one.
		var window = new ErrorWindow(_failure);

		window.DataContext.Should().BeSameAs(_failure);
	}

	private static string? ReadText(ErrorWindow window, string name)
	{
		return window.FindControl<TextBlock>(name)?.Text;
	}
}
