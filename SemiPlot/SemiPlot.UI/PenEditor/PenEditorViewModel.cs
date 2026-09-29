using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.PenEditor;

/// <summary>The pen table's columns in the order the table shows them.</summary>
public enum PenColumn
{
	Id,
	EnabledOnStart,
	Color,
	Name,
	Unit,
	Mask,
	LineStyle,
	ScaleMin,
	ScaleMax,
	Groups
}

/// <summary>
/// The pen and group editor window's root: the pen table, the form of the selected pen, the groups tab and
/// refresh. Every editor call runs through the one queue it owns.
/// </summary>
public sealed class PenEditorViewModel : ReactiveObject, IDisposable
{
	private readonly IPenCatalogueEditor _penCatalogueEditor;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly ILogger<PenEditorViewModel> _logger;
	private readonly EditorCallQueue _editorCallQueue;
	private readonly CompositeDisposable _disposables = [];
	private readonly ObservableAsPropertyHelper<PenFormViewModel?> _selectedForm;
	private readonly ObservableAsPropertyHelper<bool> _isRefreshing;
	private PenColumn? _sortColumn;
	private bool _isSortDescending;
	private bool _isReorderingRows;

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
		_editorCallQueue = new EditorCallQueue(editorCallSucceeded);

		var rows = RowsOf(catalogue);
		Rows = rows;
		Groups = GroupsOf(rows, catalogue.Groups);

		_selectedForm = this.WhenAnyValue(editor => editor.SelectedRow)
			.DistinctUntilChanged()
			.Select(FormWhileSelected)
			.Switch()
			.ToProperty(this, editor => editor.SelectedForm);
		_disposables.Add(_selectedForm);

		_disposables.Add(SortCommand = ReactiveCommand.Create<PenColumn>(Sort));
		_disposables.Add(RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync));

		_isRefreshing = RefreshCommand.IsExecuting.ToProperty(this, editor => editor.IsRefreshing);
		_disposables.Add(_isRefreshing);
	}

	/// <summary>Replaced whole on sort and on refresh; a sort keeps the row instances.</summary>
	public IReadOnlyList<PenRowViewModel> Rows
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>Two-way from the table, whose selection write-back is ignored while a sort reorders the rows.</summary>
	public PenRowViewModel? SelectedRow
	{
		get;
		set
		{
			if (!_isReorderingRows)
			{
				this.RaiseAndSetIfChanged(ref field, value);
			}
		}
	}

	/// <summary>The form of the selected row, built when the selection changes; null with no row selected.</summary>
	public PenFormViewModel? SelectedForm => _selectedForm.Value;

	/// <summary>The groups tab, rebuilt on refresh.</summary>
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

	/// <summary>Orders the rows by the column; a second sort on the same column reverses them.</summary>
	public ReactiveCommand<PenColumn, Unit> SortCommand { get; }

	/// <summary>Registers the keys SCADA writes that have no pen, then reads the catalogue again.</summary>
	public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

	/// <summary>
	/// True while a refresh runs; the window takes no edit then, since the rebuild replaces its target.
	/// </summary>
	public bool IsRefreshing => _isRefreshing.Value;

	/// <inheritdoc cref="EditorCallQueue.WhenIdleAsync"/>
	public Task WhenIdleAsync()
	{
		return _editorCallQueue.WhenIdleAsync();
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
			() => new PenFormViewModel(row, _penCatalogueEditor, _editorCallQueue, _messagePanel, _logger),
			form => Observable.Return<PenFormViewModel?>(form).Concat(Observable.Never<PenFormViewModel?>()));
	}

	private void Sort(PenColumn column)
	{
		_isSortDescending = _sortColumn == column && !_isSortDescending;
		_sortColumn = column;

		// A new items source makes the table write a null selection back before it finds the row again,
		// which would rebuild the form and drop its drafts.
		_isReorderingRows = true;
		try
		{
			Rows = Sorted(Rows);
		}
		finally
		{
			_isReorderingRows = false;
		}

		this.RaisePropertyChanged(nameof(SelectedRow));
	}

	private PenRowViewModel[] Sorted(IEnumerable<PenRowViewModel> rows)
	{
		if (_sortColumn is not { } column)
		{
			return [.. rows];
		}

		var ascending = rows.Order(Comparer<PenRowViewModel>.Create(ComparisonOf(column)))
			.ThenBy(row => row.Pen.Id);

		return _isSortDescending ? [.. ascending.Reverse()] : [.. ascending];
	}

	private async Task RefreshAsync()
	{
		var registered = await _editorCallQueue.RunAsync(_penCatalogueEditor.RegisterNewPensAsync);

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
		var selectedGroupId = Groups.SelectedGroup?.Group.Id;
		var rows = RowsOf(read.Value);
		var groups = GroupsOf(rows, read.Value.Groups);
		groups.SelectedGroup = groups.Groups.FirstOrDefault(group => group.Group.Id == selectedGroupId);
		var replacedGroups = Groups;

		Rows = Sorted(rows);
		SelectedRow = Rows.FirstOrDefault(row => row.Pen.Id == selectedPenId);
		Groups = groups;
		replacedGroups.Dispose();
	}

	private async Task<Result<PenCatalogue>> ReadAfterEveryQueuedCallAsync()
	{
		while (true)
		{
			var read = _editorCallQueue.RunAsync(_penCatalogueEditor.ReadAsync);
			var catalogue = await read;

			if (catalogue.IsFailed || _editorCallQueue.IsLast(read))
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
		return new PenGroupsViewModel(rows, groups, _penCatalogueEditor, _editorCallQueue, _messagePanel, _logger);
	}

	private static Comparison<PenRowViewModel> ComparisonOf(PenColumn column)
	{
		return column switch
		{
			PenColumn.Id => (left, right) => left.Pen.Id.CompareTo(right.Pen.Id),
			PenColumn.EnabledOnStart => (left, right) => left.Pen.EnabledOnStart.CompareTo(right.Pen.EnabledOnStart),
			PenColumn.Color => ByText(row => row.Pen.Color),
			PenColumn.Name => ByText(row => row.Pen.Name),
			PenColumn.Unit => ByText(row => row.Pen.Unit),
			PenColumn.Mask => ByText(row => row.Pen.Format),
			PenColumn.LineStyle => (left, right) => left.Pen.LineStyle.CompareTo(right.Pen.LineStyle),
			PenColumn.ScaleMin => (left, right) => Nullable.Compare(left.Pen.ScaleMin, right.Pen.ScaleMin),
			PenColumn.ScaleMax => (left, right) => Nullable.Compare(left.Pen.ScaleMax, right.Pen.ScaleMax),
			PenColumn.Groups => ByText(row => row.GroupsText),
			_ => throw new ArgumentOutOfRangeException(nameof(column), column, null)
		};
	}

	private static Comparison<PenRowViewModel> ByText(Func<PenRowViewModel, string?> textOf)
	{
		return (left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(textOf(left), textOf(right));
	}
}
