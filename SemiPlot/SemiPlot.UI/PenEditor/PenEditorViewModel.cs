using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia.Collections;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.PenEditor;

/// <summary>
/// The pen and group editor window's root: the pen list, the form of the selected pen, the groups pane and
/// refresh. Every editor call runs through the one queue it owns.
/// </summary>
public sealed class PenEditorViewModel : ReactiveObject, IDisposable
{
	private readonly IPenCatalogueEditor _penCatalogueEditor;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly ILogger<PenEditorViewModel> _logger;
	private readonly ArchiveCallQueue _archiveCallQueue;
	private readonly CompositeDisposable _disposables = [];
	private readonly ObservableCollection<PenRowViewModel> _rows;
	private readonly HashSet<PenRowViewModel> _matchingRows = [];
	private readonly ObservableAsPropertyHelper<PenFormViewModel?> _selectedForm;
	private readonly ObservableAsPropertyHelper<bool> _isRefreshing;

	/// <summary><paramref name="editorCallSucceeded"/> runs on the UI thread after an editor call succeeds.</summary>
	public PenEditorViewModel(
		IPenCatalogueEditor penCatalogueEditor,
		PenCatalogue catalogue,
		MessagePanelViewModel messagePanel,
		ILogger<PenEditorViewModel> logger,
		Action editorCallSucceeded)
	{
		_penCatalogueEditor = penCatalogueEditor;
		_messagePanel = messagePanel;
		_logger = logger;
		_archiveCallQueue = new ArchiveCallQueue(editorCallSucceeded);

		var rows = RowsOf(catalogue);
		RecomputeMatchingRows(rows);
		_rows = [.. rows];
		PenRows = new DataGridCollectionView(_rows)
		{
			Filter = row => row is PenRowViewModel penRow && _matchingRows.Contains(penRow),
		};
		Groups = GroupsOf(rows, catalogue.Groups);

		_selectedForm = this.WhenAnyValue(editor => editor.SelectedRow)
			.DistinctUntilChanged()
			.Select(FormWhileSelected)
			.Switch()
			.ToProperty(this, editor => editor.SelectedForm);
		_disposables.Add(_selectedForm);

		_disposables.Add(RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync));

