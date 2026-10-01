using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

using FluentResults;

using SemiPlot.UI.Startup;

namespace SemiPlot.UI.Settings;

/// <summary>Watches the <c>app/</c> section folder for the live theme: docs/architecture/overview.md#the-live-theme</summary>
internal sealed class AppSectionWatcher : IDisposable
{
	internal static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(300);

	internal static readonly TimeSpan ReopenInterval = TimeSpan.FromSeconds(5);

	private const string SectionFilePattern = "*.yaml";

	private readonly FileSystemWatcher _watcher;

	internal AppSectionWatcher(FileSystemWatcher watcher)
	{
		_watcher = watcher;
		Events = EventsOf(watcher);
	}

	/// <summary>One unit per file event and per buffer overflow; any other watcher error ends the stream.</summary>
	public IObservable<Unit> Events { get; }

	/// <summary>Starts watching the folder, or answers why the operating system refused to watch it.</summary>
	internal static Result<AppSectionWatcher> Open(string sectionDirectory)
	{
		FileSystemWatcher? watcher = null;

		try
		{
			ThrowUnlessListable(sectionDirectory);
			watcher = new FileSystemWatcher(sectionDirectory, SectionFilePattern)
			{
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
			};
			var opened = new AppSectionWatcher(watcher);
			var refusal = Start(watcher);

			if (refusal is null)
			{
				return opened;
			}

			watcher.Dispose();

			return Result.Fail(new ExceptionalError(refusal));
		}
		catch (Exception exception)
		{
			watcher?.Dispose();

			return Result.Fail(new ExceptionalError(exception));
		}
	}

	/// <summary>Throws the refusal of a folder the account cannot list before any watcher thread exists.</summary>
	private static void ThrowUnlessListable(string sectionDirectory)
	{
		_ = Directory.EnumerateFileSystemEntries(sectionDirectory).Any();
	}

	/// <summary>Enables the watcher and returns the refusal that Linux raises as an Error event.</summary>
	private static Exception? Start(FileSystemWatcher watcher)
	{
		Exception? refusal = null;

		void CaptureRefusal(object sender, ErrorEventArgs refused)
		{
			refusal ??= refused.GetException();
		}

		watcher.Error += CaptureRefusal;

		try
		{
			watcher.EnableRaisingEvents = true;
		}
		finally
		{
			watcher.Error -= CaptureRefusal;
		}

		return refusal;
	}

	public void Dispose()
	{
		_watcher.Dispose();
	}

	/// <summary>
	/// A success per file event and one failure per outage, a refused open or a stopped watcher; the watch opens
	/// again every <see cref="ReopenInterval"/>, and a watch opened again emits one success for what it missed.
	/// </summary>
	internal static IObservable<Result> Watch(Func<Result<AppSectionWatcher>> open, IScheduler scheduler)
	{
		return Observable.Defer(() =>
		{
			var reopening = new Reopening(open);
			var pause = Observable.Timer(ReopenInterval, scheduler).IgnoreElements().Select(_ => Result.Ok());

			return Observable.Defer(reopening.Attempt).Concat(pause).Repeat();
		});
	}

	private static IObservable<Unit> EventsOf(FileSystemWatcher watcher)
	{
		return Observable.Merge(
			UnitsOf<FileSystemEventHandler, FileSystemEventArgs>(
				handler => watcher.Changed += handler, handler => watcher.Changed -= handler),
			UnitsOf<FileSystemEventHandler, FileSystemEventArgs>(
				handler => watcher.Created += handler, handler => watcher.Created -= handler),
			UnitsOf<FileSystemEventHandler, FileSystemEventArgs>(
				handler => watcher.Deleted += handler, handler => watcher.Deleted -= handler),
			UnitsOf<RenamedEventHandler, RenamedEventArgs>(
				handler => watcher.Renamed += handler, handler => watcher.Renamed -= handler),
			Observable.FromEventPattern<ErrorEventHandler, ErrorEventArgs>(
					handler => watcher.Error += handler, handler => watcher.Error -= handler)
				.SelectMany(error => OverflowOrStop(error.EventArgs.GetException())));
	}

	private static IObservable<Unit> UnitsOf<THandler, TArgs>(Action<THandler> addHandler, Action<THandler> removeHandler)
	{
		return Observable.FromEventPattern<THandler, TArgs>(addHandler, removeHandler)
			.Select(_ => Unit.Default);
	}

	/// <summary>An overflow stands for lost events and reloads; any other error means the watcher stopped.</summary>
	private static IObservable<Unit> OverflowOrStop(Exception error)
	{
		return error is InternalBufferOverflowException
			? Observable.Return(Unit.Default)
			: Observable.Throw<Unit>(error);
	}

	/// <summary>Loads the section once per quiet burst of watch successes and emits each failure and changed theme.</summary>
	internal static IObservable<Result<AppThemeVariant>> ThemeChanges(
		IObservable<Result> watch,
		AppThemeVariant? appliedTheme,
		Func<Result<AppSettings>> load,
		IScheduler dataScheduler,
		IScheduler uiScheduler)
	{
		// docs/architecture/overview.md#the-live-theme
		var signals = appliedTheme is null ? watch : watch.StartWith(Result.Ok());

		return signals
			.Publish(shared => Observable.Merge(
				shared
					.Where(signal => signal.IsSuccess)
					.Throttle(QuietPeriod, dataScheduler)
					.Select(_ => Result.Try(load)),
				shared
					.Where(signal => signal.IsFailed)
					.Select(outage => Result.Fail<AppSettings>(outage.Errors))))
			.Scan(new ThemeStep(appliedTheme, Emitted: null), (step, loaded) => step.Next(loaded))
			.Where(step => step.Emitted is not null)
			.Select(step => step.Emitted!)
			.ObserveOn(uiScheduler);
	}

	/// <summary>The theme last passed on, and what one load emits: a failure, a new theme or nothing.</summary>
	private sealed record ThemeStep(AppThemeVariant? Applied, Result<AppThemeVariant>? Emitted)
	{
		public ThemeStep Next(Result<AppSettings> loaded)
		{
			if (loaded.IsFailed)
			{
				return this with { Emitted = Result.Fail<AppThemeVariant>(loaded.Errors) };
			}

			var theme = loaded.Value.Theme;

			return theme == Applied ? this with { Emitted = null } : new ThemeStep(theme, Result.Ok(theme));
		}
	}

	/// <summary>One subscription's opens: whether an open was tried before and whether its outage is reported.</summary>
	private sealed class Reopening(Func<Result<AppSectionWatcher>> open)
	{
		// One attempt at a time touches these: the scheduler's thread opens, the watcher's thread reports a stop.
		private bool _hasAttempted;
		private bool _isOutageReported;

		public IObservable<Result> Attempt()
		{
			var isReopen = _hasAttempted;
			_hasAttempted = true;
			var opened = open();

			if (opened.IsFailed)
			{
				return Outage(opened.Errors);
			}

			var missed = isReopen ? Observable.Return(Result.Ok()) : Observable.Empty<Result>();
			_isOutageReported = false;

			return missed
				.Concat(Observable.Using(() => opened.Value, watcher => watcher.Events.Select(_ => Result.Ok())))
				.Catch((Exception stopped) => Outage([new ExceptionalError(stopped)]));
		}

		private IObservable<Result> Outage(IEnumerable<IError> errors)
		{
			if (_isOutageReported)
			{
				return Observable.Empty<Result>();
			}

			_isOutageReported = true;

			return Observable.Return(Result.Fail(errors));
		}
	}
}
