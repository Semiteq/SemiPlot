using System.Reactive;
using System.Reactive.Linq;

using ReactiveUI;

using SemiPlot.UI.Localization;

namespace SemiPlot.UI.Legend;

/// <summary>A sidebar header, the rows under it and the switch over them; no group at all draws no header.</summary>
public sealed class TrendLegendGroupViewModel : ReactiveObject, IDisposable
{
	private readonly Action<bool> _foldChanged;
	private readonly ObservableAsPropertyHelper<bool?> _switchState;
	private readonly ObservableAsPropertyHelper<bool> _showsActiveAccent;

	public TrendLegendGroupViewModel(
		string name,
		bool hasHeader,
		IReadOnlyList<TrendLegendRowViewModel> rows,
		bool isFolded,
		Action<bool> foldChanged)
	{
		Name = name;
		HasHeader = hasHeader;
		Rows = rows;
		IsFolded = isFolded;
		_foldChanged = foldChanged;

		_switchState = rows
			.Select(row => row.PenState.WhenAnyValue(state => state.IsVisible))
			.CombineLatest()
			.Select(DeriveSwitchState)
			.ToProperty(this, group => group.SwitchState);

		_showsActiveAccent = this
			.WhenAnyValue(group => group.IsFolded)
			.CombineLatest(
				rows
					.Select(row => row.WhenAnyValue(candidate => candidate.IsActive))
					.CombineLatest()
					.Select(activities => activities.Any(isActive => isActive)),
				(folded, holdsActivePen) => folded && holdsActivePen)
			.ToProperty(this, group => group.ShowsActiveAccent);

		SwitchGroupCommand = ReactiveCommand.Create(SwitchGroup);
		FoldCommand = ReactiveCommand.Create(() => SetFolded(!IsFolded));
	}

	public string Name { get; }

	public bool HasHeader { get; }

	public IReadOnlyList<TrendLegendRowViewModel> Rows { get; }

	/// <summary>True when every row is on, false when every row is off, null when they differ.</summary>
	public bool? SwitchState => _switchState.Value;

	public string SwitchName => Resources.FormatLegendGroupSwitch(Name);

	/// <summary>The switch's one writer: all rows on unless all are on already, then all off.</summary>
	public ReactiveCommand<Unit, Unit> SwitchGroupCommand { get; }

	/// <summary>A folded group shows its header only; SetFolded is the flag's one writer.</summary>
	public bool IsFolded
	{
		get;
		private set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(FoldName));
		}
	}

	/// <summary>The fold toggle's tooltip and accessible name, naming what the next press does.</summary>
	public string FoldName => IsFolded
		? Resources.FormatLegendUnfoldGroup(Name)
		: Resources.FormatLegendFoldGroup(Name);

	/// <summary>The header carries the active row's accent bar while the group is folded over the active pen.</summary>
	public bool ShowsActiveAccent => _showsActiveAccent.Value;

	/// <summary>Folds an unfolded header and unfolds a folded one; a headerless list draws no toggle.</summary>
	public ReactiveCommand<Unit, Unit> FoldCommand { get; }

	/// <summary>The one list drawn when no pen has a group: no header, so nothing folds it.</summary>
	public static TrendLegendGroupViewModel Headerless(IReadOnlyList<TrendLegendRowViewModel> rows)
	{
		return new TrendLegendGroupViewModel(string.Empty, hasHeader: false, rows, isFolded: false, static _ => { });
	}

	/// <summary>Writes the fold and reports it to the sidebar's session record.</summary>
	public void SetFolded(bool isFolded)
	{
		IsFolded = isFolded;
		_foldChanged(isFolded);
	}

	public void Dispose()
	{
		FoldCommand.Dispose();
		SwitchGroupCommand.Dispose();
		_showsActiveAccent.Dispose();
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
			row.SetVisibility(switchOn);
		}
	}
}
