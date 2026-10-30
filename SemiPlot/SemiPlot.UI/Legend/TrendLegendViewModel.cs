using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;

namespace SemiPlot.UI.Legend;

public sealed class TrendLegendViewModel : ReactiveObject, IDisposable
{
	/// <summary>The expanded width every session starts at.</summary>
	public const double ExpandedWidth = 280;

	/// <summary>The collapsed width every session starts at.</summary>
	public const double CollapsedWidth = 168;

	/// <summary>The narrowest panel a drag or a window shrink leaves.</summary>
	public const double PanelMinWidth = 120;

	/// <summary>The narrowest chart a drag of the panel may leave.</summary>
	public const double ChartMinWidth = 320;

	private readonly TrendChartViewModel _chartViewModel;
	private readonly CompositeDisposable _subscriptions = [];
	private readonly Dictionary<string, bool> _foldByHeader = new(StringComparer.Ordinal);
	private IReadOnlyList<TrendLegendRowViewModel> _rows;
	private readonly ObservableAsPropertyHelper<bool> _areAllFolded;
	private readonly ObservableAsPropertyHelper<string> _foldAllText;
	private readonly ObservableAsPropertyHelper<string> _foldAllGroupsText;
	private double _expandedWidth = ExpandedWidth;
	private double _collapsedWidth = CollapsedWidth;
	private double _maximumWidth = double.PositiveInfinity;

	public TrendLegendViewModel(TrendChartViewModel chartViewModel, IReadOnlyList<StoredGroup> storedGroups)
	{
		_chartViewModel = chartViewModel;
		_rows = BuildRows(chartViewModel);
		Groups = BuildGroups(_rows, storedGroups);

		var headers = this.WhenAnyValue(legend => legend.Groups).Select(HeadersOf);
		_subscriptions.Add(_areAllFolded = WhenAllFolded(headers).ToProperty(this, legend => legend.AreAllFolded));
		var allFolded = this.WhenAnyValue(legend => legend.AreAllFolded);
		_subscriptions.Add(_foldAllText = allFolded
			.Select(folded => folded ? Resources.LegendUnfoldAll : Resources.LegendFoldAll)
			.ToProperty(this, legend => legend.FoldAllText));
		_subscriptions.Add(_foldAllGroupsText = allFolded
			.Select(folded => folded ? Resources.LegendUnfoldAllGroups : Resources.LegendFoldAllGroups)
			.ToProperty(this, legend => legend.FoldAllGroupsText));

		ToggleExpandedCommand = ReactiveCommand.Create(() => { IsExpanded = !IsExpanded; });
		_subscriptions.Add(ToggleExpandedCommand);
		_subscriptions.Add(chartViewModel.PenPicked.Subscribe(RevealPickedPen));

		ToggleAllFoldsCommand = ReactiveCommand.Create(ToggleAllFolds, headers.Select(drawn => drawn.Count > 0));
		_subscriptions.Add(ToggleAllFoldsCommand);
	}

