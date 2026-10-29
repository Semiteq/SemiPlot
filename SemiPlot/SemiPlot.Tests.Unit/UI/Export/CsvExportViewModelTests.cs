using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Export;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Export;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Export;

// The count, the message line and the progress run over a TestScheduler the test advances; the file work
// runs on the pool.
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class CsvExportViewModelTests : IDisposable
{
	private static readonly DateTime _startWallClock = new(2026, 10, 13, 8, 0, 0);
	private static readonly DateTime _endWallClock = new(2026, 10, 13, 9, 0, 0);

	private readonly TestScheduler _scheduler = new();
	private readonly FakeArchiveRowReader _reader = new();
	private readonly MessagePanelViewModel _messagePanel = new();
	private readonly TemporaryDirectory _directory = new("csv-export");
	private readonly Lazy<CsvExportViewModel> _viewModel;

	public CsvExportViewModelTests()
	{
		_viewModel = new Lazy<CsvExportViewModel>(BuildViewModel);
	}

	private CsvExportViewModel ViewModel => _viewModel.Value;

	public void Dispose()
	{
		if (_viewModel.IsValueCreated)
		{
			ViewModel.Dispose();
		}

		_messagePanel.Dispose();
		_directory.Dispose();
	}

	private static StoredArchiveRow RowAt(int penId, int second, double value)
	{
		return new StoredArchiveRow(penId, _startWallClock.AddSeconds(second), value);
	}

	private CsvExportViewModel BuildViewModel()
	{
		return CsvExportStand.Build(
			_reader,
			_messagePanel,
			_scheduler,
			[
				new ExportPenChoice(CsvExportStand.Pressure, true),
				new ExportPenChoice(CsvExportStand.Argon, false),
				new ExportPenChoice(CsvExportStand.Power, true)
			]);
	}

	private void Advance(long ticks)
	{
		_ = ViewModel;
		_scheduler.AdvanceBy(ticks);
	}

	// The count arrives from a task continuation, so its landing on the scheduler needs a further tick.
	private async Task SettleAsync(Func<bool> hasLanded, string awaited = "the count to land")
	{
		await HeadlessWait.Until(
			() =>
			{
				_scheduler.AdvanceBy(1);

				return hasLanded();
			},
			awaited);
	}

	private string PathOf(string fileName)
	{
		return Path.Combine(_directory.Path, fileName);
	}

	private void SetSlices(Func<FakeArchiveRowReader.SliceCall, IReadOnlyList<StoredArchiveRow>> rowsOfSlice)
	{
		_reader.OnSlice = call => Task.FromResult(Result.Ok(rowsOfSlice(call)));
	}

	private static IReadOnlyList<StoredArchiveRow> TwoRowsOf(FakeArchiveRowReader.SliceCall call)
	{
		return [new StoredArchiveRow(7, call.From, 1), new StoredArchiveRow(7, call.From.AddSeconds(1), 2)];
	}

	private static bool IsSecondSlice(FakeArchiveRowReader.SliceCall call)
	{
		return call.From == _startWallClock + ArchiveRowSlices.Width;
	}

	private static string CountLine(long count)
	{
		return Resources.FormatExportCountRows(count);
	}

	private static string PartialOf(string path)
	{
		return path + ".partial";
	}

	private string UnwritablePath()
	{
		return Path.Combine(_directory.Path, "missing-folder", "out.csv");
	}

	private void BlockAtSecondSlice(TaskCompletionSource reached, Func<FakeArchiveRowReader.SliceCall, Task> hold)
	{
		_reader.OnSlice = async call =>
		{
			if (IsSecondSlice(call))
			{
				reached.SetResult();
				await hold(call);
			}

			return Result.Ok(TwoRowsOf(call));
		};
	}

	private static async Task<Result<long>> Served(Task<Result<long>> count)
	{
		return await count;
	}

	[AvaloniaFact]
	public void ThePens_AreTickedFromTheirVisibility_InCatalogueOrder()
	{
		ViewModel.Pens.Select(choice => choice.Pen.PenId).Should().Equal(7, 3, 12);
		ViewModel.Pens.Select(choice => choice.IsTicked).Should().Equal(true, false, true);
		ViewModel.Form.TickedPenCount.Should().Be(2);
	}

	[AvaloniaFact]
	public async Task Unticking_EveryPen_MakesTheExportUnavailableAndShowsTheForm()
	{
		foreach (var choice in ViewModel.Pens)
		{
			choice.IsTicked = false;
		}

		ViewModel.Message.Should().Be(Resources.ExportMessageNoPenTicked);
		(await ViewModel.ExportCommand.CanExecute.FirstAsync()).Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task TheCount_ComesThreeHundredMillisecondsAfterTheLastEdit_OverTheTickedPens()
	{
		_reader.OnCount = _ => Task.FromResult(Result.Ok(412_000L));

		Advance(CsvExportViewModel.CountThrottle.Ticks - 1);
		_reader.CountCalls.Should().BeEmpty();

		await SettleAsync(() => ViewModel.RowCount is not null);

		var call = _reader.CountCalls.Should().ContainSingle().Subject;

		call.PenIds.Should().Equal(7, 12);
		call.From.Should().Be(_startWallClock);
		call.To.Should().Be(_endWallClock);
		ViewModel.RowCount.Should().Be(412_000);
		ViewModel.CountText.Should().Be(CountLine(412_000));
	}

	[AvaloniaFact]
	public void ARangeEdit_CancelsTheCountStillRunning_AndCountsTheNewRange()
	{
		var pending = new TaskCompletionSource<Result<long>>(TaskCreationOptions.RunContinuationsAsynchronously);

		_reader.OnCount = _ => pending.Task;
		Advance(CsvExportViewModel.CountThrottle.Ticks);
		var first = _reader.CountCalls.Should().ContainSingle().Subject;

		ViewModel.Form.EndTimeText = "10:00:00";
		Advance(CsvExportViewModel.CountThrottle.Ticks);

		first.Token.IsCancellationRequested.Should().BeTrue();
		_reader.CountCalls.Should().HaveCount(2);
		_reader.CountCalls[1].To.Should().Be(new DateTime(2026, 10, 13, 10, 0, 0));
	}

	[AvaloniaFact]
	public void Ticking_APen_CountsAgainWithTheNewSet()
	{
		Advance(CsvExportViewModel.CountThrottle.Ticks);

		ViewModel.Pens[1].IsTicked = true;
		Advance(CsvExportViewModel.CountThrottle.Ticks);

		_reader.CountCalls.Should().HaveCount(2);
		_reader.CountCalls[1].PenIds.Should().Equal(7, 3, 12);
	}

	[AvaloniaTheory]
	[InlineData(ExportMessageLine.ExcelDataRowLimit, false)]
	[InlineData(ExportMessageLine.ExcelDataRowLimit + 1, true)]
	public async Task ACountAboveTheExcelLimit_ShowsTheNoticeWithoutBlockingTheExport(long count, bool shouldNotice)
	{
		_reader.OnCount = _ => Task.FromResult(Result.Ok(count));

		Advance(CsvExportViewModel.CountThrottle.Ticks);
		await SettleAsync(() => ViewModel.RowCount == count);

		var notice = ExportMessageLine.ExcelNotice;

		ViewModel.Message.Should().Be(shouldNotice ? notice : string.Empty);
		(await ViewModel.ExportCommand.CanExecute.FirstAsync()).Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task AFailedCount_ShowsCountUnavailable_AndAddsOneEntryToThePanel()
	{
		_reader.OnCount = _ => Task.FromResult(Result.Fail<long>(Archive(ArchiveFault.ConnectionLost)));

		Advance(CsvExportViewModel.CountThrottle.Ticks);
		await SettleAsync(() => ViewModel.CountText.Length > 0);

		ViewModel.CountText.Should().Be(Resources.ExportCountUnavailable);
		ViewModel.RowCount.Should().BeNull();
		_messagePanel.Entries.Should().ContainSingle();
	}

	[AvaloniaFact]
	public async Task TheExport_WritesTheHeader_ThenTheSeedsThenTheSliceRowsInCallOrder()
	{
		var path = PathOf("out.csv");

		_reader.OnSeeds = _ => Task.FromResult(
			Result.Ok<IReadOnlyList<StoredArchiveRow>>([RowAt(12, -20, 0.5), RowAt(7, -10, 0.25)]));
		SetSlices(call => call.From == _startWallClock
			? [RowAt(7, 0, 1.5), RowAt(12, 1, 3.5), RowAt(7, 1, 2.5)]
			: []);

		await ViewModel.ExportCommand.Execute(path).ToTask();

		var lines = (await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken)).ToList();

		lines.Should().Equal(
			"Pen,Time,Value",
			"Bias power,2026-10-13 07:59:40.000,0.5",
			"\"Chamber pressure, Pa\",2026-10-13 07:59:50.000,0.25",
			"\"Chamber pressure, Pa\",2026-10-13 08:00:00.000,1.5",
			"Bias power,2026-10-13 08:00:01.000,3.5",
			"\"Chamber pressure, Pa\",2026-10-13 08:00:01.000,2.5");
		ViewModel.StatusText.Should().Be(Resources.FormatExportSuccess(5, "out.csv"));
		_messagePanel.Entries.Should().BeEmpty();
		File.Exists(PartialOf(path)).Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task AOneHourRange_IssuesOneSeedCallThenFourSlicesThatTileIt_AllOverTheTickedPensInChecklistOrder()
	{
		await ViewModel.ExportCommand.Execute(PathOf("tiles.csv")).ToTask();

		var seed = _reader.SeedCalls.Should().ContainSingle().Subject;

		seed.PenIds.Should().Equal(7, 12);
		seed.From.Should().Be(_startWallClock);
		_reader.SliceCalls.Should().HaveCount(4);
		_reader.SliceCalls.Select(call => call.PenIds).Should().AllSatisfy(ids => ids.Should().Equal(7, 12));
		_reader.SliceCalls.Select(call => call.To - call.From).Should()
			.OnlyContain(width => width <= ArchiveRowSlices.Width);
		_reader.SliceCalls[0].From.Should().Be(_startWallClock);
		_reader.SliceCalls.Zip(_reader.SliceCalls.Skip(1)).Should()
			.OnlyContain(pair => pair.First.To == pair.Second.From);
		_reader.SliceCalls[^1].To.Should().Be(_endWallClock);
	}

	[AvaloniaFact]
	public async Task ATwentyMinuteRange_EndsItsSecondSliceAtTheEndOfTheRange()
	{
		ViewModel.Form.EndTimeText = "08:20:00";

		await ViewModel.ExportCommand.Execute(PathOf("short.csv")).ToTask();

		_reader.SliceCalls.Select(call => (call.From, call.To)).Should().Equal(
			(_startWallClock, _startWallClock.AddMinutes(15)),
			(_startWallClock.AddMinutes(15), new DateTime(2026, 10, 13, 8, 20, 0)));
	}

	[AvaloniaFact]
	public async Task TheFile_StartsWithAByteOrderMark()
	{
		var path = PathOf("bom.csv");

		await ViewModel.ExportCommand.Execute(path).ToTask();

		var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

		bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
	}

	[AvaloniaFact]
	public async Task Progress_ReachesTheBoundStatePerSlice_OnlyAfterTheSchedulerAdvances()
	{
		var secondSliceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseSecondSlice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		_reader.OnCount = _ => new TaskCompletionSource<Result<long>>().Task;
		BlockAtSecondSlice(secondSliceReached, _ => releaseSecondSlice.Task);
		Advance(CsvExportViewModel.CountThrottle.Ticks);

		var export = ViewModel.ExportCommand.Execute(PathOf("progress.csv")).ToTask();

		await secondSliceReached.Task;
		ViewModel.StatusText.Should().Be(Resources.FormatExportProgressUnknownTotal(0), "no count has landed");

		Advance(1);
		ViewModel.StatusText.Should().Be(Resources.FormatExportProgressUnknownTotal(2));

		releaseSecondSlice.SetResult();
		await export;

		var outcome = Resources.FormatExportSuccess(8, "progress.csv");

		ViewModel.StatusText.Should().Be(outcome);
		Advance(1);
		ViewModel.StatusText.Should().Be(outcome, "a progress post that lands after the export is ignored");
	}

	[AvaloniaFact]
	public async Task Progress_NamesTheCountedTotal_WhenACountHasLanded()
	{
		var secondSliceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseSecondSlice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		_reader.OnCount = _ => Task.FromResult(Result.Ok(10L));
		BlockAtSecondSlice(secondSliceReached, _ => releaseSecondSlice.Task);
		Advance(CsvExportViewModel.CountThrottle.Ticks);
		await SettleAsync(() => ViewModel.RowCount == 10);

		var export = ViewModel.ExportCommand.Execute(PathOf("total.csv")).ToTask();

		await secondSliceReached.Task;
		Advance(1);
		ViewModel.StatusText.Should().Be(Resources.FormatExportProgress(2, 10));

		releaseSecondSlice.SetResult();
		await export;
	}

	[AvaloniaFact]
	public async Task Cancelling_DuringTheSecondSlice_LeavesNeitherTheFileNorThePartial()
	{
		var path = PathOf("cancelled.csv");
		var secondSliceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		BlockAtSecondSlice(secondSliceReached, call => Task.Delay(Timeout.Infinite, call.Token));

		var export = ViewModel.ExportCommand.Execute(path).ToTask();

		await secondSliceReached.Task;
		File.Exists(PartialOf(path)).Should().BeTrue();

		await ViewModel.CancelCommand.Execute().ToTask();
		await export;

		File.Exists(path).Should().BeFalse();
		File.Exists(PartialOf(path)).Should().BeFalse();
		ViewModel.StatusText.Should().Be(Resources.ExportCancelled);
		_messagePanel.Entries.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task ACancelAfterTheFirstSlice_StopsBeforeTheNextSliceEvenWhenTheReaderIgnoresTheToken()
	{
		var path = PathOf("stopped.csv");

		_reader.OnSlice = call =>
		{
			_ = ViewModel.CancelCommand.Execute().Subscribe(_ => { }, _ => { });

			return Task.FromResult(Result.Ok(TwoRowsOf(call)));
		};

		await ViewModel.ExportCommand.Execute(path).ToTask();

		_reader.SliceCalls.Should().ContainSingle();
		File.Exists(path).Should().BeFalse();
		File.Exists(PartialOf(path)).Should().BeFalse();
		ViewModel.StatusText.Should().Be(Resources.ExportCancelled);
	}

	[AvaloniaFact]
	public async Task ANamesakeThatIsNotTicked_AddsNoIdToTheTickedPensFileField()
	{
		var path = PathOf("namesake-unticked.csv");
		using var viewModel = BuildNamesakes(secondIsTicked: false);

		await viewModel.ExportCommand.Execute(path).ToTask();

		var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);

		lines.Should().HaveCount(2);
		lines[1].Should().StartWith("Damper 01,");
	}

	[AvaloniaFact]
	public async Task TwoTickedNamesakes_CarryTheirIdsInTheFile()
	{
		var path = PathOf("namesake-ticked.csv");
		using var viewModel = BuildNamesakes(secondIsTicked: true);

		await viewModel.ExportCommand.Execute(path).ToTask();

		var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);

		lines.Skip(1).Select(line => line[..line.IndexOf(',')]).Should().Equal("Damper 01 (21)", "Damper 01 (22)");
	}

	[AvaloniaFact]
	public async Task AFailedSecondSlice_LeavesNoFile_ShowsTheTitle_AndAddsOneEntryToThePanel()
	{
		var path = PathOf("failed.csv");
		var error = Archive(ArchiveFault.ConnectionLost);

		_reader.OnSlice = call => Task.FromResult(
			IsSecondSlice(call)
				? Result.Fail<IReadOnlyList<StoredArchiveRow>>(error)
				: Result.Ok(TwoRowsOf(call)));

		await ViewModel.ExportCommand.Execute(path).ToTask();

		File.Exists(path).Should().BeFalse();
		File.Exists(PartialOf(path)).Should().BeFalse();
		ViewModel.StatusText.Should().Be(ArchiveFailureMapper.Map(error).Title);
		_messagePanel.Entries.Should().ContainSingle();
	}

	[AvaloniaFact]
	public async Task AFailedExport_KeepsTheFileThatWasAlreadyAtThePath()
	{
		var path = PathOf("existing.csv");

		await File.WriteAllTextAsync(path, "previous", TestContext.Current.CancellationToken);
		_reader.OnSeeds = _ => Task.FromResult(
			Result.Fail<IReadOnlyList<StoredArchiveRow>>(Archive(ArchiveFault.ConnectionLost)));

		await ViewModel.ExportCommand.Execute(path).ToTask();

		(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().Be("previous");
		File.Exists(PartialOf(path)).Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task AnUnwritablePath_MapsToAnExportFileError()
	{
		var path = UnwritablePath();

		await ViewModel.ExportCommand.Execute(path).ToTask();

		ViewModel.StatusText.Should().Be(Resources.FailureExportFileTitle);
		_messagePanel.Entries.Should().ContainSingle()
			.Which.View.Detail.Should().Contain(path);
	}

	[AvaloniaFact]
	public async Task Dispose_DuringASlice_CancelsItAndLeavesNothingBehind()
	{
		var path = PathOf("disposed.csv");
		var pendingReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var readWasCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		_reader.OnSlice = async call =>
		{
			pendingReadStarted.SetResult();

			try
			{
				await Task.Delay(Timeout.Infinite, call.Token);
			}
			catch (OperationCanceledException)
			{
				readWasCancelled.SetResult();

				throw;
			}

			return Result.Ok<IReadOnlyList<StoredArchiveRow>>([]);
		};

		_ = ViewModel.ExportCommand.Execute(path).Subscribe(_ => { }, _ => { });

		await pendingReadStarted.Task;
		ViewModel.Dispose();
		await readWasCancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
		await HeadlessWait.Until(() => !File.Exists(PartialOf(path)), "the partial file to be deleted");

		File.Exists(path).Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task AnInvalidRange_ClearsTheCountThatLanded()
	{
		_reader.OnCount = _ => Task.FromResult(Result.Ok(412_000L));
		Advance(CsvExportViewModel.CountThrottle.Ticks);
		await SettleAsync(() => ViewModel.RowCount is not null);

		ViewModel.Form.EndTimeText = "tomorrow";
		Advance(CsvExportViewModel.CountThrottle.Ticks);
		await SettleAsync(() => ViewModel.RowCount is null, "the count to clear");

		ViewModel.CountText.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void ASecondEditInsideTheThrottle_RestartsIt()
	{
		Advance(CsvExportViewModel.CountThrottle.Ticks - 1);
		ViewModel.Form.EndTimeText = "10:00:00";
		Advance(CsvExportViewModel.CountThrottle.Ticks - 1);
		_reader.CountCalls.Should().BeEmpty();

		Advance(1);

		_reader.CountCalls.Should().ContainSingle().Which.To.Should().Be(new DateTime(2026, 10, 13, 10, 0, 0));
	}

	[AvaloniaFact]
	public async Task ASupersededCount_CompletingLate_DoesNotLand()
	{
		var gate = new TaskCompletionSource<Result<long>>(TaskCreationOptions.RunContinuationsAsynchronously);
		Task<Result<long>>? oldRangeCount = null;

		_reader.OnCount = call => call.To == _endWallClock
			? oldRangeCount = Served(gate.Task)
			: Task.FromResult(Result.Ok(5L));
		Advance(CsvExportViewModel.CountThrottle.Ticks);
		ViewModel.Form.EndTimeText = "10:00:00";
		Advance(CsvExportViewModel.CountThrottle.Ticks);
		await SettleAsync(() => ViewModel.RowCount == 5);

		gate.SetResult(Result.Ok(99L));
		await oldRangeCount!;
		Advance(1);

		ViewModel.RowCount.Should().Be(5);
	}

	[AvaloniaFact]
	public async Task AnOldRangeCount_CompletingInsideTheThrottleWindow_DoesNotLand()
	{
		var gate = new TaskCompletionSource<Result<long>>(TaskCreationOptions.RunContinuationsAsynchronously);
		Task<Result<long>>? oldRangeCount = null;

		_reader.OnCount = call => call.To == _endWallClock
			? oldRangeCount = Served(gate.Task)
			: new TaskCompletionSource<Result<long>>().Task;
		Advance(CsvExportViewModel.CountThrottle.Ticks);
		var running = _reader.CountCalls.Should().ContainSingle().Subject;

		ViewModel.Form.EndTimeText = "10:00:00";

		running.Token.IsCancellationRequested.Should().BeTrue("the edit cancels the count that is still running");

		Advance(CsvExportViewModel.CountThrottle.Ticks - 1);
		gate.SetResult(Result.Ok(99L));
		await oldRangeCount!;
		Advance(1);

		ViewModel.RowCount.Should().BeNull("the old range's count finished inside the throttle window");
	}

	[AvaloniaFact]
	public async Task AfterAnExport_WideningTheRangePastTheExcelLimit_ShowsTheNotice()
	{
		_reader.OnCount = _ => Task.FromResult(Result.Ok(1_000L));
		Advance(CsvExportViewModel.CountThrottle.Ticks);
		await SettleAsync(() => ViewModel.RowCount == 1_000);
		await ViewModel.ExportCommand.Execute(PathOf("before-widening.csv")).ToTask();
		ViewModel.StatusText.Should().NotBeEmpty();

		_reader.OnCount = _ => Task.FromResult(Result.Ok(ExportMessageLine.ExcelDataRowLimit + 1));
		ViewModel.Form.EndTimeText = "10:00:00";

		ViewModel.StatusText.Should().BeEmpty("the line described a range that is no longer in the form");
		Advance(CsvExportViewModel.CountThrottle.Ticks);
		await SettleAsync(() => ViewModel.RowCount > ExportMessageLine.ExcelDataRowLimit);

		ViewModel.Message.Should().Be(ExportMessageLine.ExcelNotice);
	}

	[AvaloniaFact]
	public async Task TheMessageLine_IsAnError_ForAFormProblemAndAFailedExport_AndNotForASuccess()
	{
		ViewModel.IsMessageError.Should().BeFalse();

		await ViewModel.ExportCommand.Execute(PathOf("fine.csv")).ToTask();
		ViewModel.IsMessageError.Should().BeFalse();

		ViewModel.Form.EndTimeText = "tomorrow";
		ViewModel.IsMessageError.Should().BeTrue();

		ViewModel.Form.EndTimeText = "09:00:00";
		ViewModel.IsMessageError.Should().BeFalse();

		await ViewModel.ExportCommand.Execute(UnwritablePath()).ToTask();
		ViewModel.IsMessageError.Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task AfterASuccess_TheToolTipIsTheFullPath_AndARangeEditClearsIt()
	{
		var path = PathOf("tip.csv");

		ViewModel.MessageToolTip.Should().BeNull();

		await ViewModel.ExportCommand.Execute(path).ToTask();

		ViewModel.MessageToolTip.Should().Be(path);

		ViewModel.Form.EndTimeText = "10:00:00";

		ViewModel.MessageToolTip.Should().BeNull();
	}

	[AvaloniaFact]
	public async Task AFailedExport_AndACancelledOne_LeaveTheToolTipNull()
	{
		await ViewModel.ExportCommand.Execute(PathOf("shown.csv")).ToTask();
		ViewModel.MessageToolTip.Should().NotBeNull();

		await ViewModel.ExportCommand.Execute(UnwritablePath()).ToTask();
		ViewModel.MessageToolTip.Should().BeNull();

		await ViewModel.ExportCommand.Execute(PathOf("shown-again.csv")).ToTask();
		ViewModel.MessageToolTip.Should().NotBeNull();

		var secondSliceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		BlockAtSecondSlice(secondSliceReached, call => Task.Delay(Timeout.Infinite, call.Token));

		var export = ViewModel.ExportCommand.Execute(PathOf("cancelled-tip.csv")).ToTask();

		await secondSliceReached.Task;
		await ViewModel.CancelCommand.Execute().ToTask();
		await export;

		ViewModel.MessageToolTip.Should().BeNull();
	}

	[AvaloniaFact]
	public async Task AFormProblemOverASuccess_ShowsNoToolTip()
	{
		await ViewModel.ExportCommand.Execute(PathOf("problem.csv")).ToTask();
		ViewModel.MessageToolTip.Should().NotBeNull();

		ViewModel.Form.EndTimeText = "tomorrow";

		ViewModel.MessageToolTip.Should().BeNull();
	}

	[AvaloniaFact]
	public async Task AReaderThatThrows_ShowsTheThrownTitle_ReportsOnce_AndLeavesNoFile()
	{
		var path = PathOf("thrown.csv");

		_reader.OnSlice = _ => throw new InvalidOperationException("the reader broke");

		await ViewModel.ExportCommand.Execute(path).ToTask();

		ViewModel.StatusText.Should().Be(Resources.FailureThrownTitle);
		ViewModel.IsStatusFailure.Should().BeTrue();
		_messagePanel.Entries.Should().ContainSingle();
		File.Exists(path).Should().BeFalse();
		File.Exists(PartialOf(path)).Should().BeFalse();
	}

	[AvaloniaFact]
	public void ReportFailure_ShowsTheThrownTitle_AndAddsOneEntryToThePanel()
	{
		ViewModel.ReportFailure(new InvalidOperationException("the picker broke"));

		ViewModel.StatusText.Should().Be(Resources.FailureThrownTitle);
		_messagePanel.Entries.Should().ContainSingle();
	}

	[AvaloniaFact]
	public async Task AnExportOverADirectory_FailsTheMove_LeavesNoPartial_AndKeepsThePath()
	{
		var path = PathOf("taken.csv");

		Directory.CreateDirectory(path);

		await ViewModel.ExportCommand.Execute(path).ToTask();

		ViewModel.StatusText.Should().Be(Resources.FailureExportFileTitle);
		File.Exists(PartialOf(path)).Should().BeFalse();
		Directory.Exists(path).Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task APartialFileThatCannotBeDeleted_IsNamedInTheReportedFailure()
	{
		var path = PathOf("stuck.csv");
		var partialPath = PartialOf(path);

		Directory.CreateDirectory(partialPath);
		await File.WriteAllTextAsync(Path.Combine(partialPath, "held"), "x", TestContext.Current.CancellationToken);

		await ViewModel.ExportCommand.Execute(path).ToTask();

		ViewModel.StatusText.Should().Be(Resources.FailureExportFileTitle);
		_messagePanel.Entries.Should().ContainSingle()
			.Which.View.Detail.Should().Contain(partialPath);
	}

	[AvaloniaFact]
	public void TheSuggestedName_FollowsTheStartBoxsChoice()
	{
		var markerWallClock = _startWallClock.AddMinutes(20).AddSeconds(15).AddMilliseconds(400);
		var marker = new StoredMarker(
			1, CsvExportStand.ToUtc(markerWallClock), "RecipeName.csv", MarkerKind.RecipeStart);
		var unlabelled = new StoredMarker(2, CsvExportStand.ToUtc(markerWallClock), null, MarkerKind.User);
		using var viewModel = CsvExportStand.Build(
			_reader,
			_messagePanel,
			_scheduler,
			[new ExportPenChoice(CsvExportStand.Pressure, true)],
			markers: [marker, unlabelled]);

		viewModel.SuggestedFileName().Should().Be("semiplot_20261013_080000-20261013_090000.csv");

		viewModel.SelectedStartMarker = viewModel.StartMarkers[0];

		viewModel.SuggestedFileName().Should().Be("RecipeName_2026-10-13_08-20.csv");

		viewModel.SelectedStartMarker = viewModel.StartMarkers[1];

		viewModel.SuggestedFileName()
			.Should().Be("semiplot_20261013_082015-20261013_090000.csv", "an unlabelled marker names nothing");

		viewModel.SelectedStartMarker = viewModel.StartMarkers[0];
		viewModel.SelectedStartMarker = viewModel.StartChoices[0];

		viewModel.SuggestedFileName()
			.Should().Be("semiplot_20261013_082015-20261013_090000.csv", "no marker keeps the range name");

		viewModel.SelectedStartMarker = viewModel.StartMarkers[0];
		viewModel.Form.StartTimeText = "08:20:16";

		viewModel.SuggestedFileName().Should().Be("semiplot_20261013_082016-20261013_090000.csv");

		viewModel.Form.StartTimeText = "08:20:15";

		viewModel.SuggestedFileName()
			.Should().Be("semiplot_20261013_082015-20261013_090000.csv", "a typed time names nothing, the box does");

		viewModel.Form.StartTimeText = "08:20";

		viewModel.SuggestedFileName().Should().BeNull("an invalid range proposes no name");
	}

	private CsvExportViewModel BuildNamesakes(bool secondIsTicked)
	{
		SetSlices(call => call.From == _startWallClock
			? [.. call.PenIds.Select(penId => new StoredArchiveRow(penId, call.From, 1))]
			: []);

		return CsvExportStand.Build(
			_reader,
			_messagePanel,
			_scheduler,
			ExportPenChoice.Checklist(
			[
				(new Pen(21, "Damper 01", [], "#1F77B4"), true),
				(new Pen(22, "Damper 01", [], "#FF7F0E"), secondIsTicked)
			]));
	}

	private static ArchiveError Archive(ArchiveFault fault)
	{
		return new ArchiveError(fault, "scada-host", 5432, "semiplot", "3");
	}
}
