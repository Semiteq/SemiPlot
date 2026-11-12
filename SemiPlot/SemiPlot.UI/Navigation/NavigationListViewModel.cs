using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using ReactiveUI;

namespace SemiPlot.UI.Navigation;

/// <summary>
/// A navigation list's filter, rows and active row: the operator marker list or the recipe run list
/// (docs/architecture/trend-interaction.md#the-marker-and-run-lists).
/// </summary>
public sealed class NavigationListViewModel : ReactiveObject, IDisposable
{
	private readonly Func<DateTime> _windowCentreUtc;
	private readonly Action<NavigationListEntry> _goToEntry;
	private readonly CompositeDisposable _disposables = [];
	private readonly ObservableAsPropertyHelper<ReadOnlyCollection<NavigationListRow>> _rows;
	private readonly ObservableAsPropertyHelper<bool> _hasNoMatch;

	public NavigationListViewModel(
		IObservable<IReadOnlyList<NavigationListEntry>> newestFirstEntries,
		Func<DateTime> windowCentreUtc,
		Action<NavigationListEntry> goToEntry,
		TimeZoneInfo zone,
		string filterPlaceholder,
		string noMatchText)
	{
		_windowCentreUtc = windowCentreUtc;
		_goToEntry = goToEntry;
		FilterPlaceholder = filterPlaceholder;
		NoMatchText = noMatchText;

		_disposables.Add(newestFirstEntries
			.CombineLatest(
				this.WhenAnyValue(list => list.FilterText),
				(entries, filter) => NavigationListRows.Build(entries, filter, zone))
			.ToProperty(this, list => list.Rows, out _rows));
		_disposables.Add(this
			.WhenAnyValue(list => list.Rows, rows => rows.Count == 0)
			.ToProperty(this, list => list.HasNoMatch, out _hasNoMatch));
		_disposables.Add(this.WhenAnyValue(list => list.Rows).Subscribe(KeepActiveRow));
	}

	/// <summary>The text the list filters on; the filter box writes it, and <see cref="Open"/> clears it.</summary>
	public string FilterText
	{
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	} = string.Empty;

	public string FilterPlaceholder { get; }

	public string NoMatchText { get; }

	/// <summary>The month headers and the entry rows the filter keeps, newest first.</summary>
	public ReadOnlyCollection<NavigationListRow> Rows => _rows.Value;

	/// <summary>True while the list holds no row: the filter keeps none, or no entry is left.</summary>
	public bool HasNoMatch => _hasNoMatch.Value;

	public NavigationListItem? ActiveItem { get; private set; }

	/// <summary>The active row's index in <see cref="Rows"/>, or -1 without one.</summary>
	public int ActiveIndex => ActiveItem is { } item ? Rows.IndexOf(item) : -1;

	public void Dispose()
	{
		_disposables.Dispose();
	}

	/// <summary>Clears the filter and makes the entry nearest the window centre the active row.</summary>
	public void Open()
	{
		FilterText = string.Empty;
		Activate(NavigationListRows.Nearest(Rows, _windowCentreUtc()));
	}

	/// <summary>Moves the active row by the step over the entry rows, held at the first and the last.</summary>
	public void MoveActive(int step)
	{
		var items = Rows.OfType<NavigationListItem>().ToList();
		if (items.Count == 0)
		{
			return;
		}

		var index = ActiveItem is { } item ? items.IndexOf(item) : -1;
		var target = index < 0 ? (step > 0 ? 0 : items.Count - 1) : Math.Clamp(index + step, 0, items.Count - 1);
		Activate(items[target]);
	}

	/// <summary>Goes to the active row's entry; false without one.</summary>
	public bool GoToActive()
	{
		if (ActiveItem is not { } item)
		{
			return false;
		}

		GoTo(item);

		return true;
	}

	public void GoTo(NavigationListItem item)
	{
		_goToEntry(item.Entry);
	}

	/// <summary>A rebuilt list keeps its active entry while the filter keeps it, else takes the nearest one.</summary>
	private void KeepActiveRow(IReadOnlyList<NavigationListRow> rows)
	{
		var kept = ActiveItem is { } active
			? rows.OfType<NavigationListItem>().FirstOrDefault(item => item.Entry.Key == active.Entry.Key)
			: null;
		Activate(kept ?? NavigationListRows.Nearest(rows, _windowCentreUtc()));
	}

	private void Activate(NavigationListItem? item)
	{
		if (ActiveItem is { } previous && !ReferenceEquals(previous, item))
		{
			previous.IsActive = false;
		}

		ActiveItem = item;
		item?.IsActive = true;
	}
}
