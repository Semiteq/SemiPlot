using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
		var combo = new ComboBox { ItemsSource = new[] { "first", "second" }, SelectedIndex = 0 };
		var picker = new ColorPicker();
		var window = new Window { Content = Stacked(button, box, check, combo, picker) };
		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();

			var palette = new CornerRadius(4);
			button.CornerRadius.Should().Be(palette);
			NamedBorder(box, "PART_ContentPresenterBorder").CornerRadius.Should().Be(palette);
			NamedBorder(check, "NormalRectangle").CornerRadius.Should().Be(palette);
			NamedBorder(combo, "Background").CornerRadius.Should().Be(palette);
			NamedBorder(picker, "PART_Background").CornerRadius.Should().Be(palette);
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
		var mixed = new CheckBox { IsChecked = null };
		var scroller = ScrollerWithAThumb();
		var window = new Window { Content = Stacked(toggleOff, toggleOn, check, mixed, scroller) };
		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();

			ColourOf(toggleOff.Foreground).Should().Be(Color.Parse(Accent));
			ColourOf(toggleOn.Background).Should().Be(Color.Parse(Accent));
			ColourOf(toggleOn.BorderBrush).Should().Be(Color.Parse(Accent));
			ColourOf(NamedBorder(check, "NormalRectangle").BorderBrush).Should().Be(Color.Parse(border));
			ColourOf(NamedBorder(mixed, "NormalRectangle").Background).Should().Be(Color.Parse(Accent));
			ColourOf(NamedBorder(mixed, "NormalRectangle").BorderBrush).Should().Be(Color.Parse(Accent));
			ColourOf(ThumbOf(scroller)).Should().Be(Color.Parse(thumb));
		}
		finally
		{
			window.Close();
		}
	}

	// The menu strip, its open submenu and its separator, read back off a realised menu:
	// docs/architecture/ui-theme.md, How the retint reaches a control.
	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light, "#000000", "#F7F8FA", "#EBECF0")]
	[InlineData(AppThemeVariant.Dark, "#DFE1E5", "#2B2D30", "#393B40")]
	public void EveryMenuSurface_PaintsItselfFromThePalette(
		AppThemeVariant theme, string text, string flyoutBackground, string border)
	{
		using var scope = ThemeProbe.ApplyVariant(App.VariantFor(theme));
		Dispatcher.UIThread.RunJobs();

		var leaf = new MenuItem { Header = "leaf" };
		var separator = new Separator();
		var checkable = new MenuItem
		{
			Header = "checkable",
			ToggleType = MenuItemToggleType.CheckBox,
			IsChecked = true
		};
		var top = new MenuItem { Header = "top" };
		top.Items.Add(leaf);
		top.Items.Add(separator);
		top.Items.Add(checkable);
		var menu = new Menu();
		menu.Items.Add(top);
		var items = new ItemsControl { ItemsSource = new[] { "row" } };
		var window = new Window { Content = Stacked(menu, items) };
		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();

			ColourOf(TextOf(top)).Should().Be(Color.Parse(text), "the resting caption");

			top.Open();
			Dispatcher.UIThread.RunJobs();

			// An open top-level item takes MenuItemPointeroverForeground, which Semi ships brighter than its
			// own text colour, so the palette has to carry that key too.
			ColourOf(TextOf(top)).Should().Be(Color.Parse(text), "the open caption");
			ColourOf(TextOf(leaf)).Should().Be(Color.Parse(text));
			ColourOf(TextOf(checkable)).Should().Be(Color.Parse(text));
			ColourOf(CheckGlyphOf(checkable)).Should().Be(Color.Parse(text));
			ColourOf(SeparatorFillOf(separator)).Should().Be(Color.Parse(border));
			ColourOf(FlyoutBorderOf(top).Background).Should().Be(Color.Parse(flyoutBackground));
			ColourOf(FlyoutBorderOf(top).BorderBrush).Should().Be(Color.Parse(border));
			ColourOf(TextOf(items)).Should().Be(Color.Parse(text), "an ItemsControl row inherits the text colour");
		}
		finally
		{
			top.Close();
			window.Close();
		}
	}

	// The pen editor's tabs and table, read back off realised controls in each state the window reaches:
	// docs/architecture/ui-theme.md, How the retint reaches a control.
	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light, "#000000", "#818594", "#EBECF0")]
	[InlineData(AppThemeVariant.Dark, "#DFE1E5", "#6F737A", "#393B40")]
	public void EveryTabAndListSurface_PaintsItselfFromThePalette(
		AppThemeVariant theme, string text, string secondaryText, string border)
	{
		using var scope = ThemeProbe.ApplyVariant(App.VariantFor(theme));
		Dispatcher.UIThread.RunJobs();

		var selectedTab = new TabItem { Header = "selected", Content = new TextBlock { Text = "content" } };
		var otherTab = new TabItem { Header = "other" };
		var tabs = new TabControl { ItemsSource = new[] { selectedTab, otherTab } };
		var list = new ListBox { ItemsSource = new[] { "selected", "hovered", "pressed" }, SelectedIndex = 0 };
		var window = new Window { Content = Stacked(tabs, list) };
		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();
			var selectedRow = list.ContainerFromIndex(0).Should().BeOfType<ListBoxItem>().Subject;

			ColourOf(TextOf(selectedTab)).Should().Be(Color.Parse(text), "the selected tab's caption");
			ColourOf(TextOf(otherTab)).Should().Be(Color.Parse(secondaryText), "a tab at rest");
			ColourOf(NamedBorder(selectedTab, "PART_RootBorder").BorderBrush).Should().Be(Color.Parse(Accent));
			ColourOf(NamedBorder(tabs, "PART_BorderSeparator").Background).Should().Be(Color.Parse(border));
			ColourOf(TextOf(selectedRow)).Should().Be(Color.Parse(text), "the selected row");
			var selection = selectedRow.Background.Should().BeAssignableTo<ISolidColorBrush>().Subject;
			selection.Color.Should().Be(Color.Parse(Accent));
			selection.Opacity.Should().Be(0.25);

			PointAt(window, otherTab);
			ColourOf(TextOf(otherTab)).Should().Be(Color.Parse(text), "a hovered tab");

			PointAt(window, list.ContainerFromIndex(1)!);
			ColourOf(TextOf(list.ContainerFromIndex(1)!)).Should().Be(Color.Parse(text), "a hovered row");

			var pressedRow = list.ContainerFromIndex(2)!;
			var pressedAt = PointAt(window, pressedRow);
			window.MouseDown(pressedAt, MouseButton.Left);
			Dispatcher.UIThread.RunJobs();
			ColourOf(TextOf(pressedRow)).Should().Be(Color.Parse(text), "a pressed row");
			window.MouseUp(pressedAt, MouseButton.Left);
		}
		finally
		{
			window.Close();
		}
	}

	// The pen form's line-style list and "on start" box, open, hovered and disabled.
	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light, "#000000", "#A8ADBD", "#F7F8FA", "#EBECF0")]
	[InlineData(AppThemeVariant.Dark, "#DFE1E5", "#5A5D63", "#2B2D30", "#393B40")]
	public void EveryComboBoxSurface_PaintsItselfFromThePalette(
		AppThemeVariant theme, string text, string disabledText, string flyoutBackground, string border)
	{
		using var scope = ThemeProbe.ApplyVariant(App.VariantFor(theme));
		Dispatcher.UIThread.RunJobs();

		var combo = new ComboBox { ItemsSource = new[] { "selected", "hovered" }, SelectedIndex = 0 };
		var dimCombo = new ComboBox { ItemsSource = new[] { "item" }, SelectedIndex = 0, IsEnabled = false };
		var dimCheck = new CheckBox { IsEnabled = false };
		var window = new Window { Content = Stacked(combo, dimCombo, dimCheck) };
		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();

			ColourOf(TextOf(dimCombo)).Should().Be(Color.Parse(disabledText), "a disabled list's value");
			ColourOf(NamedBorder(dimCheck, "NormalRectangle").BorderBrush).Should().Be(Color.Parse(border));

			PointAt(window, combo);
			ColourOf(combo.GetVisualDescendants().OfType<PathIcon>().First().Foreground).Should()
				.Be(Color.Parse(text), "the hovered list's arrow");

			combo.IsDropDownOpen = true;
			Dispatcher.UIThread.RunJobs();
			var popup = combo.GetVisualDescendants().OfType<Popup>().Single().Child.Should()
				.BeAssignableTo<Border>().Subject;
			var selectedItem = combo.ContainerFromIndex(0)!;
			var hoveredItem = combo.ContainerFromIndex(1)!;
			PointAt(window, hoveredItem);

			ColourOf(combo.BorderBrush).Should().Be(Color.Parse(Accent), "the open list's border");
			ColourOf(popup.Background).Should().Be(Color.Parse(flyoutBackground));
			ColourOf(popup.BorderBrush).Should().Be(Color.Parse(border));
			ColourOf(TextOf(selectedItem)).Should().Be(Color.Parse(text), "the selected item");
			ColourOf(TextOf(hoveredItem)).Should().Be(Color.Parse(text), "a hovered item");
			var selection = selectedItem.GetVisualDescendants().OfType<ContentPresenter>().First().Background
				.Should().BeAssignableTo<ISolidColorBrush>().Subject;
			selection.Color.Should().Be(Color.Parse(Accent));
			selection.Opacity.Should().Be(0.25);
		}
		finally
		{
			combo.IsDropDownOpen = false;
			window.Close();
		}
	}

	// The groups tab's membership boxes: checked while hovered or pressed, pressed unchecked, and checked while
	// the command writing it runs and disables it.
	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light, "#A8ADBD")]
	[InlineData(AppThemeVariant.Dark, "#5A5D63")]
	public void EveryCheckBoxStateAMembershipBoxReaches_PaintsItselfFromThePalette(
		AppThemeVariant theme, string disabled)
	{
		using var scope = ThemeProbe.ApplyVariant(App.VariantFor(theme));
		Dispatcher.UIThread.RunJobs();

		var hoveredChecked = new CheckBox { IsChecked = true, Content = "hovered" };
		var pressedChecked = new CheckBox { IsChecked = true, Content = "pressed" };
		var pressedUnchecked = new CheckBox { IsChecked = false, Content = "pressed" };
		var dimChecked = new CheckBox { IsChecked = true, IsEnabled = false, Content = "writing" };
		var window = new Window { Content = Stacked(hoveredChecked, pressedChecked, pressedUnchecked, dimChecked) };
		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();

			ColourOf(NamedBorder(dimChecked, "NormalRectangle").Background).Should().Be(Color.Parse(disabled));
			ColourOf(NamedBorder(dimChecked, "NormalRectangle").BorderBrush).Should().Be(Color.Parse(disabled));

			PointAt(window, hoveredChecked);
			ColourOf(NamedBorder(hoveredChecked, "NormalRectangle").Background).Should().Be(Color.Parse(Accent));
			ColourOf(NamedBorder(hoveredChecked, "NormalRectangle").BorderBrush).Should().Be(Color.Parse(Accent));

			var box = Press(window, pressedChecked);
			ColourOf(box.Background).Should().Be(Color.Parse(Accent), "a pressed checked box");
			ColourOf(box.BorderBrush).Should().Be(Color.Parse(Accent));
			Release(window, pressedChecked);

			box = Press(window, pressedUnchecked);
			ColourOf(box.BorderBrush).Should().Be(Color.Parse(Accent), "a pressed unchecked box");
			Release(window, pressedUnchecked);
		}
		finally
		{
			window.Close();
		}
	}

	// The pen form's colour picker with its flyout open on the spectrum and on the components tab.
	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light, "#000000", "#F7F8FA", "#EBECF0", "#FFFFFF")]
	[InlineData(AppThemeVariant.Dark, "#DFE1E5", "#2B2D30", "#393B40", "#1E1F22")]
	public void EveryColourPickerSurface_PaintsItselfFromThePalette(
		AppThemeVariant theme, string text, string flyoutBackground, string border, string ground)
	{
		using var scope = ThemeProbe.ApplyVariant(App.VariantFor(theme));
		Dispatcher.UIThread.RunJobs();

		var picker = new ColorPicker { Color = Colors.SteelBlue, IsAlphaEnabled = false, IsAlphaVisible = false };
		var window = new Window { Content = Stacked(picker), Width = 800, Height = 600 };
		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();
			var flyout = picker.GetVisualDescendants().OfType<DropDownButton>().Single().Flyout.Should()
				.BeOfType<Flyout>().Subject;

			HeadlessInput.Click(window, picker);
			var content = flyout.Content.Should().BeAssignableTo<Control>().Subject;
			var presenter = content.GetVisualAncestors().OfType<FlyoutPresenter>().First();
			var tabs = content.GetVisualDescendants().OfType<TabItem>().ToList();

			ColourOf(presenter.Background).Should().Be(Color.Parse(flyoutBackground));
			ColourOf(presenter.BorderBrush).Should().Be(Color.Parse(border));
			ColourOf(presenter.Foreground).Should().Be(Color.Parse(text));
			var spectrumEdge = content.GetVisualDescendants()
				.OfType<Rectangle>()
				.Single(edge => edge.Name == "BorderRectangle");
			ColourOf(spectrumEdge.Stroke).Should().Be(Color.Parse(border), "the spectrum's edge");
			ColourOf(tabs[0].GetVisualDescendants().OfType<PathIcon>().First().Foreground).Should()
				.Be(Color.Parse(Accent), "the selected tab's icon");

			HeadlessInput.Click(window, tabs[^1]);
			var modes = content.GetVisualDescendants()
				.OfType<RadioButton>()
				.Where(mode => mode.IsEffectivelyVisible)
				.ToList();
			var checkedMode = modes.Single(mode => mode.IsChecked == true);
			var otherMode = modes.First(mode => mode.IsChecked != true);

			ColourOf(checkedMode.Background).Should().Be(Color.Parse(Accent), "the chosen colour model");
			ColourOf(otherMode.Foreground).Should().Be(Color.Parse(Accent), "the other colour model's caption");
			ColourOf(otherMode.Background).Should().Be(Color.Parse(ground));
		}
		finally
		{
			window.Close();
		}
	}

	[AvaloniaTheory]
	[InlineData("AppAccentFillBrush")]
	[InlineData("ListBoxItemSelectedBackground")]
	[InlineData("ListBoxItemSelectedPointeroverBackground")]
	[InlineData("ComboBoxItemSelectedBackground")]
	public void APaletteKeyCarryingOpacity_KeepsItUnderBothVariants(string key)
	{
		const double Opacity = 0.25;

		foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
		{
			ThemeProbe.Brush(key, variant).Opacity.Should().Be(Opacity);
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

	private static Point PointAt(Window window, Control control)
	{
		var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
			?? throw new InvalidOperationException("The control is not in the window's visual tree.");
		window.MouseMove(center, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();

		return center;
	}

	private static Border Press(Window window, CheckBox check)
	{
		window.MouseDown(PointAt(window, check), MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		return NamedBorder(check, "NormalRectangle");
	}

	private static void Release(Window window, CheckBox check)
	{
		window.MouseUp(PointAt(window, check), MouseButton.Left);
		Dispatcher.UIThread.RunJobs();
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

	// The separator and the flyout chrome are unnamed template parts, so each is found by its own shape.
	private static IBrush? SeparatorFillOf(Separator separator)
	{
		return separator.GetSelfAndVisualDescendants()
			.OfType<Border>()
			.Select(border => border.Background)
			.First(brush => brush is ISolidColorBrush { Color.A: > 0 });
	}

	private static Border FlyoutBorderOf(MenuItem item)
	{
		return item.GetVisualDescendants()
			.OfType<Popup>()
			.Select(popup => popup.Child)
			.OfType<Visual>()
			.SelectMany(child => child.GetSelfAndVisualDescendants().OfType<Border>())
			.First(border => border.Background is ISolidColorBrush { Color.A: 255 });
	}

	private static IBrush? CheckGlyphOf(MenuItem item)
	{
		return item.GetVisualDescendants().OfType<Shape>().First(shape => shape.Fill is not null).Fill;
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
