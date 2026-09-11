using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using SemiPlot.UI;
using SemiPlot.UI.Startup;

using Xunit;

using MainWindowView = SemiPlot.UI.MainWindow.MainWindow;

namespace SemiPlot.Tests.Unit.UI;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ThemeTests
{
	private const string Accent = "#3574F0";

	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light)]
	[InlineData(AppThemeVariant.Dark)]
	public void TheApplicationVariant_FollowsTheSettings(AppThemeVariant theme)
	{
		using var scope = ThemeProbe.ApplyVariant(App.VariantFor(theme));

		var expected = theme == AppThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
		Application.Current!.ActualThemeVariant.Should().Be(expected);
	}

	// Reading a real control back is the only thing that catches an inert override:
	// docs/architecture/ui-theme.md, How the retint reaches a control.
	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light, "#000000", "#818594", "#A8ADBD", "#FFFFFF")]
	[InlineData(AppThemeVariant.Dark, "#DFE1E5", "#6F737A", "#5A5D63", "#1E1F22")]
	public void EverySemiControl_PaintsItselfFromThePalette(
		AppThemeVariant theme, string text, string secondaryText, string disabledText, string windowBackground)
	{
		using var scope = ThemeProbe.ApplyVariant(App.VariantFor(theme));
		Dispatcher.UIThread.RunJobs();

		var label = new TextBlock { Text = "label" };
		var dimLabel = new TextBlock { Text = "label", IsEnabled = false };
		var button = new Button { Content = "button" };
		var dimButton = new Button { Content = "button", IsEnabled = false };
		var box = new TextBox { PlaceholderText = "hint" };
		var check = new CheckBox { IsChecked = true };
		var window = new Window { Content = Stacked(label, dimLabel, button, dimButton, box, check) };
		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();
			box.Focus();
			Dispatcher.UIThread.RunJobs();

			ColourOf(label.Foreground).Should().Be(Color.Parse(text));
			ColourOf(box.Foreground).Should().Be(Color.Parse(text));
			ColourOf(check.Foreground).Should().Be(Color.Parse(text));
			ColourOf(window.Foreground).Should().Be(Color.Parse(text));
			ColourOf(window.Background).Should().Be(Color.Parse(windowBackground));
			ColourOf(button.Foreground).Should().Be(Color.Parse(Accent));

			ColourOf(TextOf(dimLabel)).Should().Be(Color.Parse(disabledText));
			ColourOf(TextOf(dimButton)).Should().Be(Color.Parse(disabledText));
			ColourOf(PlaceholderOf(box)).Should().Be(Color.Parse(secondaryText));
			ColourOf(FocusBorderOf(box)).Should().Be(Color.Parse(Accent));
			ColourOf(CheckedBoxOf(check)).Should().Be(Color.Parse(Accent));
		}
		finally
		{
			window.Close();
		}
	}

	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light)]
	[InlineData(AppThemeVariant.Dark)]
	public void EveryCornerRadius_ComesFromThePalette(AppThemeVariant theme)
	{
		using var scope = ThemeProbe.ApplyVariant(App.VariantFor(theme));
		Dispatcher.UIThread.RunJobs();

		var button = new Button { Content = "button" };
		var box = new TextBox();
		var check = new CheckBox();
		var window = new Window { Content = Stacked(button, box, check) };
		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();

			var palette = new CornerRadius(4);
			button.CornerRadius.Should().Be(palette);
			NamedBorder(box, "PART_ContentPresenterBorder").CornerRadius.Should().Be(palette);
			NamedBorder(check, "NormalRectangle").CornerRadius.Should().Be(palette);
		}
		finally
		{
			window.Close();
		}
	}

	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light, "#EBECF0", "#A8ADBD")]
	[InlineData(AppThemeVariant.Dark, "#393B40", "#5A5D63")]
	public void EveryToggleAndScrollSurface_PaintsItselfFromThePalette(
		AppThemeVariant theme, string border, string thumb)
	{
		using var scope = ThemeProbe.ApplyVariant(App.VariantFor(theme));
		Dispatcher.UIThread.RunJobs();

		var toggleOff = new ToggleButton { Content = "toggle", IsChecked = false };
		var toggleOn = new ToggleButton { Content = "toggle", IsChecked = true };
		var check = new CheckBox { IsChecked = false };
		var scroller = ScrollerWithAThumb();
		var window = new Window { Content = Stacked(toggleOff, toggleOn, check, scroller) };
		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();

			ColourOf(toggleOff.Foreground).Should().Be(Color.Parse(Accent));
			ColourOf(toggleOn.Background).Should().Be(Color.Parse(Accent));
			ColourOf(toggleOn.BorderBrush).Should().Be(Color.Parse(Accent));
			ColourOf(NamedBorder(check, "NormalRectangle").BorderBrush).Should().Be(Color.Parse(border));
			ColourOf(ThumbOf(scroller)).Should().Be(Color.Parse(thumb));
		}
		finally
		{
			window.Close();
		}
	}

	[AvaloniaFact]
	public void APaletteKeyCarryingOpacity_KeepsItUnderBothVariants()
	{
		const string Key = "AppAccentFillBrush";
		const double Opacity = 0.25;

		foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
		{
			ThemeProbe.Brush(Key, variant).Opacity.Should().Be(Opacity);
		}
	}

	[AvaloniaFact]
	public void TheChartBorderAndTheMinimapStrip_TakeTheirBrushFromTheVariant()
	{
		using var scope = ThemeProbe.PreserveVariant();
		var application = Application.Current!;
		var window = new MainWindowView();
		try
		{
			window.Show();

			var chartBorder = window.GetVisualDescendants()
				.OfType<Border>()
				.Single(border => border.Name == "ChartContent");
			var strip = window.GetVisualDescendants()
				.OfType<Canvas>()
				.Single(canvas => canvas.Name == "StripCanvas");

			var light = SurfacesUnder(application, ThemeVariant.Light, chartBorder, strip);
			var dark = SurfacesUnder(application, ThemeVariant.Dark, chartBorder, strip);

			light.Chart.Should().NotBe(dark.Chart);
			light.Strip.Should().NotBe(dark.Strip);
			light.Chart.Should().Be(ThemeProbe.Colour("AppContentBackgroundBrush", ThemeVariant.Light));
			dark.Chart.Should().Be(ThemeProbe.Colour("AppContentBackgroundBrush", ThemeVariant.Dark));
			light.Strip.Should().Be(ThemeProbe.Colour("AppContentBackgroundBrush", ThemeVariant.Light));
			dark.Strip.Should().Be(ThemeProbe.Colour("AppContentBackgroundBrush", ThemeVariant.Dark));
		}
		finally
		{
			window.Close();
		}
	}

	private static StackPanel Stacked(params Control[] children)
	{
		var panel = new StackPanel();
		foreach (var child in children)
		{
			panel.Children.Add(child);
		}

		return panel;
	}

	private static ScrollViewer ScrollerWithAThumb()
	{
		return new ScrollViewer
		{
			Height = 20,
			VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
			Content = Stacked(
				new TextBlock { Text = "row", Height = 100 },
				new TextBlock { Text = "row", Height = 100 })
		};
	}

	private static Border NamedBorder(Control control, string name)
	{
		return control.GetVisualDescendants().OfType<Border>().Single(border => border.Name == name);
	}

	private static IBrush? ThumbOf(ScrollViewer scroller)
	{
		return scroller.GetVisualDescendants()
			.OfType<Thumb>()
			.SelectMany(thumb => thumb.GetVisualDescendants().OfType<Border>())
			.Select(border => border.Background)
			.First(brush => brush is ISolidColorBrush { Color.A: > 0 });
	}

	private static IBrush? TextOf(Control control)
	{
		return control.GetSelfAndVisualDescendants()
			.OfType<TextBlock>()
			.Select(text => text.Foreground)
			.First(foreground => foreground is not null);
	}

	private static IBrush? PlaceholderOf(TextBox box)
	{
		return box.GetVisualDescendants()
			.OfType<TextBlock>()
			.Single(text => text.Text == box.PlaceholderText)
			.Foreground;
	}

	private static IBrush? FocusBorderOf(TextBox box)
	{
		return box.GetVisualDescendants()
			.OfType<Border>()
			.Select(border => border.BorderBrush)
			.First(brush => brush is ISolidColorBrush { Color.A: > 0 });
	}

	private static IBrush? CheckedBoxOf(CheckBox check)
	{
		return check.GetVisualDescendants()
			.OfType<Border>()
			.Select(border => border.Background)
			.First(brush => brush is ISolidColorBrush { Color.A: > 0 });
	}

	private static (Color Chart, Color Strip) SurfacesUnder(
		Application application, ThemeVariant variant, Border chartBorder, Canvas strip)
	{
		application.RequestedThemeVariant = variant;
		Dispatcher.UIThread.RunJobs();

		return (ColourOf(chartBorder.Background), ColourOf(strip.Background));
	}

	private static Color ColourOf(IBrush? brush)
	{
		return brush.Should().BeAssignableTo<ISolidColorBrush>().Subject.Color;
	}
}
