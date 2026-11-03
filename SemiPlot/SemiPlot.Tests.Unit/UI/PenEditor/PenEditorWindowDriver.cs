using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using SemiPlot.Core.Data;
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

	public static DataGrid PenListOf(PenEditorWindow window)
	{
		return ViewProbe.Named<DataGrid>(window, "PenList");
	}

	public static DataGridRow RowAt(PenEditorWindow window, int index)
	{
		var list = PenListOf(window);
		if (RealisedRowAt(list, index) is { } realised)
		{
			return realised;
		}

		var view = list.ItemsSource as DataGridCollectionView
			?? throw new InvalidOperationException("The pen list binds no collection view.");
		list.ScrollIntoView(view[index], null);
		Dispatcher.UIThread.RunJobs();

		return RealisedRowAt(list, index)
			?? throw new InvalidOperationException($"The pen list realised no row {index}.");
	}

	public static DataGridColumnHeader HeaderOf(PenEditorWindow window, string header)
	{
		return PenListOf(window).GetVisualDescendants().OfType<DataGridColumnHeader>()
			.Single(candidate => Equals(candidate.Content, header));
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

	/// <summary>The realised rows' pens in list order.</summary>
	public static IEnumerable<StoredPen> RealisedPens(DataGrid list)
	{
		return RealisedRows(list)
			.OrderBy(row => row.Index)
			.Select(row => row.DataContext)
			.Cast<PenRowViewModel>()
			.Select(row => row.Pen);
	}

	public static IEnumerable<DataGridRow> RealisedRows(DataGrid list)
	{
		return list.GetVisualDescendants().OfType<DataGridRow>().Where(row => row.IsVisible && row.Index >= 0);
	}

	private static DataGridRow? RealisedRowAt(DataGrid list, int index)
	{
		return RealisedRows(list).FirstOrDefault(row => row.Index == index);
	}
}
