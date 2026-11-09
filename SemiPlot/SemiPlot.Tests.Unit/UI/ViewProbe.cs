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
