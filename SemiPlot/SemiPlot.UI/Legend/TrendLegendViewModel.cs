using System.Reactive;
using System.Reactive.Disposables;

using ReactiveUI;

using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;

namespace SemiPlot.UI.Legend;

public sealed class TrendLegendViewModel : ReactiveObject, IDisposable
{
	/// <summary>The expanded width every session starts at; MainWindow.axaml falls back to it.</summary>
	public const double ExpandedWidth = 280;

	/// <summary>The collapsed width every session starts at.</summary>
	public const double CollapsedWidth = 168;

	/// <summary>The narrowest panel a drag or a window shrink leaves.</summary>
	public const double PanelMinWidth = 120;

	/// <summary>The narrowest chart a drag of the panel may leave.</summary>
	public const double ChartMinWidth = 320;

	private readonly IReadOnlyList<TrendLegendRowViewModel> _rows;
	private readonly CompositeDisposable _subscriptions = [];
	private double _expandedWidth = ExpandedWidth;
	private double _collapsedWidth = CollapsedWidth;
	private double _maximumWidth = double.PositiveInfinity;

	public TrendLegendViewModel(TrendChartViewModel chartViewModel)
	{
		_rows = [.. chartViewModel.Pens.Select(pen => new TrendLegendRowViewModel(chartViewModel, pen))];
		Groups = BuildGroups(_rows);

		ToggleExpandedCommand = ReactiveCommand.Create(() => { IsExpanded = !IsExpanded; });
		_subscriptions.Add(ToggleExpandedCommand);
	}

	public IReadOnlyList<TrendLegendGroupViewModel> Groups { get; }

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

	public void Dispose()
	{
		_subscriptions.Dispose();

		foreach (var group in Groups)
		{
			group.Dispose();
		}

		foreach (var row in _rows)
		{
			row.Dispose();
		}
	}

	// docs/architecture/charting.md#module-layout-avalonia-views--view-models--core-models
	private double FitWidth(double width)
	{
		return Math.Max(PanelMinWidth, Math.Min(width, _maximumWidth));
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