	public IReadOnlyList<TrendLegendGroupViewModel> Groups
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>Expanded adds the value and the unit to the row; the command below is the flag's one writer.</summary>
	public bool IsExpanded
	{
		get;
		private set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(ToggleText));
			this.RaisePropertyChanged(nameof(ToggleTip));
			this.RaisePropertyChanged(nameof(PanelWidth));
		}
	} = true;

	/// <summary>The shown state's slot, fitted to the room the window leaves; ResizePanel writes a slot.</summary>
	public double PanelWidth => FitWidth(IsExpanded ? _expandedWidth : _collapsedWidth);

	public string ToggleText => IsExpanded ? Resources.LegendHideValues : Resources.LegendShowValues;

	public string ToggleTip => IsExpanded ? Resources.LegendHideValuesTip : Resources.LegendShowValuesTip;

	public ReactiveCommand<Unit, Unit> ToggleExpandedCommand { get; }

	/// <summary>True when every header is folded; a list with no header is never all folded.</summary>
	public bool AreAllFolded => _areAllFolded.Value;

	/// <summary>The fold-all button's label, naming what the next press does.</summary>
	public string FoldAllText => _foldAllText.Value;

	/// <summary>The fold-all tooltip and menu item, naming what the next press does.</summary>
	public string FoldAllGroupsText => _foldAllGroupsText.Value;

	/// <summary>Folds every header unless every header is folded, then unfolds every header.</summary>
	public ReactiveCommand<Unit, Unit> ToggleAllFoldsCommand { get; }

	/// <summary>Moves the panel's left edge by the drag delta; the panel and the chart each keep a floor.</summary>
	public void ResizePanel(double delta)
	{
		var width = FitWidth(PanelWidth - delta);

		if (IsExpanded)
		{
			_expandedWidth = width;
		}
		else
		{
			_collapsedWidth = width;
		}

		this.RaisePropertyChanged(nameof(PanelWidth));
	}

	/// <summary>Records the room the window leaves, the one write of the maximum; both slots keep their value.</summary>
	public void FitPanel(double maximumWidth)
	{
		_maximumWidth = maximumWidth;
		this.RaisePropertyChanged(nameof(PanelWidth));
	}

	/// <summary>
	/// Replaces every row and header from the chart's pens and the stored groups; the widths, the expanded state
	/// and the folds stay.
	/// </summary>
	public void Rebuild(IReadOnlyList<StoredGroup> storedGroups)
	{
		var replacedRows = _rows;
		var replacedGroups = Groups;

		_rows = BuildRows(_chartViewModel);
		Groups = BuildGroups(_rows, storedGroups);

		DisposeAll(replacedGroups, replacedRows);
	}

	public void Dispose()
	{
		_subscriptions.Dispose();
		DisposeAll(Groups, _rows);
	}

	// docs/architecture/charting.md#module-layout-avalonia-views--view-models--core-models
	private double FitWidth(double width)
	{
		return Math.Max(PanelMinWidth, Math.Min(width, _maximumWidth));
	}

	private static void DisposeAll(
		IReadOnlyList<TrendLegendGroupViewModel> groups,
		IReadOnlyList<TrendLegendRowViewModel> rows)
	{
		foreach (var group in groups)
		{
			group.Dispose();
		}

		foreach (var row in rows)
		{
			row.Dispose();
		}
	}

	private static IReadOnlyList<TrendLegendRowViewModel> BuildRows(TrendChartViewModel chartViewModel)
	{
		return [.. chartViewModel.Pens.Select(pen => new TrendLegendRowViewModel(chartViewModel, pen))];
	}

	private IReadOnlyList<TrendLegendGroupViewModel> BuildGroups(
		IReadOnlyList<TrendLegendRowViewModel> rows,
		IReadOnlyList<StoredGroup> storedGroups)
	{
		var membershipsByRow = rows.Select(row => (Row: row, Memberships: MembershipsOf(storedGroups, row))).ToArray();

		if (membershipsByRow.All(entry => entry.Memberships.Count == 0))
		{
			return rows.Count > 0 ? [TrendLegendGroupViewModel.Headerless(rows)] : [];
		}

		var ungroupedHeader = Resources.LegendUngroupedHeader;
		var rowsUnderHeaders = membershipsByRow.SelectMany(entry => HeaderNamesOf(entry.Memberships, ungroupedHeader)
			.Select(name => (Header: name, entry.Row)));

		return
		[
			.. rowsUnderHeaders
				.GroupBy(entry => entry.Header, StringComparer.Ordinal)
				.OrderBy(group => string.Equals(group.Key, ungroupedHeader, StringComparison.Ordinal))
				.ThenBy(group => group.Key, StringComparer.CurrentCulture)
				.Select(group => BuildHeader(group.Key, [.. group.Select(entry => entry.Row)], storedGroups))
		];
	}

	private static IEnumerable<string> HeaderNamesOf(IReadOnlyList<StoredGroup> memberships, string ungroupedHeader)
	{
		return memberships.Count > 0 ? memberships.Select(group => group.Name) : [ungroupedHeader];
	}

	private TrendLegendGroupViewModel BuildHeader(
		string name,
		IReadOnlyList<TrendLegendRowViewModel> rows,
		IReadOnlyList<StoredGroup> storedGroups)
	{
		if (!_foldByHeader.TryGetValue(name, out var isFolded))
		{
			isFolded = storedGroups.Any(group =>
				string.Equals(group.Name, name, StringComparison.Ordinal) && group.FoldedOnStart);
			_foldByHeader[name] = isFolded;
		}

		return new TrendLegendGroupViewModel(
			name,
			hasHeader: true,
			rows,
			isFolded,
			folded => _foldByHeader[name] = folded);
	}

	private static IReadOnlyList<TrendLegendGroupViewModel> HeadersOf(IReadOnlyList<TrendLegendGroupViewModel> groups)
	{
		return [.. groups.Where(group => group.HasHeader)];
	}

	private static IObservable<bool> WhenAllFolded(IObservable<IReadOnlyList<TrendLegendGroupViewModel>> headers)
	{
		return headers
			.Select(drawn => drawn.Count == 0
				? Observable.Return(false)
				: drawn
					.Select(header => header.WhenAnyValue(group => group.IsFolded))
					.CombineLatest()
					.Select(folds => folds.All(isFolded => isFolded)))
			.Switch()
			.DistinctUntilChanged();
	}

	private void ToggleAllFolds()
	{
		var fold = !AreAllFolded;

		foreach (var header in HeadersOf(Groups))
		{
			header.SetFolded(fold);
		}
	}

	private void RevealPickedPen(int penId)
	{
		var holders = Groups
			.Where(group => group.Rows.Any(row => row.PenState.Pen.PenId == penId))
			.ToArray();

		if (holders.Any(group => !group.IsFolded))
		{
			return;
		}

		foreach (var group in holders)
		{
			group.SetFolded(false);
		}
	}

	private static IReadOnlyList<StoredGroup> MembershipsOf(
		IReadOnlyList<StoredGroup> storedGroups,
		TrendLegendRowViewModel row)
	{
		return [.. storedGroups.Where(group => group.MemberPenIds.Contains(row.PenState.Pen.PenId))];
	}
}
