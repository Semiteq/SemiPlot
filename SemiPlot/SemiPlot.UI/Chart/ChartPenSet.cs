using ScottPlot;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

/// <summary>The chart's pens in catalogue order: one state, one plottable and one scale setting per id.</summary>
public sealed class ChartPenSet(Plot plot, ChartAxisBinder axisBinder)
{
	private readonly Plot _plot = plot;
	private readonly ChartAxisBinder _axisBinder = axisBinder;
	private readonly Dictionary<int, TrendPenState> _pensById = [];
	private readonly Dictionary<int, PenScaleSettings> _settingsById = [];

	public IReadOnlyDictionary<int, TrendPenState> ById => _pensById;

	/// <summary>A new list per catalogue applied, in that catalogue's order.</summary>
	public IReadOnlyList<TrendPenState> Ordered { get; private set; } = [];

	public IReadOnlyDictionary<int, PenScaleSettings> ScaleSettings => _settingsById;

	/// <summary>The stored settings of every pen shown, in catalogue order.</summary>
	public IReadOnlyList<Pen> Catalogue => [.. Ordered.Select(state => state.Pen)];

	/// <summary>
	/// Shows exactly <paramref name="catalogue"/> and returns what that changed against the pens shown before.
	/// The caller holds the plot's lock.
	/// </summary>
	public PenListDelta Apply(IReadOnlyList<Pen> catalogue)
	{
		var change = PenListDelta.Between(Catalogue, catalogue);

		foreach (var penId in change.RemovedPenIds)
		{
			Remove(penId);
		}

		foreach (var revision in change.Revised)
		{
			Revise(revision);
		}

		foreach (var pen in change.Added)
		{
			Add(pen);
		}

		Ordered = [.. catalogue.Select(pen => _pensById[pen.PenId])];

		return change;
	}

	public bool UpdateScaleSettings(int penId, Func<PenScaleSettings, PenScaleSettings> update)
	{
		if (!_settingsById.TryGetValue(penId, out var settings))
		{
			return false;
		}

		_settingsById[penId] = update(settings);

		return true;
	}

	private void Add(Pen pen)
	{
		var line = new EnvelopeLine();

		line.Axes.XAxis = _plot.Axes.Bottom;
		_plot.Add.Plottable(line);

		_pensById.Add(pen.PenId, new TrendPenState(pen, line) { IsVisible = pen.EnabledOnStart });
		_settingsById.Add(pen.PenId, BuildScaleSettings(pen));
	}

	private void Remove(int penId)
	{
		_plot.Remove(_pensById[penId].Line);
		_axisBinder.HideAxis(penId);
		_pensById.Remove(penId);
		_settingsById.Remove(penId);
	}

	// docs/architecture/charting.md#applying-a-catalogue-read
	private void Revise(PenRevision revision)
	{
		var pen = revision.Current;

		_pensById[pen.PenId].Revise(pen);

		if (revision.ScaleChanged)
		{
			_settingsById[pen.PenId] = BuildScaleSettings(pen);
		}
	}

	private static PenScaleSettings BuildScaleSettings(Pen pen)
	{
		var settings = new PenScaleSettings(pen.PenId);

		if (pen.ScaleMin is { } min && pen.ScaleMax is { } max)
		{
			settings = settings with { Mode = ScaleMode.Manual, ManualMin = min, ManualMax = max };
		}

		return settings;
	}
}
