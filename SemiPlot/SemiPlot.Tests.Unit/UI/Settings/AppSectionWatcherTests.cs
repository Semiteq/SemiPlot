using System.ComponentModel;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Reactive.Testing;

using SemiPlot.UI.Settings;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Settings;

[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class AppSectionWatcherTests : IDisposable
{
	private static readonly TimeSpan _tick = TimeSpan.FromTicks(1);

	private readonly TestScheduler _dataScheduler = new();
	private readonly TestScheduler _uiScheduler = new();
	private readonly Subject<Result> _fileEvents = new();
	private readonly List<Result<AppThemeVariant>> _emitted = [];
	private Result<AppSettings> _onDisk = Result.Ok(new AppSettings(UiLanguage.Ru, AppThemeVariant.Light));
	private Exception? _loadFailure;
	private int _loads;
	private int _opens;
	private IDisposable? _subscription;

	public void Dispose()
	{
		_subscription?.Dispose();
		_fileEvents.Dispose();
	}

	[Fact]
	public void AThemeChangeOnDiskAppliesTheTheme()
	{
		Watch(appliedTheme: AppThemeVariant.Light);
		_onDisk = Result.Ok(new AppSettings(UiLanguage.Ru, AppThemeVariant.Dark));

		_fileEvents.OnNext(Result.Ok());
		_dataScheduler.AdvanceBy(AppSectionWatcher.QuietPeriod.Ticks);

		_loads.Should().Be(1, "the load runs on the data scheduler");
		_emitted.Should().BeEmpty("the theme reaches the application on the UI scheduler");

		_uiScheduler.AdvanceBy(_tick.Ticks);

		_emitted.Should().ContainSingle().Which.Value.Should().Be(AppThemeVariant.Dark);
	}

	[Fact]
	public void ALanguageChangeOnDiskAppliesNothing()
	{
		Watch(appliedTheme: AppThemeVariant.Light);
		_onDisk = Result.Ok(new AppSettings(UiLanguage.En, AppThemeVariant.Light));

		Signal();

		_loads.Should().Be(1);
		_emitted.Should().BeEmpty();
	}

	[Fact]
	public void TheSameThemeTwiceAppliesOnce()
	{
		Watch(appliedTheme: AppThemeVariant.Light);
		_onDisk = Result.Ok(new AppSettings(UiLanguage.Ru, AppThemeVariant.Dark));

		Signal();
		Signal();

		_loads.Should().Be(2);
		_emitted.Should().ContainSingle().Which.Value.Should().Be(AppThemeVariant.Dark);
	}

	[Fact]
	public void AStartWithoutSettingsAppliesTheFirstThemeItLoads()
	{
		Watch(appliedTheme: null);

		Signal();

		_emitted.Should().ContainSingle().Which.Value.Should().Be(AppThemeVariant.Light);
	}

	[Fact]
	public void AThemeSavedBeforeTheWatchStartsIsAppliedAfterOneQuietPeriod()
	{
		_onDisk = Result.Ok(new AppSettings(UiLanguage.Ru, AppThemeVariant.Dark));
		Watch(appliedTheme: AppThemeVariant.Light);

		AdvanceBoth();

		_loads.Should().Be(1, "the save landed between the start's read and the watcher, so no event reports it");
		_emitted.Should().ContainSingle().Which.Value.Should().Be(AppThemeVariant.Dark);
	}

	[Fact]
	public void AStartWithoutSettingsLoadsNothingBeforeAFileEvent()
	{
		Watch(appliedTheme: null);

		AdvanceBoth();

		_loads.Should().Be(0, "the start has already reported the section it could not load");
	}

	[Fact]
	public void ABurstOfEventsCollapsesToOneLoad()
	{
		Watch(appliedTheme: AppThemeVariant.Light);
		var withinQuietPeriod = AppSectionWatcher.QuietPeriod - _tick;

		for (var index = 0; index < 5; index++)
		{
			_fileEvents.OnNext(Result.Ok());
			_dataScheduler.AdvanceBy(withinQuietPeriod.Ticks);
		}

		_loads.Should().Be(0, "each event restarts the quiet period");

		_dataScheduler.AdvanceBy(_tick.Ticks);

		_loads.Should().Be(1);
	}

	[Fact]
	public void AFailedLoadReachesTheReportAndKeepsTheAppliedTheme()
	{
		Watch(appliedTheme: AppThemeVariant.Light);
		var failure = new AppSettingsError("app", AppSettingsProblem.KeyMissing, AppSettingsLoader.ThemeKey);
		_onDisk = Result.Fail<AppSettings>(failure);

		Signal();

		_emitted.Should().ContainSingle().Which.Errors.Should().ContainSingle().Which.Should().BeSameAs(failure);

		_onDisk = Result.Ok(new AppSettings(UiLanguage.Ru, AppThemeVariant.Light));
		Signal();

		_emitted.Should().ContainSingle("the theme on disk is the one already applied");
	}

	[Fact]
	public void ALoadThatThrowsIsEmittedAsAFailureAndTheWatchGoesOn()
	{
		Watch(appliedTheme: AppThemeVariant.Light);
		var thrown = new IOException("The section read threw.");
		_loadFailure = thrown;

		Signal();

		_emitted.Should().ContainSingle().Which.Errors.Should().ContainSingle()
			.Which.Should().BeOfType<ExceptionalError>().Which.Exception.Should().BeSameAs(thrown);

		_loadFailure = null;
		_onDisk = Result.Ok(new AppSettings(UiLanguage.Ru, AppThemeVariant.Dark));
		Signal();

		_emitted.Should().HaveCount(2).And.Subject.Last().Value.Should().Be(AppThemeVariant.Dark);
	}

	[Fact]
	public void ABufferOverflowReloadsOnce()
	{
		using var watcher = new RaisingWatcher();
		WatchThrough(Opening(Opened(watcher)));
		_onDisk = Result.Ok(new AppSettings(UiLanguage.Ru, AppThemeVariant.Dark));

		watcher.RaiseError(new InternalBufferOverflowException());
		AdvanceBoth();

		_loads.Should().Be(1);
		_opens.Should().Be(1, "an overflow loses events, not the watcher");
		_emitted.Should().ContainSingle().Which.Value.Should().Be(AppThemeVariant.Dark);
	}

	[Fact]
	public void AStoppedWatcherIsReportedOnceAndItsWatchOpensAgain()
	{
		using var stopped = new RaisingWatcher();
		using var reopened = new RaisingWatcher();
		var networkNameDeleted = new Win32Exception(64);
		WatchThrough(Opening(Opened(stopped), Result.Fail("The share is still gone."), Opened(reopened)));
		AdvanceBoth();

		stopped.RaiseError(networkNameDeleted);
		AdvanceBoth();

		_emitted.Should().ContainSingle().Which.Errors.Should().ContainSingle()
			.Which.Should().BeOfType<ExceptionalError>().Which.Exception.Should().BeSameAs(networkNameDeleted);
		stopped.IsDisposed.Should().BeTrue();

		_dataScheduler.AdvanceBy(AppSectionWatcher.ReopenInterval.Ticks);
		AdvanceBoth();

		_emitted.Should().ContainSingle("the outage is reported once");

		_onDisk = Result.Ok(new AppSettings(UiLanguage.Ru, AppThemeVariant.Dark));
		_dataScheduler.AdvanceBy(AppSectionWatcher.ReopenInterval.Ticks);
		AdvanceBoth();

		_opens.Should().Be(3);
		_loads.Should().Be(2, "the watch opened again loads once for the events it missed");
		_emitted.Should().HaveCount(2).And.Subject.Last().Value.Should().Be(AppThemeVariant.Dark);
	}

	[Fact]
	public void ARefusedOpenIsReportedOnceAndOpenedAgain()
	{
		using var watcher = new RaisingWatcher();
		var refusal = new UnauthorizedAccessException("The folder cannot be listed.");
		WatchThrough(Opening(
			Result.Fail(new ExceptionalError(refusal)),
			Result.Fail(new ExceptionalError(refusal)),
			Opened(watcher)));
		AdvanceBoth();

		_emitted.Should().ContainSingle().Which.Errors.Should().ContainSingle()
			.Which.Should().BeOfType<ExceptionalError>().Which.Exception.Should().BeSameAs(refusal);

		_dataScheduler.AdvanceBy(AppSectionWatcher.ReopenInterval.Ticks);
		_onDisk = Result.Ok(new AppSettings(UiLanguage.Ru, AppThemeVariant.Dark));
		_dataScheduler.AdvanceBy(AppSectionWatcher.ReopenInterval.Ticks);
		AdvanceBoth();

		_opens.Should().Be(3);
		_emitted.Should().HaveCount(2).And.Subject.Last().Value.Should().Be(AppThemeVariant.Dark);
	}

	[Fact]
	public void EveryWatcherEventReachesTheStream()
	{
		using var watcher = new RaisingWatcher();
		var events = 0;
		using var subscription = new AppSectionWatcher(watcher).Events.Subscribe(_ => events++);

		watcher.RaiseEveryFileEvent();

		events.Should().Be(4);
	}

	[Fact]
	public async Task AReplacedFileInARealFolderYieldsTheNewTheme()
	{
		var configDirectory = Directory.CreateTempSubdirectory("semiplot-app-watcher-").FullName;
		try
		{
			var sectionDirectory = Directory.CreateDirectory(Path.Combine(configDirectory, "app")).FullName;
			var stagingDirectory = Directory.CreateDirectory(Path.Combine(configDirectory, "staging")).FullName;
			var target = Path.Combine(sectionDirectory, "app.yaml");
			var staged = Path.Combine(stagingDirectory, "app.yaml");
			File.WriteAllText(target, "locale: ru\ntheme: light\n");
			File.WriteAllText(staged, "locale: ru\ntheme: dark\n");
			var applied = new TaskCompletionSource<Result<AppThemeVariant>>(
				TaskCreationOptions.RunContinuationsAsynchronously);

			using var subscription = AppSectionWatcher.ThemeChanges(
					AppSectionWatcher.Watch(() => AppSectionWatcher.Open(sectionDirectory), DefaultScheduler.Instance),
					AppThemeVariant.Light,
					() => AppSettingsLoader.Load(sectionDirectory),
					DefaultScheduler.Instance,
					ImmediateScheduler.Instance)
				.Subscribe(theme => applied.TrySetResult(theme));

			File.Replace(staged, target, Path.Combine(stagingDirectory, "app.yaml.replaced"));

			var theme = await applied.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
			theme.IsSuccess.Should().BeTrue(string.Join("; ", theme.Errors.Select(error => error.Message)));
			theme.Value.Should().Be(AppThemeVariant.Dark);
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	[Fact]
	public async Task AnAppFolderDeletedAndRecreatedIsWatchedAgain()
	{
		// Linux raises nothing when the watched folder itself goes: docs/architecture/overview.md#the-live-theme
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		var configDirectory = Directory.CreateTempSubdirectory("semiplot-app-watcher-").FullName;
		try
		{
			var sectionDirectory = Directory.CreateDirectory(Path.Combine(configDirectory, "app")).FullName;
			var file = Path.Combine(sectionDirectory, "app.yaml");
			File.WriteAllText(file, "locale: ru\ntheme: light\n");
			var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var dark = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			using var subscription = AppSectionWatcher.ThemeChanges(
					AppSectionWatcher.Watch(() => AppSectionWatcher.Open(sectionDirectory), DefaultScheduler.Instance),
					AppThemeVariant.Light,
					() => AppSettingsLoader.Load(sectionDirectory),
					DefaultScheduler.Instance,
					ImmediateScheduler.Instance)
				.Subscribe(theme =>
				{
					if (theme.IsFailed && theme.Errors.OfType<ExceptionalError>().Any(error => error.Exception is Win32Exception))
					{
						stopped.TrySetResult();
					}
					else if (theme.IsSuccess && theme.Value == AppThemeVariant.Dark)
					{
						dark.TrySetResult();
					}
				});

			Directory.Delete(sectionDirectory, recursive: true);
			await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
			await Task.Delay(AppSectionWatcher.QuietPeriod * 3, TestContext.Current.CancellationToken);
			Directory.CreateDirectory(sectionDirectory);
			File.WriteAllText(file, "locale: ru\ntheme: dark\n");

			await dark.Task.WaitAsync(
				AppSectionWatcher.ReopenInterval + TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	private void Watch(AppThemeVariant? appliedTheme)
	{
		_subscription = AppSectionWatcher.ThemeChanges(_fileEvents, appliedTheme, Load, _dataScheduler, _uiScheduler)
			.Subscribe(_emitted.Add);
	}

	private void WatchThrough(Func<Result<AppSectionWatcher>> open)
	{
		_subscription = AppSectionWatcher.ThemeChanges(
				AppSectionWatcher.Watch(open, _dataScheduler), AppThemeVariant.Light, Load, _dataScheduler, _uiScheduler)
			.Subscribe(_emitted.Add);
	}

	private Func<Result<AppSectionWatcher>> Opening(params Result<AppSectionWatcher>[] opens)
	{
		var pending = new Queue<Result<AppSectionWatcher>>(opens);

		return () =>
		{
			_opens++;

			return pending.Dequeue();
		};
	}

	private static Result<AppSectionWatcher> Opened(FileSystemWatcher watcher)
	{
		return Result.Ok(new AppSectionWatcher(watcher));
	}

	private Result<AppSettings> Load()
	{
		_loads++;

		if (_loadFailure is { } failure)
		{
			throw failure;
		}

		return _onDisk;
	}

	private void Signal()
	{
		_fileEvents.OnNext(Result.Ok());
		AdvanceBoth();
	}

	private void AdvanceBoth()
	{
		_dataScheduler.AdvanceBy(AppSectionWatcher.QuietPeriod.Ticks);
		_uiScheduler.AdvanceBy(_tick.Ticks);
	}

	private sealed class RaisingWatcher : FileSystemWatcher
	{
		public bool IsDisposed { get; private set; }

		public void RaiseError(Exception error)
		{
			OnError(new ErrorEventArgs(error));
		}

		public void RaiseEveryFileEvent()
		{
			const string SectionDirectory = "app";

			OnChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, SectionDirectory, "app.yaml"));
			OnCreated(new FileSystemEventArgs(WatcherChangeTypes.Created, SectionDirectory, "app.yaml"));
			OnDeleted(new FileSystemEventArgs(WatcherChangeTypes.Deleted, SectionDirectory, "app.yaml"));
			OnRenamed(new RenamedEventArgs(WatcherChangeTypes.Renamed, SectionDirectory, "app.yaml", "app.yaml.tmp"));
		}

		protected override void Dispose(bool disposing)
		{
			IsDisposed = true;
			base.Dispose(disposing);
		}
	}
}
