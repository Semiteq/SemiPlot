using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
}
