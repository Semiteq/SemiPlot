using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using SemiPlot.UI.PenEditor;

namespace SemiPlot.Tests.Unit.UI.PenEditor;

/// <summary>Realises the editor window and reaches its controls, for the view tests of both tabs.</summary>
internal static class PenEditorWindowDriver
{
	public static PenEditorWindow Realise(PenEditorViewModel viewModel)
	{
		var window = new PenEditorWindow { DataContext = viewModel };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		return window;
	}

	// The window waits for the edits in progress and closes on a posted job.
	public static void Close(PenEditorWindow window)
	{
		window.Close();
		Dispatcher.UIThread.RunJobs();
		window.IsVisible.Should().BeFalse("the close request completes once the edits are written");
	}

	public static void Retype(PenEditorWindow window, TextBox field, string text)
	{
		HeadlessInput.Clear(window, field);
		HeadlessInput.Type(window, field, text);
	}

	public static T Named<T>(PenEditorWindow window, string name)
		where T : Control
	{
		return window.FindControl<T>(name) ?? throw new InvalidOperationException($"No control named {name}.");
	}

	public static bool IsOnScreen(Control control)
	{
		return control.IsEffectivelyVisible && TopLevel.GetTopLevel(control) is not null && control.Bounds.Height > 0;
	}

	public static Control RowAt(PenEditorWindow window, int index)
	{
		return Named<ListBox>(window, "PenTable").ContainerFromIndex(index)
			?? throw new InvalidOperationException($"The table realised no row {index}.");
	}

	public static void OpenGroupsTab(PenEditorWindow window)
	{
		HeadlessInput.Click(window, Named<TabItem>(window, "GroupsTab"));
	}

	public static Control GroupAt(PenEditorWindow window, int index)
	{
		return Named<ListBox>(window, "GroupList").ContainerFromIndex(index)
			?? throw new InvalidOperationException($"The group list realised no row {index}.");
	}

	// A row template carries its own name scope, so a cell is reached through the visual tree.
	public static T Cell<T>(Control row, string name)
		where T : Control
	{
		return row.GetVisualDescendants().OfType<T>().FirstOrDefault(cell => cell.Name == name)
			?? throw new InvalidOperationException($"The row realised no {name}.");
	}

	public static string? TextOf(Control row, string name)
	{
		return Cell<TextBlock>(row, name).Text;
	}
}
