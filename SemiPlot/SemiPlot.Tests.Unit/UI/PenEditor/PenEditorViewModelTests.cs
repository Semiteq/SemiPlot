using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.PenEditor;

using Xunit;

using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorFixture;

namespace SemiPlot.Tests.Unit.UI.PenEditor;

// The added count reads the resource set the UI culture selects, and a text sort compares under the
// current culture.
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class PenEditorViewModelTests : IDisposable
{
	private static readonly StoredPen _registered = NumberedPen(4242);

	private static readonly PenCatalogue _catalogue = new([Argon, Pressure, Power], [Chamber, Gas]);

	private readonly FakePenCatalogueEditor _editor = new();

	private readonly MessagePanelViewModel _messagePanel = new();

	public void Dispose()
	{
		_messagePanel.Dispose();
	}

	[AvaloniaFact]
	public void TheTable_ShowsThePensAsReadWithTheNamesOfTheirGroups()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);

		editor.Rows.Select(row => row.Pen).Should().Equal(Argon, Pressure, Power);
		editor.Rows.Select(row => row.GroupsText).Should().Equal("Chamber, Gas", "Chamber", string.Empty);
		editor.SelectedRow.Should().BeNull();
		editor.SelectedForm.Should().BeNull();
		editor.AddedCountText.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void SelectingARow_BuildsItsFormAndClearingTheSelectionDropsIt()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);

		editor.SelectedRow = editor.Rows[1];

		editor.SelectedForm.Should().NotBeNull();
		editor.SelectedForm!.Row.Should().BeSameAs(editor.Rows[1]);
		editor.SelectedForm.Name.Should().Be(Pressure.Name);

		editor.SelectedRow = null;

		editor.SelectedForm.Should().BeNull();
	}

	[AvaloniaFact]
	public async Task Refresh_RegistersRereadsAndShowsTheNewRowsTheAddedCountAndTheSelectedPen()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		editor.SelectedRow = editor.Rows[1];
		_editor.RegisterResult = Result.Ok(1);
		_editor.ReadResult = Result.Ok(new PenCatalogue([Argon, Pressure, Power, _registered], [Chamber, Gas]));

		await editor.RefreshCommand.Execute();

		_editor.Calls.Should().Equal(new FakeEditorCall.RegisterNewPens(), new FakeEditorCall.Read());
		editor.Rows.Select(row => row.Pen).Should().Equal(Argon, Pressure, Power, _registered);
		editor.AddedCountText.Should().Be(Resources.FormatPenEditorAddedCount(1));
		editor.SelectedRow.Should().NotBeNull();
		editor.SelectedRow!.Pen.Should().Be(Pressure);
		editor.SelectedForm!.Row.Should().BeSameAs(editor.SelectedRow);
	}

	[AvaloniaFact]
	public async Task Refresh_ClearsTheSelectionWhenTheSelectedPenIsGone()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		editor.SelectedRow = editor.Rows[2];
		_editor.ReadResult = Result.Ok(new PenCatalogue([Argon, Pressure], [Chamber, Gas]));

		await editor.RefreshCommand.Execute();

		editor.Rows.Select(row => row.Pen).Should().Equal(Argon, Pressure);
		editor.SelectedRow.Should().BeNull();
		editor.SelectedForm.Should().BeNull();
	}

	[AvaloniaFact]
	public async Task Refresh_KeepsTheSortOrder()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		editor.SortCommand.Execute(PenColumn.Id).Subscribe();
		editor.SortCommand.Execute(PenColumn.Id).Subscribe();
		_editor.ReadResult = Result.Ok(new PenCatalogue([Argon, Pressure, Power, _registered], []));

		await editor.RefreshCommand.Execute();

		editor.Rows.Select(row => row.Pen.Id).Should().Equal(4242, 12, 7, 3);
	}

	[AvaloniaFact]
	public async Task AFailedRegistration_ReportsOnceReadsNothingAndLeavesTheTable()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var rows = editor.Rows;
		_editor.RegisterResult = Result.Fail(
			FakePenCatalogueEditor.Refusal(ArchiveFault.TableMissing, "semiplot_register_new_pens()"));

		await editor.RefreshCommand.Execute();

		_editor.Calls.Should().Equal(new FakeEditorCall.RegisterNewPens());
		editor.Rows.Should().BeSameAs(rows);
		editor.AddedCountText.Should().BeEmpty();
		_messagePanel.Entries.Should().ContainSingle();
	}

	[AvaloniaFact]
	public async Task AFailedRead_ReportsOnceShowsTheAddedCountAndLeavesTheTable()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var rows = editor.Rows;
		_editor.RegisterResult = Result.Ok(2);
		_editor.ReadResult = Result.Fail(FakePenCatalogueEditor.Refusal(ArchiveFault.Unreachable, "semiplot_tags"));

		await editor.RefreshCommand.Execute();

		_editor.Calls.Should().Equal(new FakeEditorCall.RegisterNewPens(), new FakeEditorCall.Read());
		editor.Rows.Should().BeSameAs(rows);
		editor.AddedCountText.Should().Be(Resources.FormatPenEditorAddedCount(2), "the registration did succeed");
		_messagePanel.Entries.Should().ContainSingle();
	}

	[AvaloniaFact]
	public async Task WhenIdle_DuringARefresh_CompletesOnlyAfterTheReadHasRebuiltTheTable()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var registration = _editor.HoldNextCall();
		var read = _editor.HoldNextCall();
		_editor.ReadResult = Result.Ok(new PenCatalogue([Argon, Pressure, Power, _registered], [Chamber, Gas]));

		var refreshed = editor.RefreshCommand.Execute().ToTask();
		var idle = editor.WhenIdleAsync();
		registration.SetResult();
		Dispatcher.UIThread.RunJobs();

		idle.IsCompleted.Should().BeFalse("the read queued after registration is still held");

		read.SetResult();
		await idle;

		editor.Rows.Should().HaveCount(4, "the table was rebuilt before the editor went idle");
		await refreshed;
	}

	[AvaloniaFact]
	public async Task ACommitQueuedBehindTheRefreshRead_ReachesTheRebuiltTableAndForm()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		editor.SelectedRow = editor.Rows[1];
		var registration = _editor.HoldNextCall();
		var read = _editor.HoldNextCall();
		var refreshed = editor.RefreshCommand.Execute().ToTask();
		registration.SetResult();
		Dispatcher.UIThread.RunJobs();

		editor.SelectedForm!.Unit = "kPa";
		var committed = editor.SelectedForm.EndEditAsync(PenField.Unit);
		_editor.ReadResult = Result.Ok(_catalogue);
		read.SetResult();
		var written = Pressure with { Unit = "kPa" };
		_editor.ReadResult = Result.Ok(new PenCatalogue([Argon, written, Power], [Chamber, Gas]));
		await committed;
		await refreshed;

		_editor.Calls.Should().Equal(
			new FakeEditorCall.RegisterNewPens(),
			new FakeEditorCall.Read(),
			new FakeEditorCall.Change(Pressure, new PenSettingChange.Unit("kPa")),
			new FakeEditorCall.Read());
		editor.Rows[1].Pen.Unit.Should().Be("kPa");
		editor.SelectedForm!.Row.Should().BeSameAs(editor.Rows[1]);
		editor.SelectedForm.Unit.Should().Be("kPa");
	}

	[AvaloniaFact]
	public async Task AFormBuiltWhileItsRowsWriteIsHeld_ShowsTheQueuedValueAndAnUntouchedFieldWritesNothing()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		editor.SelectedRow = editor.Rows[1];
		var first = editor.SelectedForm!;
		first.Name = "Renamed";
		var gate = _editor.HoldNextCall();
		var held = first.EndEditAsync(PenField.Name);

		editor.SelectedRow = editor.Rows[0];
		editor.SelectedRow = editor.Rows[1];
		var second = editor.SelectedForm!;

		second.Should().NotBeSameAs(first);
		second.Name.Should().Be("Renamed", "the form seeds from the row as queued");

		gate.SetResult();
		await held;
		await second.EndEditAsync(PenField.Name);
		await editor.WhenIdleAsync();

		_editor.Changes.Select(change => change.Setting).Should().Equal(new PenSettingChange.Name("Renamed"));
		editor.Rows[1].Pen.Name.Should().Be("Renamed");
	}

	[AvaloniaFact]
	public async Task AFormBuiltWhileItsRowsWriteIsHeld_FollowsTheRowBackWhenTheWriteFails()
	{
		_editor.ChangeResult = Result.Fail(FakePenCatalogueEditor.Refusal(ArchiveFault.NameTaken, "Renamed"));
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		editor.SelectedRow = editor.Rows[1];
		var first = editor.SelectedForm!;
		first.Name = "Renamed";
		var gate = _editor.HoldNextCall();
		var held = first.EndEditAsync(PenField.Name);

		editor.SelectedRow = editor.Rows[0];
		editor.SelectedRow = editor.Rows[1];
		var second = editor.SelectedForm!;
		second.Name.Should().Be("Renamed");
		second.Unit = "bar";

		gate.SetResult();
		await held;
		await second.EndEditAsync(PenField.Name);
		await editor.WhenIdleAsync();

		second.Name.Should().Be(Pressure.Name, "an untouched draft follows the row back");
		second.Unit.Should().Be("bar", "an edited draft stays as typed");
		_editor.Changes.Select(change => change.Setting).Should().Equal(new PenSettingChange.Name("Renamed"));
	}

	[AvaloniaFact]
	public async Task AFormWhoseRowIsNoLongerSelected_StopsFollowingTheRow()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		editor.SelectedRow = editor.Rows[1];
		var left = editor.SelectedForm!;
		editor.SelectedRow = editor.Rows[0];
		editor.SelectedRow = editor.Rows[1];

		editor.SelectedForm!.Name = "Renamed";
		await editor.SelectedForm.EndEditAsync(PenField.Name);

		left.Name.Should().Be(Pressure.Name);
		editor.SelectedForm.Name.Should().Be("Renamed");
	}

	[AvaloniaFact]
	public async Task ARefreshIssuedWhileACommitIsHeld_RegistersOnlyAfterTheCommitCompletes()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		editor.SelectedRow = editor.Rows[1];
		var gate = _editor.HoldNextCall();

		editor.SelectedForm!.Name = "Chamber pressure, main";
		var committed = editor.SelectedForm.EndEditAsync(PenField.Name);
		var refreshed = editor.RefreshCommand.Execute().ToTask();

		_editor.Calls.Should().ContainSingle("registration waits for the held commit");

		gate.SetResult();
		await committed;
		await refreshed;

		_editor.Calls.Should().Equal(
			new FakeEditorCall.Change(Pressure, new PenSettingChange.Name("Chamber pressure, main")),
			new FakeEditorCall.RegisterNewPens(),
			new FakeEditorCall.Read());
	}

	[AvaloniaTheory]
	[InlineData(PenColumn.Id, new[] { 3, 7, 12 })]
	[InlineData(PenColumn.EnabledOnStart, new[] { 3, 7, 12 })]
	[InlineData(PenColumn.Color, new[] { 12, 7, 3 })]
	[InlineData(PenColumn.Name, new[] { 3, 12, 7 })]
	[InlineData(PenColumn.Unit, new[] { 7, 3, 12 })]
	[InlineData(PenColumn.Mask, new[] { 3, 12, 7 })]
	[InlineData(PenColumn.LineStyle, new[] { 7, 12, 3 })]
	[InlineData(PenColumn.ScaleMinOnStart, new[] { 3, 7, 12 })]
	[InlineData(PenColumn.ScaleMaxOnStart, new[] { 3, 7, 12 })]
	[InlineData(PenColumn.Groups, new[] { 12, 7, 3 })]
	public void ASort_OrdersTheRowsByTheColumnThenById(PenColumn column, int[] expectedIds)
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);

		editor.SortCommand.Execute(column).Subscribe();

		editor.Rows.Select(row => row.Pen.Id).Should().Equal(expectedIds);
	}

	[AvaloniaFact]
	public void ASecondSortOnTheSameColumn_ReversesTheRowsAndKeepsTheSelectionAndItsForm()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var rows = editor.Rows.ToArray();
		editor.SelectedRow = rows[1];
		var form = editor.SelectedForm;
		form!.Unit = "kPa";

		editor.SortCommand.Execute(PenColumn.Name).Subscribe();
		editor.SortCommand.Execute(PenColumn.Name).Subscribe();

		editor.Rows.Should().Equal(rows[1], rows[2], rows[0]);
		editor.SelectedRow.Should().BeSameAs(rows[1]);
		editor.SelectedForm.Should().BeSameAs(form);
		editor.SelectedForm!.Unit.Should().Be("kPa", "the form keeps its drafts");
		_editor.Calls.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void ASortOnAnotherColumn_StartsAscending()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);

		editor.SortCommand.Execute(PenColumn.Name).Subscribe();
		editor.SortCommand.Execute(PenColumn.Id).Subscribe();

		editor.Rows.Select(row => row.Pen.Id).Should().Equal(3, 7, 12);
	}

	[AvaloniaFact]
	public async Task WhenIdle_CompletesOnlyAfterEveryQueuedCall()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		editor.SelectedRow = editor.Rows[1];
		var gate = _editor.HoldNextCall();

		editor.SelectedForm!.Name = "First";
		_ = editor.SelectedForm.EndEditAsync(PenField.Name);
		editor.SelectedForm.Unit = "kPa";
		_ = editor.SelectedForm.EndEditAsync(PenField.Unit);
		var idle = editor.WhenIdleAsync();

		idle.IsCompleted.Should().BeFalse();

		gate.SetResult();
		await idle;

		_editor.Changes.Should().HaveCount(2);
		editor.Rows[1].Pen.Should().Be(Pressure with { Name = "First", Unit = "kPa" });
	}

	[AvaloniaFact]
	public void ReportFailure_AddsOneMessagePanelEntry()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);

		editor.ReportFailure(new InvalidOperationException("the window's handler threw"));

		_messagePanel.Entries.Should().ContainSingle();
	}
}
