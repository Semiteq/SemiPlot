using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.Core.Export;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Navigation;

namespace SemiPlot.UI.Export;

public sealed class CsvExportViewModel : ReactiveObject, IDisposable
{
	internal static readonly TimeSpan CountThrottle = TimeSpan.FromMilliseconds(300);

	private readonly IArchiveRowReader _archiveRowReader;
	private readonly Func<DateOnly, DateOnly, CancellationToken, Task<Result<IReadOnlyList<DateOnly>>>> _readArchiveDays;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly ILogger<CsvExportViewModel> _logger;
	private readonly IScheduler _uiScheduler;
	private readonly CsvFileExporter _fileExporter;
	private readonly CompositeDisposable _disposables = [];
	private readonly ObservableAsPropertyHelper<string> _message;
	private readonly ObservableAsPropertyHelper<bool> _isMessageError;
	private readonly ObservableAsPropertyHelper<string?> _messageToolTip;
	private CancellationTokenSource? _exportCancellation;

	public CsvExportViewModel(
		IArchiveRowReader archiveRowReader,
		Func<DateOnly, DateOnly, CancellationToken, Task<Result<IReadOnlyList<DateOnly>>>> readArchiveDays,
		CsvDialect dialect,
		MessagePanelViewModel messagePanel,
		ILogger<CsvExportViewModel> logger,
		IScheduler uiScheduler,
		TimeZoneInfo timeZone,
		DateTime fromUtc,
		DateTime toUtc,
		DateTime firstSampleUtc,
		DateTime? newestSampleUtc,
		IReadOnlyList<ExportPenChoice> pens)
	{
		_archiveRowReader = archiveRowReader;
		_readArchiveDays = readArchiveDays;
		_messagePanel = messagePanel;
		_logger = logger;
		_uiScheduler = uiScheduler;
		_fileExporter = new CsvFileExporter(archiveRowReader, dialect);

		Pens = pens;
		Form = new ExportRangeForm(fromUtc, toUtc, timeZone) { TickedPenCount = TickedPens().Count };
		Days = new ArchiveDayCalendar(timeZone);

		if (Form is { StartDate: { } startDate, EndDate: { } endDate })
		{
			Days.Seed(firstSampleUtc, newestSampleUtc, startDate, endDate);
		}

		_disposables.Add(WatchTicks());
		_disposables.Add(WatchCount());
		_disposables.Add(ReadArchiveDays());

		_disposables.Add(_message = this
			.WhenAnyValue(
				model => model.Form.Problem, model => model.StatusText, model => model.RowCount,
				ExportMessageLine.Compose)
			.ToProperty(this, model => model.Message));
		_disposables.Add(_isMessageError = this
			.WhenAnyValue(
				model => model.Form.Problem, model => model.IsStatusFailure,
				(problem, isFailure) => problem != ExportRangeProblem.None || isFailure)
			.ToProperty(this, model => model.IsMessageError));
		_disposables.Add(_messageToolTip = this
			.WhenAnyValue(
				model => model.Form.Problem, model => model.SuccessPath,
				(problem, successPath) => problem == ExportRangeProblem.None ? successPath : null)
			.ToProperty(this, model => model.MessageToolTip));

		_disposables.Add(ExportCommand = ReactiveCommand.CreateFromTask<string>(
			ExportAsync, Form.WhenAnyValue(form => form.IsValid)));
		_disposables.Add(CancelCommand = ReactiveCommand.Create(
			() => _exportCancellation?.Cancel(), ExportCommand.IsExecuting));
	}

	public ExportRangeForm Form { get; }

	public ArchiveDayCalendar Days { get; }

	/// <summary>Every pen of the snapshot in catalogue order; the export follows this order.</summary>
	public IReadOnlyList<ExportPenChoice> Pens { get; }

	/// <summary>The row count line; empty while the range is invalid, no pen is ticked or no count landed.</summary>
	public string CountText
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = string.Empty;

	/// <summary>The last count that landed; null while there is none or the last one failed.</summary>
	internal long? RowCount
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>Progress, then the outcome of the last export; empty before the first one and after an edit.</summary>
	internal string StatusText
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = string.Empty;

