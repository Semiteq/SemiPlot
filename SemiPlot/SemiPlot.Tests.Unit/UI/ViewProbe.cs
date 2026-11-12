using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

namespace SemiPlot.Tests.Unit.UI;

internal static class ViewProbe
{
	public static TWindow Realise<TWindow>(TWindow window)
		where TWindow : Window
	{
		window.Show();
		Dispatcher.UIThread.RunJobs();

		return window;
	}

	public static void Dismiss(Window window)
	{
		window.Close();
		Dispatcher.UIThread.RunJobs();
	}

	/// <summary>Semi's text button padding, 12,0, and its 32 px height.</summary>
	public static void ShouldTakeTheThemesButtonInsets(Button button, string because)
	{
		button.Padding.Should().Be(new Thickness(12, 0), because);
		button.Bounds.Height.Should().Be(32, because);
	}

	public static T Named<T>(Control root, string name)
		where T : Control
	{
		return root.FindControl<T>(name) ?? throw new InvalidOperationException($"No control named {name}.");
	}

	// A template carries its own name scope, so a part of one is reached through the visual tree.
	public static T Descendant<T>(Visual root, string name)
		where T : Control
	{
		return root.GetVisualDescendants().OfType<T>().FirstOrDefault(child => child.Name == name)
			?? throw new InvalidOperationException($"{root.GetType().Name} realised no {name}.");
	}

	public static IEnumerable<T> Descendants<T>(Visual root)
		where T : Control
	{
		return root.GetVisualDescendants().OfType<T>();
	}

	// Avalonia keeps the open ToolTip behind an internal property; no public member reaches it.
	public static TextBlock OpenTooltipText(Control holder)
	{
		var property = typeof(ToolTip).GetField("ToolTipProperty", BindingFlags.NonPublic | BindingFlags.Static)
			?.GetValue(null) as AvaloniaProperty
			?? throw new InvalidOperationException("Avalonia no longer exposes ToolTip.ToolTipProperty.");

		ToolTip.SetIsOpen(holder, true);
		Dispatcher.UIThread.RunJobs();

		var tooltip = holder.GetValue(property) as ToolTip
			?? throw new InvalidOperationException($"'{holder.Name}' opened no tooltip.");

		return Descendants<TextBlock>(tooltip).Single();
	}

	/// <summary>Opens the holder's tooltip, finds its text laid out whole, and closes it again.</summary>
	public static void ShouldShowTheWholeTip(Control holder, string expected, string because)
	{
		var text = OpenTooltipText(holder);

		text.Text.Should().Be(expected, because);
		text.IsEffectivelyVisible.Should().BeTrue(because);
		text.UseLayoutRounding.Should().BeTrue(because);
		text.TextTrimming.Should().Be(TextTrimming.None, because);
		text.MaxLines.Should().Be(0, because);
		text.TextLayout.TextLines.Should().OnlyContain(line => !line.HasCollapsed, because);
		text.TextLayout.TextLines.Sum(line => line.Length).Should().BeGreaterThanOrEqualTo(expected.Length, because);
		text.Bounds.Width.Should().BeGreaterThanOrEqualTo(text.DesiredSize.Width, because);

		ToolTip.SetIsOpen(holder, false);
		Dispatcher.UIThread.RunJobs();
	}

	public static DataGridColumnHeader HeaderOf(DataGrid grid, string header)
	{
		return grid.GetVisualDescendants().OfType<DataGridColumnHeader>()
			.Single(candidate => Equals(candidate.Content, header));
	}

	/// <summary>The headers presenter's width less the vertical scroll bar's while that bar is visible.</summary>
	public static double ViewportOf(DataGrid grid)
	{
		var scrollBar = Descendant<ScrollBar>(grid, "PART_VerticalScrollbar");
		var scrollBarWidth = scrollBar.IsVisible ? scrollBar.Bounds.Width : 0;

		return Descendant<DataGridColumnHeadersPresenter>(grid, "PART_ColumnHeadersPresenter").Bounds.Width
			- scrollBarWidth;
	}

	public static Color? ColourOf(IBrush? brush)
	{
		return (brush as ISolidColorBrush)?.Color;
	}

	public static byte AlphaOf(IBrush? brush)
	{
		return brush switch
		{
			null => 0,
			ISolidColorBrush solid => (byte)(solid.Color.A * solid.Opacity),
			_ => throw new InvalidOperationException($"The control paints a {brush.GetType().Name}.")
		};
	}

	/// <summary>An icon button on Semi's InnerIconButton: the rendered glyph in one colour and no fill behind it.</summary>
	public static void ShouldPaintTheGlyphAlone(Button button, Color glyph, string state)
	{
		var icon = Descendants<PathIcon>(button).Single();

		AlphaOf(button.Background).Should().Be(0, "the button paints no fill {0}", state);
		AlphaOf(icon.Background).Should().Be(0, "the glyph paints no fill {0}", state);
		ColourOf(icon.Foreground).Should().Be(glyph, "the glyph {0}", state);
	}

	public static double LeftOf(Control control, Visual ancestor)
	{
		return control.TranslatePoint(default, ancestor)?.X
			?? throw new InvalidOperationException("The control is not in the ancestor's visual tree.");
	}

	public static double RightOf(Control control, Visual ancestor)
	{
		return LeftOf(control, ancestor) + control.Bounds.Width;
	}

	public static double TopOf(Control control, Visual ancestor)
	{
		return control.TranslatePoint(default, ancestor)?.Y
			?? throw new InvalidOperationException("The control is not in the ancestor's visual tree.");
	}

	public static double BottomOf(Control control, Visual ancestor)
	{
		return TopOf(control, ancestor) + control.Bounds.Height;
	}

	/// <summary>The control's desired width with no constraint, rounded up, and the window laid out again.</summary>
	public static double UnconstrainedWidthOf(Window window, Control control)
	{
		control.Measure(Size.Infinity);
		var width = Math.Ceiling(control.DesiredSize.Width);
		control.InvalidateMeasure();
		window.UpdateLayout();

		return width;
	}
}
