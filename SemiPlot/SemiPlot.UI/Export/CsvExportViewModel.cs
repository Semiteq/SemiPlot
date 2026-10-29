using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
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
	private readonly ObservableAsPropertyHelper<IReadOnlyList<ExportMarkerChoice>> _startMarkers;
	private readonly ObservableAsPropertyHelper<IReadOnlyList<ExportMarkerChoice>> _endMarkers;
	private readonly ExportMarkerChoice _noMarker = ExportMarkerChoice.NoMarker();
	private readonly ObservableCollection<ExportMarkerChoice> _startChoices;
	private readonly ObservableCollection<ExportMarkerChoice> _endChoices;
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
		IReadOnlyList<ExportPenChoice> pens,
		IReadOnlyList<StoredMarker> markers)
	{
		_archiveRowReader = archiveRowReader;
		_readArchiveDays = readArchiveDays;
		_messagePanel = messagePanel;
		_logger = logger;
		_uiScheduler = uiScheduler;
		_fileExporter = new CsvFileExporter(archiveRowReader, dialect);
		_startChoices = [_noMarker];
		_endChoices = [_noMarker];
		StartChoices = new ReadOnlyObservableCollection<ExportMarkerChoice>(_startChoices);
		EndChoices = new ReadOnlyObservableCollection<ExportMarkerChoice>(_endChoices);

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

		(_message, _isMessageError, _messageToolTip) = WatchMessageLine();
		_disposables.Add(_message);
		_disposables.Add(_isMessageError);
		_disposables.Add(_messageToolTip);

		SelectedStartMarker = _noMarker;
		SelectedEndMarker = _noMarker;

		// Ahead of the lists: a null from a box keeps the choice, so a dropped chosen item would blank the box.
		_disposables.Add(WatchStaleChoices(markers, timeZone));

		var startLists = StartLists(markers, timeZone);
		var endLists = EndLists(markers, timeZone);
		_disposables.Add(_startMarkers = startLists.ToProperty(this, model => model.StartMarkers));
		_disposables.Add(_endMarkers = endLists.ToProperty(this, model => model.EndMarkers));
		_disposables.Add(WatchChoices(startLists, _startChoices));
		_disposables.Add(WatchChoices(endLists, _endChoices));

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

	/// <summary>The start-kind markers of the start date, in time order.</summary>
	public IReadOnlyList<ExportMarkerChoice> StartMarkers => _startMarkers.Value;

	/// <summary>The end-kind markers of the end date in a second after the typed start's, in time order.</summary>
	public IReadOnlyList<ExportMarkerChoice> EndMarkers => _endMarkers.Value;

	/// <summary>The start box's items: no marker, then <see cref="StartMarkers"/>; one instance, updated in place.</summary>
	public ReadOnlyObservableCollection<ExportMarkerChoice> StartChoices { get; }

	/// <summary>The end box's items: no marker, then <see cref="EndMarkers"/>; one instance, updated in place.</summary>
	public ReadOnlyObservableCollection<ExportMarkerChoice> EndChoices { get; }

	/// <summary>A marker chosen here writes the start from its second; an edit of the start releases it.</summary>
	[AllowNull]
	public ExportMarkerChoice SelectedStartMarker
	{
		get;
		set
		{
			if (value is null || value == field)
			{
				return;
			}

			field = value;
			this.RaisePropertyChanged();
			PlaceStart(field);
		}
	}

	/// <summary>A marker chosen here writes the end past its second; an edit of the end releases it.</summary>
	[AllowNull]
	public ExportMarkerChoice SelectedEndMarker
	{
		get;
		set
		{
			if (value is null || value == field)
			{
				return;
			}

			field = value;
			this.RaisePropertyChanged();
			PlaceEnd(field);
		}
	}

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

	/// <summary>The save dialog's proposed name; null while the range is invalid.</summary>
	internal string? SuggestedFileName()
	{
		return Form is { From: { } from, To: { } to }
			? ExportFileName.Suggest(from, to, SelectedStartMarker.Marker?.Label)
			: null;
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

	private (ObservableAsPropertyHelper<string> Message, ObservableAsPropertyHelper<bool> IsError,
		ObservableAsPropertyHelper<string?> ToolTip) WatchMessageLine()
	{
		var message = this
			.WhenAnyValue(
				model => model.Form.Problem, model => model.StatusText, model => model.RowCount,
				ExportMessageLine.Compose)
			.ToProperty(this, model => model.Message);
		var isError = this
			.WhenAnyValue(
				model => model.Form.Problem, model => model.IsStatusFailure,
				(problem, isFailure) => problem != ExportRangeProblem.None || isFailure)
			.ToProperty(this, model => model.IsMessageError);
		var toolTip = this
			.WhenAnyValue(
				model => model.Form.Problem, model => model.SuccessPath,
				(problem, successPath) => problem == ExportRangeProblem.None ? successPath : null)
			.ToProperty(this, model => model.MessageToolTip);

		return (message, isError, toolTip);
	}

	private IObservable<IReadOnlyList<ExportMarkerChoice>> StartLists(
		IReadOnlyList<StoredMarker> markers, TimeZoneInfo timeZone)
	{
		return Form
			.WhenAnyValue(form => form.StartDate)
			.Select(startDate => ExportMarkerChoice.ForStart(markers, timeZone, startDate));
	}

	private IObservable<IReadOnlyList<ExportMarkerChoice>> EndLists(
		IReadOnlyList<StoredMarker> markers, TimeZoneInfo timeZone)
	{
		return Form
			.WhenAnyValue(
				form => form.EndDate,
				form => form.StartDate,
				form => form.StartTimeText,
				(endDate, _, _) => endDate)
			.Select(endDate => ExportMarkerChoice.ForEnd(markers, timeZone, endDate, Form.StartBound));
	}

	private IDisposable WatchChoices(
		IObservable<IReadOnlyList<ExportMarkerChoice>> lists, ObservableCollection<ExportMarkerChoice> choices)
	{
		return lists.Subscribe(list => Guarded(() => UpdateChoices(choices, list)), ReportFailure);
	}

	/// <summary>Moves a box's items to the list without a reset; relies on the lists' one time-then-id order.</summary>
	private static void UpdateChoices(
		ObservableCollection<ExportMarkerChoice> choices, IReadOnlyList<ExportMarkerChoice> list)
	{
		for (var index = choices.Count - 1; index >= 1; index--)
		{
			if (!list.Contains(choices[index]))
			{
				choices.RemoveAt(index);
			}
		}

		for (var index = 0; index < list.Count; index++)
		{
			if (index + 1 >= choices.Count || choices[index + 1] != list[index])
			{
				choices.Insert(index + 1, list[index]);
			}
		}
	}

	private IDisposable WatchStaleChoices(IReadOnlyList<StoredMarker> markers, TimeZoneInfo timeZone)
	{
		return Form
			.WhenAnyValue(
				form => form.StartDate, form => form.StartTimeText, form => form.EndDate, form => form.EndTimeText)
			.Subscribe(_ => Guarded(() => ReleaseStaleChoices(markers, timeZone)), ReportFailure);
	}

	/// <summary>A choice holds while its edge reads the value it wrote and its edge's list still offers it.</summary>
	private void ReleaseStaleChoices(IReadOnlyList<StoredMarker> markers, TimeZoneInfo timeZone)
	{
		if (!SelectedStartMarker.IsNoMarker && Form.StartBound != SelectedStartMarker.StartValue)
		{
			SelectedStartMarker = _noMarker;
		}

		if (SelectedEndMarker.IsNoMarker)
		{
			return;
		}

		var endList = ExportMarkerChoice.ForEnd(markers, timeZone, Form.EndDate, Form.StartBound);

		if (Form.EndBound != SelectedEndMarker.EndValue || !endList.Contains(SelectedEndMarker))
		{
			SelectedEndMarker = _noMarker;
		}
	}

	private void PlaceStart(ExportMarkerChoice choice)
	{
		if (!choice.IsNoMarker)
		{
			Form.PlaceStart(choice.StartValue);
		}
	}

	private void PlaceEnd(ExportMarkerChoice choice)
	{
		if (choice.IsNoMarker)
		{
			return;
		}

		if (!choice.EndLeavesItsDate)
		{
			Form.PlaceEnd(choice.EndValue);

			return;
		}

		// The box refreshes its closed item from the pick it is still raising, so a release inside it is lost;
		// AvaloniaScheduler runs a zero due time inline on the UI thread, hence one tick.
		_uiScheduler.Schedule(TimeSpan.FromTicks(1), () => Guarded(() => PlaceEndAfterThePick(choice)));
	}

	private void PlaceEndAfterThePick(ExportMarkerChoice choice)
	{
		if (SelectedEndMarker == choice)
		{
			Form.PlaceEnd(choice.EndValue);
		}
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
