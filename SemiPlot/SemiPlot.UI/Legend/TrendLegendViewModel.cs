using System.Reactive;
using System.Reactive.Disposables;

using ReactiveUI;

using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;

namespace SemiPlot.UI.Legend;

/// <summary>A sidebar header and the rows under it; a catalogue with no group at all draws no header.</summary>
public sealed record TrendLegendGroupViewModel(
	string Name,
	bool HasHeader,
	IReadOnlyList<TrendLegendRowViewModel> Rows);

public sealed class TrendLegendViewModel : ReactiveObject, IDisposable
{
	/// <summary>The sidebar width the panel asks for while expanded; MainWindow.axaml falls back to it.</summary>
	public const double ExpandedWidth = 280;

	public const double CollapsedWidth = 168;

	private readonly IReadOnlyList<TrendLegendRowViewModel> _rows;
	private readonly CompositeDisposable _subscriptions = [];

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
			this.RaisePropertyChanged(nameof(RequestedWidth));
		}
	} = true;

	public double RequestedWidth => IsExpanded ? ExpandedWidth : CollapsedWidth;

	public string ToggleText => IsExpanded ? Resources.LegendCollapsePanel : Resources.LegendExpandPanel;

	public ReactiveCommand<Unit, Unit> ToggleExpandedCommand { get; }

	public void Dispose()
	{
		_subscriptions.Dispose();

		foreach (var row in _rows)
		{
			row.Dispose();
		}
	}

	// A pen draws once and is listed under every group it carries, so one row view model appears in
	// several headers and Dispose walks the distinct list above rather than this one. Headers read in
	// the operator's alphabetical order, the ungrouped one last; the key identity above stays ordinal.
	private static IReadOnlyList<TrendLegendGroupViewModel> BuildGroups(IReadOnlyList<TrendLegendRowViewModel> rows)
	{
		var ungrouped = rows.Where(row => row.Groups.Count == 0).ToList();

		if (ungrouped.Count == rows.Count)
		{
			return rows.Count > 0 ? [new TrendLegendGroupViewModel(string.Empty, HasHeader: false, rows)] : [];
		}

		List<TrendLegendGroupViewModel> groups =
		[
			.. rows
				.SelectMany(row => row.Groups.Select(name => (Name: name, Row: row)))
				.GroupBy(entry => entry.Name, StringComparer.Ordinal)
				.OrderBy(group => group.Key, StringComparer.CurrentCulture)
				.Select(group => new TrendLegendGroupViewModel(
					group.Key,
					HasHeader: true,
					[.. group.Select(entry => entry.Row)]))
		];

		if (ungrouped.Count > 0)
		{
			AppendUngrouped(groups, ungrouped);
		}

		return groups;
	}

	// A catalogue is free to hold a group named exactly like the ungrouped header, and two headers of
	// one text read as a duplicate: the rows join that group instead.
	private static void AppendUngrouped(
		List<TrendLegendGroupViewModel> groups,
		IReadOnlyList<TrendLegendRowViewModel> ungrouped)
	{
		var collision = groups.FindIndex(group =>
			string.Equals(group.Name, Resources.LegendUngroupedHeader, StringComparison.Ordinal));

		if (collision < 0)
		{
			groups.Add(new TrendLegendGroupViewModel(Resources.LegendUngroupedHeader, HasHeader: true, ungrouped));

			return;
		}

		var merged = groups[collision] with { Rows = [.. groups[collision].Rows, .. ungrouped] };
		groups.RemoveAt(collision);
		groups.Add(merged);
	}
}
