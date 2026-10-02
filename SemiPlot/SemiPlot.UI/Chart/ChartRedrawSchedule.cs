using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace SemiPlot.UI.Chart;

// docs/architecture/charting.md#module-layout-avalonia-views--view-models--core-models
public sealed class ChartRedrawSchedule : IDisposable
{
	private static readonly TimeSpan _redrawDelay = TimeSpan.FromMilliseconds(33);
	private readonly IScheduler _uiScheduler;
	private readonly Subject<Unit> _redraws = new();
	private IDisposable _scheduledRedraw = Disposable.Empty;
	private bool _isRedrawScheduled;

	public ChartRedrawSchedule(IScheduler uiScheduler)
	{
		_uiScheduler = uiScheduler;
		Redraws = _redraws.AsObservable();
	}

	public IObservable<Unit> Redraws { get; }

	public void Request()
	{
		if (_isRedrawScheduled || !_redraws.HasObservers)
		{
			return;
		}

		_isRedrawScheduled = true;
		_scheduledRedraw = _uiScheduler.Schedule(_redrawDelay, Emit);
	}

	public void Dispose()
	{
		_scheduledRedraw.Dispose();
		_redraws.Dispose();
	}

	private void Emit()
	{
		_isRedrawScheduled = false;
		_redraws.OnNext(Unit.Default);
	}
}
