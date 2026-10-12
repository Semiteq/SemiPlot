using Avalonia.Controls;

using AwesomeAssertions;

using SemiPlot.UI.PenEditor;

namespace SemiPlot.Tests.Unit.UI.PenEditor;

/// <summary>Realises the editor window and reaches its controls, for the view tests of both tabs.</summary>
internal static class PenEditorWindowDriver
{
	public static PenEditorWindow Realise(PenEditorViewModel viewModel)
	{
		return ViewProbe.Realise(new PenEditorWindow { DataContext = viewModel });
	}

	// The window waits for the edits in progress and closes on a posted job.
	public static void Close(PenEditorWindow window)
	{
		ViewProbe.Dismiss(window);
		window.IsVisible.Should().BeFalse("the close request completes once the edits are written");
	}

	public static void Retype(PenEditorWindow window, TextBox field, string text)
	{
		HeadlessInput.Clear(window, field);
		HeadlessInput.Type(window, field, text);
	}

	public static bool IsOnScreen(Control control)
	{
		return control.IsEffectivelyVisible && TopLevel.GetTopLevel(control) is not null && control.Bounds.Height > 0;
	}

	public static Control RowAt(PenEditorWindow window, int index)
	{
		return ViewProbe.Named<ListBox>(window, "PenTable").ContainerFromIndex(index)
			?? throw new InvalidOperationException($"The table realised no row {index}.");
	}

	public static void OpenGroupsTab(PenEditorWindow window)
	{
		HeadlessInput.Click(window, ViewProbe.Named<TabItem>(window, "GroupsTab"));
	}

	public static Control GroupAt(PenEditorWindow window, int index)
	{
		return ViewProbe.Named<ListBox>(window, "GroupList").ContainerFromIndex(index)
			?? throw new InvalidOperationException($"The group list realised no row {index}.");
	}

	public static string? TextOf(Control row, string name)
	{
		return ViewProbe.Descendant<TextBlock>(row, name).Text;
	}
}
