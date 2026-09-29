using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.PenEditor;

/// <summary>
/// The groups tab's one route to the editor, shared by the tab, its groups and their membership entries.
/// </summary>
internal sealed class PenGroupWriter(
	IPenCatalogueEditor penCatalogueEditor,
	EditorCallQueue editorCallQueue,
	MessagePanelViewModel messagePanel,
	ILogger logger,
	Action<IReadOnlyCollection<int>> showGroupsOn) : ReactiveObject
{
	private readonly IPenCatalogueEditor _penCatalogueEditor = penCatalogueEditor;
	private readonly EditorCallQueue _editorCallQueue = editorCallQueue;
	private readonly MessagePanelViewModel _messagePanel = messagePanel;
	private readonly ILogger _logger = logger;
	private readonly Action<IReadOnlyCollection<int>> _showGroupsOn = showGroupsOn;

	/// <summary>The title of the last failed tab write, until a later tab write succeeds.</summary>
	public string Refusal
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = string.Empty;

	/// <summary>Runs the call through the editor's queue; a success clears the last refusal.</summary>
	public async Task<TResult> RunAsync<TResult>(Func<IPenCatalogueEditor, Task<TResult>> call)
		where TResult : IResultBase
	{
		var result = await _editorCallQueue.RunAsync(() => call(_penCatalogueEditor));

		if (result.IsSuccess)
		{
			Refusal = string.Empty;
		}

		return result;
	}

	public void ReportFailure(IResultBase failed)
	{
		_messagePanel.ReportFailure(failed, _logger);
	}

	/// <summary>Shows a failed tab write on the tab's message line and in the message panel.</summary>
	public void RefuseWrite(IResultBase failed)
	{
		Refusal = ArchiveFailureMapper.Map(failed.Errors[0]).Title;
		ReportFailure(failed);
	}

	/// <summary>Renames the groups text of the rows of these pens after a group write the editor has made.</summary>
	public void ShowGroupsOn(IReadOnlyCollection<int> penIds)
	{
		_showGroupsOn(penIds);
	}
}
