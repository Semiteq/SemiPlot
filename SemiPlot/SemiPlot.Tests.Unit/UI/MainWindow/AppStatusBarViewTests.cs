using System.Reactive.Subjects;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;

using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.UI;
using SemiPlot.UI.Localization;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Startup;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

/// <summary>The realised bar: the indicator's class, its palette brush, its command, and the layer caption.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class AppStatusBarViewTests
{
	private static readonly ArchiveConnectionState _lost =
		new(new ArchiveError(ArchiveFault.ConnectionLost, "bench", 5432, "semiplot_dev", "3"));

	[AvaloniaFact]
	public void ConnectionIndicator_CarriesTheStateClassAndOpensThePanel()
	{
		using var panel = new MessagePanelViewModel();
		using var statusBar = NewStatusBar(panel);
		using var states = new Subject<ArchiveConnectionState>();
		statusBar.TrackArchiveConnection(states);
		var view = new AppStatusBar { DataContext = statusBar };
		var window = new Window { Content = view };
		window.Show();
		var indicator = view.FindControl<Button>("ConnectionIndicator");

		indicator.Should().NotBeNull();
		indicator!.Classes.Should().Contain("connection-ok");
		indicator.Content.Should().Be(Resources.StatusConnectionOk);

		states.OnNext(_lost);
		Dispatcher.UIThread.RunJobs();

		indicator.Classes.Should().Contain("connection-fault");
		indicator.Classes.Should().NotContain("connection-ok");
		indicator.Content.Should().Be(
			Resources.StatusConnectionFault, "the caption follows the state, not only the class");

		panel.IsVisible.Should().BeTrue();
		indicator.Command!.Execute(null);
		Dispatcher.UIThread.RunJobs();

		panel.IsVisible.Should().BeFalse("the indicator invokes the panel's own toggle");
	}

	// The state class only proves a class was set; the brush behind it is what the operator sees, and a
	// dropped or misspelled palette key stays green until a realised control is read back
	// (docs/architecture/ui-theme.md, How the retint reaches a control).
	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light)]
	[InlineData(AppThemeVariant.Dark)]
	public void ConnectionIndicator_PaintsBothStatesFromThePalette(AppThemeVariant theme)
	{
		var variant = App.VariantFor(theme);
		using var scope = ThemeProbe.ApplyVariant(variant);
		using var panel = new MessagePanelViewModel();
		using var statusBar = NewStatusBar(panel);
		using var states = new Subject<ArchiveConnectionState>();
		statusBar.TrackArchiveConnection(states);
		var view = new AppStatusBar { DataContext = statusBar };
		var window = new Window { Content = view };
		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();
			var indicator = view.FindControl<Button>("ConnectionIndicator");

			indicator.Should().NotBeNull();
			ColourOf(indicator!.Foreground).Should().Be(ThemeProbe.Colour("AppConnectionOkBrush", variant));

			states.OnNext(_lost);
			Dispatcher.UIThread.RunJobs();

			ColourOf(indicator.Foreground).Should().Be(ThemeProbe.Colour("AppConnectionFaultBrush", variant));
		}
		finally
		{
			window.Close();
		}
	}

	// Read off the realised TextBlock, so dropping LayerText's change notification freezes the bar at the
	// launch layer and turns this red while the view model's own property still moves.
	[AvaloniaFact]
	public void ActiveLayerText_FollowsTheChartsLayerOnTheRealisedBar()
	{
		using var panel = new MessagePanelViewModel();
		using var statusBar = NewStatusBar(panel);
		var navigation = NavigationAtRawLayer();
		statusBar.TrackLayer(navigation);
		var view = new AppStatusBar { DataContext = statusBar };
		var window = new Window { Content = view };
		window.Show();
		Dispatcher.UIThread.RunJobs();
		var layerText = view.FindControl<TextBlock>("ActiveLayerText");

		layerText.Should().NotBeNull();
		layerText!.Text.Should().Be(Resources.FormatStatusLayerFormat(Resources.StatusLayerRaw));

		DriveToLayer(navigation, AggregationLayer.Hour);
		Dispatcher.UIThread.RunJobs();

		layerText.Text.Should().Be(Resources.FormatStatusLayerFormat(Resources.StatusLayerHour));
	}

	private static Color ColourOf(IBrush? brush)
	{
		return brush.Should().BeAssignableTo<ISolidColorBrush>().Subject.Color;
	}
}
