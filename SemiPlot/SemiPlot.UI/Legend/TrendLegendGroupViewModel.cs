using System.Reactive;
using System.Reactive.Linq;

using ReactiveUI;

using SemiPlot.UI.Localization;

namespace SemiPlot.UI.Legend;

/// <summary>A sidebar header, the rows under it and the switch over them; no group at all draws no header.</summary>
public sealed class TrendLegendGroupViewModel : ReactiveObject, IDisposable
{
	private readonly ObservableAsPropertyHelper<bool?> _switchState;

	public TrendLegendGroupViewModel(string name, bool hasHeader, IReadOnlyList<TrendLegendRowViewModel> rows)
	{
		Name = name;
		HasHeader = hasHeader;
		Rows = rows;

		_switchState = rows
			.Select(row => row.WhenAnyValue(visibleRow => visibleRow.IsVisible))
			.CombineLatest()
			.Select(DeriveSwitchState)
			.ToProperty(this, group => group.SwitchState);

		SwitchGroupCommand = ReactiveCommand.Create(SwitchGroup);
	}

	public string Name { get; }

	public bool HasHeader { get; }

	public IReadOnlyList<TrendLegendRowViewModel> Rows { get; }

	/// <summary>True when every row is on, false when every row is off, null when they differ.</summary>
	public bool? SwitchState => _switchState.Value;

	public string SwitchName => Resources.FormatLegendGroupSwitch(Name);

	/// <summary>The switch's one writer: all rows on unless all are on already, then all off.</summary>
	public ReactiveCommand<Unit, Unit> SwitchGroupCommand { get; }

	public void Dispose()
	{
		SwitchGroupCommand.Dispose();
		_switchState.Dispose();
	}

	private static bool? DeriveSwitchState(IList<bool> visibilities)
	{
		if (visibilities.All(isVisible => isVisible))
		{
			return true;
		}

		return visibilities.Any(isVisible => isVisible) ? null : false;
	}

	private void SwitchGroup()
	{
		var switchOn = SwitchState != true;

		foreach (var row in Rows)
		{
			row.IsVisible = switchOn;
		}
	}
}
