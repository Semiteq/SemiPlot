using System.Reactive.Concurrency;
using System.Reactive.Linq;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Bridge;

namespace SemiPlot.UI.MainWindow;

/// <summary>
/// Applies what the catalogue read loop emits to the window's chart, sidebar and minimap, one change at a
/// time, in the order read (docs/architecture/overview.md#what-a-read-changes).
/// </summary>
internal sealed class PenCatalogueApplier : IDisposable
{
	private readonly PenCatalogueSync _catalogueSync;
	private readonly MainWindowViewModel _window;
	private readonly IDisposable _subscription;

	public PenCatalogueApplier(PenCatalogueSync catalogueSync, MainWindowViewModel window, IScheduler uiScheduler)
	{
		_catalogueSync = catalogueSync;
		_window = window;
		_subscription = catalogueSync.Deltas
			.Select(delta => Observable.FromAsync(disposed => ApplyAsync(delta, disposed), uiScheduler))
			.Concat()
			.Subscribe(_ => { }, window.ReportFailure);
	}

	public void Dispose()
	{
		_subscription.Dispose();
	}

	private async Task ApplyAsync(PenListDelta delta, CancellationToken disposed)
	{
		if (_window.ChartViewModel is not { } chart)
		{
			return;
		}

		var shownBefore = chart.Catalogue;
		var isApplied = false;

		try
		{
			var addsPens = delta.Added.Count > 0;
			var hadPens = !chart.HasNoPens;

			if (!hadPens && addsPens && _window.MinimapViewModel is { } emptyChartMinimap)
			{
				var extent = await emptyChartMinimap.LoadExtentAsync();

				if (disposed.IsCancellationRequested)
				{
					return;
				}

				if (extent.IsSuccess)
				{
					chart.Navigation.SeedFromArchiveExtent(extent.Value);
				}
			}

			chart.ApplyCatalogue(delta.Current);
			_window.LegendViewModel?.Rebuild();
			isApplied = true;

			if (hadPens && addsPens && _window.MinimapViewModel is { } minimap)
			{
				var extent = await minimap.LoadExtentAsync();

				if (!disposed.IsCancellationRequested && extent.IsSuccess)
				{
					chart.Navigation.WidenToArchiveExtent(extent.Value);
				}
			}
		}
		catch (Exception applyFailure)
		{
			_window.ReportFailure(applyFailure);

			// docs/architecture/overview.md#what-a-read-changes
			if (!isApplied)
			{
				_catalogueSync.Rebase(shownBefore);
			}
		}
	}
}
