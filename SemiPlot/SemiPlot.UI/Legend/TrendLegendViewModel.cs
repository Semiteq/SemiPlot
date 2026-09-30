using System.Reactive;
using System.Reactive.Disposables;

using ReactiveUI;

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
	private IReadOnlyList<TrendLegendRowViewModel> _rows;
	private double _expandedWidth = ExpandedWidth;
	private double _collapsedWidth = CollapsedWidth;
	private double _maximumWidth = double.PositiveInfinity;

	public TrendLegendViewModel(TrendChartViewModel chartViewModel)
	{
		_chartViewModel = chartViewModel;
		_rows = BuildRows(chartViewModel);
		Groups = BuildGroups(_rows);

		ToggleExpandedCommand = ReactiveCommand.Create(() => { IsExpanded = !IsExpanded; });
		_subscriptions.Add(ToggleExpandedCommand);
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
			this.RaisePropertyChanged(nameof(PanelWidth));
		}
	} = true;

	/// <summary>The shown state's slot, fitted to the room the window leaves; ResizePanel writes a slot.</summary>
	public double PanelWidth => FitWidth(IsExpanded ? _expandedWidth : _collapsedWidth);

	public string ToggleText => IsExpanded ? Resources.LegendCollapsePanel : Resources.LegendExpandPanel;

	public ReactiveCommand<Unit, Unit> ToggleExpandedCommand { get; }

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

	/// <summary>Replaces every row and header from the chart's pens; the widths and the expanded state stay.</summary>
	public void Rebuild()
	{
		var replacedRows = _rows;
		var replacedGroups = Groups;

		_rows = BuildRows(_chartViewModel);
		Groups = BuildGroups(_rows);

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

	private static IReadOnlyList<TrendLegendGroupViewModel> BuildGroups(IReadOnlyList<TrendLegendRowViewModel> rows)
	{
		if (rows.All(row => row.Groups.Count == 0))
		{
			return rows.Count > 0 ? [new TrendLegendGroupViewModel(string.Empty, hasHeader: false, rows)] : [];
		}

		var ungroupedHeader = Resources.LegendUngroupedHeader;

		return
		[
			.. rows
				.SelectMany(row => (row.Groups.Count == 0 ? [ungroupedHeader] : row.Groups)
					.Select(name => (Name: name, Row: row)))
				.GroupBy(entry => entry.Name, StringComparer.Ordinal)
				.OrderBy(group => string.Equals(group.Key, ungroupedHeader, StringComparison.Ordinal))
				.ThenBy(group => group.Key, StringComparer.CurrentCulture)
				.Select(group => new TrendLegendGroupViewModel(
					group.Key,
					hasHeader: true,
					[.. group.Select(entry => entry.Row)]))
		];
	}
}