		_isRefreshing = RefreshCommand.IsExecuting.ToProperty(this, editor => editor.IsRefreshing);
		_disposables.Add(_isRefreshing);
	}

	/// <summary>
	/// The one view the pen list sorts and filters; a refresh replaces its rows and keeps its sort and filter.
	/// </summary>
	public DataGridCollectionView PenRows { get; }

	/// <summary>
	/// The pen list shows the rows <see cref="PenRowFilter"/> matches against it; the filter box writes it.
	/// </summary>
	public string FilterText
	{
		get;
		set
		{
			var text = value ?? string.Empty;
			if (field == text)
			{
				return;
			}

			field = text;
			this.RaisePropertyChanged();
			ShowRowsMatchingFilter();
		}
	} = string.Empty;

	/// <summary>
	/// Set by each pen list selection change, by the refresh and by a filter that hides it; the list binds it one way.
	/// </summary>
	public PenRowViewModel? SelectedRow
	{
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>The form of the selected row, built when the selection changes; null with no row selected.</summary>
	public PenFormViewModel? SelectedForm => _selectedForm.Value;

	/// <summary>The groups pane, rebuilt on refresh after the selected pen is restored.</summary>
	public PenGroupsViewModel Groups
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>How many pens the last refresh registered; empty until a registration first succeeds.</summary>
	public string AddedCountText
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = string.Empty;

	/// <summary>Registers the keys SCADA writes that have no pen, then reads the catalogue again.</summary>
	public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

	/// <summary>
	/// True while a refresh runs; the window takes no edit then, since the rebuild replaces its target.
	/// </summary>
	public bool IsRefreshing => _isRefreshing.Value;

	/// <inheritdoc cref="ArchiveCallQueue.IsIdle"/>
	public bool IsIdle => _archiveCallQueue.IsIdle;

	/// <inheritdoc cref="ArchiveCallQueue.WhenIdleAsync"/>
	public Task WhenIdleAsync()
	{
		return _archiveCallQueue.WhenIdleAsync();
	}

	/// <summary>The window's own code-behind route to the panel, for a throw it cannot let escape.</summary>
	public void ReportFailure(Exception failure)
	{
		_messagePanel.TryReportFailure(new ExceptionalError(failure), _logger);
	}

	public void Dispose()
	{
		_disposables.Dispose();
		Groups.Dispose();
	}

	// Switch disposes the form once the selection moves on, which stops its drafts following the row.
	private IObservable<PenFormViewModel?> FormWhileSelected(PenRowViewModel? row)
	{
		if (row is null)
		{
			return Observable.Return<PenFormViewModel?>(null);
		}

		return Observable.Using(
			() => new PenFormViewModel(row, _penCatalogueEditor, _archiveCallQueue, _messagePanel, _logger),
			form => Observable.Return<PenFormViewModel?>(form).Concat(Observable.Never<PenFormViewModel?>()));
	}

	// docs/architecture/overview.md#the-pen-list-filter
	private void ShowRowsMatchingFilter()
	{
		RecomputeMatchingRows(_rows);
		if (SelectedRow is not null && !_matchingRows.Contains(SelectedRow))
		{
			SelectedRow = null;
		}

		PenRows.MoveCurrentTo(SelectedRow);
		PenRows.Refresh();
	}

	private void RecomputeMatchingRows(IEnumerable<PenRowViewModel> rows)
	{
		_matchingRows.Clear();
		_matchingRows.UnionWith(rows.Where(row => PenRowFilter.Matches(row, FilterText)));
	}

	private async Task RefreshAsync()
	{
		var registered = await _archiveCallQueue.RunAsync(_penCatalogueEditor.RegisterNewPensAsync);

		if (registered.IsFailed)
		{
			_messagePanel.ReportFailure(registered, _logger);

			return;
		}

		AddedCountText = Resources.FormatPenEditorAddedCount(registered.Value);
		var read = await ReadAfterEveryQueuedCallAsync();

		if (read.IsFailed)
		{
			_messagePanel.ReportFailure(read, _logger);

			return;
		}

		var selectedPenId = SelectedRow?.Pen.Id;
		var rows = RowsOf(read.Value);
		var replacedGroups = Groups;

		RecomputeMatchingRows(rows);
		_rows.Clear();
		foreach (var row in rows)
		{
			_rows.Add(row);
		}

		replacedGroups.Dispose();
		SelectedRow = rows.FirstOrDefault(row => row.Pen.Id == selectedPenId && _matchingRows.Contains(row));
		Groups = GroupsOf(rows, read.Value.Groups);
	}

	private async Task<Result<PenCatalogue>> ReadAfterEveryQueuedCallAsync()
	{
		while (true)
		{
			var read = _archiveCallQueue.RunAsync(_penCatalogueEditor.ReadAsync);
			var catalogue = await read;

			if (catalogue.IsFailed || _archiveCallQueue.IsLast(read))
			{
				return catalogue;
			}
		}
	}

	private static PenRowViewModel[] RowsOf(PenCatalogue catalogue)
	{
		return [.. catalogue.Pens.Select(pen => new PenRowViewModel(pen, catalogue.Groups))];
	}

	private PenGroupsViewModel GroupsOf(IReadOnlyList<PenRowViewModel> rows, IReadOnlyList<StoredGroup> groups)
	{
		return new PenGroupsViewModel(
			rows,
			groups,
			this.WhenAnyValue(editor => editor.SelectedRow),
			_penCatalogueEditor,
			_archiveCallQueue,
			_messagePanel,
			_logger);
	}
}