	internal bool IsStatusFailure
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	private string? SuccessPath
	{
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public string Message => _message.Value;

	public bool IsMessageError => _isMessageError.Value;

	public string? MessageToolTip => _messageToolTip.Value;

	/// <summary>Takes the chosen file path and writes the ticked pens' rows into it.</summary>
	public ReactiveCommand<string, Unit> ExportCommand { get; }

	public ReactiveCommand<Unit, Unit> CancelCommand { get; }

	/// <summary>Every throw of this window, its own pipelines' included, routes here.</summary>
	public void ReportFailure(Exception failure)
	{
		var error = new ExceptionalError(failure);

		_messagePanel.TryReportFailure(error, _logger);

		try
		{
			SetStatus(ArchiveFailureMapper.Map(error).Title, isFailure: true);
		}
		catch (Exception statusFailure)
		{
			_logger.LogError(statusFailure, "The export status line could not be updated");
		}
	}

	public void Dispose()
	{
		_exportCancellation?.Cancel();
		_disposables.Dispose();
	}

	private List<Pen> TickedPens()
	{
		return [.. Pens.Where(choice => choice.IsTicked).Select(choice => choice.Pen)];
	}

	private IDisposable WatchTicks()
	{
		return Pens.Select(choice => choice.WhenAnyValue(model => model.IsTicked).Skip(1))
			.Merge()
			.Subscribe(_ => Guarded(() => Form.TickedPenCount = TickedPens().Count), ReportFailure);
	}

	private IDisposable WatchCount()
	{
		return Form
			.WhenAnyValue(
				form => form.From,
				form => form.To,
				form => form.TickedPenCount,
				(start, end, _) => (Start: start, End: end))
			.Do(_ => ClearOutcome())
			.Select(range => Observable.Timer(CountThrottle, _uiScheduler)
				.SelectMany(_ => Observable.FromAsync(token => CountAsync(range.Start, range.End, token)))
				.ObserveOn(_uiScheduler))
			.Switch()
			.Subscribe(count => Guarded(() => ApplyCount(count)), ReportFailure);
	}

	private IDisposable ReadArchiveDays()
	{
		if (Days.ReadRange is not { } range)
		{
			return Disposable.Empty;
		}

		return Observable
			.FromAsync(cancellationToken => _readArchiveDays(range.First, range.Last, cancellationToken))
			.ObserveOn(_uiScheduler)
			.Subscribe(result => Guarded(() => ApplyArchiveDays(result, range)), ReportFailure);
	}

	private void ApplyArchiveDays(Result<IReadOnlyList<DateOnly>> result, (DateOnly First, DateOnly Last) range)
	{
		if (result.IsFailed)
		{
			_messagePanel.TryReportFailure(result.Errors, _logger);

			return;
		}

		Days.ApplyCoverage(range, result.Value, Form.StartDate, Form.EndDate);
	}

	private async Task<Result<long>?> CountAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		if (from is not { } start || to is not { } end)
		{
			return null;
		}

		var penIds = TickedPens().ConvertAll(pen => pen.PenId);

		return await _archiveRowReader.CountRowsAsync(penIds, start, end, cancellationToken);
	}

	private void ApplyCount(Result<long>? count)
	{
		RowCount = count is { IsSuccess: true } landed ? landed.Value : null;
		CountText = count switch
		{
			null => string.Empty,
			{ IsFailed: true } => Resources.ExportCountUnavailable,
			_ => Resources.FormatExportCountRows(count.Value)
		};

		if (count is { IsFailed: true })
		{
			_messagePanel.TryReportFailure(count.Errors, _logger);
		}
	}

	private async Task ExportAsync(string path)
	{
		if (Form.From is not { } from || Form.To is not { } to)
		{
			return;
		}

		var pens = TickedPens();
		var cancellation = new CancellationTokenSource();

		_exportCancellation = cancellation;
		ShowProgress(0);

		try
		{
			var result = await Task.Run(
				() => _fileExporter.ExportAsync(path, from, to, pens, PostProgress, cancellation.Token),
				cancellation.Token);

			ShowOutcome(result, path);
		}
		catch (OperationCanceledException)
		{
			SetStatus(Resources.ExportCancelled);
		}
		catch (Exception failure)
		{
			ReportFailure(failure);
		}
		finally
		{
			_exportCancellation = null;
			cancellation.Dispose();
		}
	}

	private void ShowOutcome(Result<long> result, string path)
	{
		if (result.IsSuccess)
		{
			SetStatus(Resources.FormatExportSuccess(result.Value, Path.GetFileName(path)));
			SuccessPath = path;

			return;
		}

		SetStatus(ArchiveFailureMapper.Map(result.Errors[0]).Title, isFailure: true);
		_messagePanel.TryReportFailure(result.Errors, _logger);
	}

	private void PostProgress(long rowsWritten)
	{
		_uiScheduler.Schedule(() => Guarded(() => ShowProgress(rowsWritten)));
	}

	private void ShowProgress(long rowsWritten)
	{
		if (_exportCancellation is null)
		{
			return;
		}

		SetStatus(RowCount is { } total
			? Resources.FormatExportProgress(rowsWritten, Math.Max(rowsWritten, total))
			: Resources.FormatExportProgressUnknownTotal(rowsWritten));
	}

	private void SetStatus(string text, bool isFailure = false)
	{
		SuccessPath = null;
		IsStatusFailure = isFailure;
		StatusText = text;
	}

	private void ClearOutcome()
	{
		if (_exportCancellation is null)
		{
			SetStatus(string.Empty);
		}
	}

	private void Guarded(Action handler)
	{
		try
		{
			handler();
		}
		catch (Exception failure)
		{
			ReportFailure(failure);
		}
	}
}
