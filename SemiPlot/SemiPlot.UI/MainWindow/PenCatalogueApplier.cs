using System.Reactive.Concurrency;
using System.Reactive.Linq;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Legend;
using SemiPlot.UI.Minimap;

namespace SemiPlot.UI.MainWindow;

/// <summary>
/// Applies what the catalogue read loop emits to the window's chart, sidebar and minimap, one change at a
/// time, in the order read (docs/architecture/overview.md#what-a-read-changes).
/// </summary>
internal sealed class PenCatalogueApplier : IDisposable
{
	private readonly PenCatalogueSync _catalogueSync;
	private readonly TrendChartViewModel _chart;
	private readonly MinimapViewModel _minimap;
	private readonly TrendLegendViewModel _legend;
	private readonly IDisposable _subscription;

	public PenCatalogueApplier(
		PenCatalogueSync catalogueSync,
		TrendChartViewModel chart,
		MinimapViewModel minimap,
		TrendLegendViewModel legend,
		IScheduler uiScheduler)
	{
		_catalogueSync = catalogueSync;
		_chart = chart;
		_minimap = minimap;
		_legend = legend;
		_subscription = catalogueSync.Deltas
			.Select(delta => Observable.FromAsync(disposed => ApplyAsync(delta, disposed), uiScheduler))
			.Concat()
			.Subscribe(_ => { }, chart.ReportFailure);
	}

	public void Dispose()
	{
		_subscription.Dispose();
	}

	private async Task ApplyAsync(PenListDelta delta, CancellationToken disposed)
	{
		var shownBefore = _chart.Catalogue;
		var isApplied = false;

		try
		{
			var addsPens = delta.Added.Count > 0;
			var hadPens = !_chart.HasNoPens;

			if (!hadPens && addsPens)
			{
				var extent = await _minimap.LoadExtentAsync();

				if (disposed.IsCancellationRequested)
				{
					return;
				}

				if (extent.IsSuccess)
				{
					_chart.Navigation.SeedFromArchiveExtent(extent.Value);
					_chart.WidenToArchiveExtent(extent.Value);
				}
			}

			_chart.ApplyCatalogue(delta.Current);
			_legend.Rebuild();
			isApplied = true;

			if (hadPens && addsPens)
			{
				var extent = await _minimap.LoadExtentAsync();

				if (!disposed.IsCancellationRequested && extent.IsSuccess)
				{
					_chart.WidenToArchiveExtent(extent.Value);
				}
			}
		}
		catch (Exception applyFailure)
		{
			_chart.ReportFailure(applyFailure);

			// docs/architecture/overview.md#what-a-read-changes
			if (!isApplied)
			{
				_catalogueSync.Rebase(shownBefore);
			}
		}
	}
}
