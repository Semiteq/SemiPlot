using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.PenEditor;

using Xunit;

using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorFixture;
using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorWindowDriver;
using static SemiPlot.Tests.Unit.UI.ViewProbe;

namespace SemiPlot.Tests.Unit.UI.PenEditor;

/// <summary>
/// The editor window's pen list and pen pane as the operator drives them: values read off realised controls,
/// input clicked.
/// </summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class PenEditorViewTests : IDisposable
{
	private static readonly Color _pickedColor = Color.Parse("#123456");

	private readonly FakePenCatalogueEditor _editor = new();

	private readonly MessagePanelViewModel _messagePanel = new();

	public void Dispose()
	{
		_messagePanel.Dispose();
	}

	[AvaloniaFact]
	public void ANameTypedThenThePenListsEmptyAreaClicked_WritesOnce()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var name = Named<TextBox>(window, "FormName");
			Retype(window, name, "Chamber pressure A");
			var rowsPresenter = Descendant<Control>(PenListOf(window), "PART_RowsPresenter");
			var belowTheLastRow = BottomOf(RowAt(window, 2), rowsPresenter) + PenEditorWindow.RowHeight;
			belowTheLastRow.Should().BeLessThan(rowsPresenter.Bounds.Height, "the list has room below its rows");

			HeadlessInput.Click(window, rowsPresenter, new Point(rowsPresenter.Bounds.Width / 2, belowTheLastRow));

			name.IsFocused.Should().BeFalse("a press on the list takes the keyboard from the field");
			_editor.Changes.Should().Equal(
				new FakeEditorCall.Change(Pressure, new PenSettingChange.Name("Chamber pressure A")));
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANameTypedThenAColumnHeaderClicked_WritesOnce()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var name = Named<TextBox>(window, "FormName");
			Retype(window, name, "Chamber pressure A");

			HeadlessInput.Click(window, HeaderOf(window, Resources.PenEditorColumnId));

			name.IsFocused.Should().BeFalse("a press on a header takes the keyboard from the field");
			_editor.Changes.Should().Equal(
				new FakeEditorCall.Change(Pressure, new PenSettingChange.Name("Chamber pressure A")));
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANameTypedThenTheSelectedRowCtrlClicked_WritesOnceAndShowsTheHint()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var row = RowAt(window, 1);
			HeadlessInput.Click(window, row);
			Retype(window, Named<TextBox>(window, "FormName"), "Chamber pressure A");

			HeadlessInput.Click(
				window, row, new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), RawInputModifiers.Control);

			_editor.Changes.Should().Equal(
				new FakeEditorCall.Change(Pressure, new PenSettingChange.Name("Chamber pressure A")));
			PenListOf(window).SelectedItem.Should().BeNull("a Ctrl+click on the selected row clears the selection");
			viewModel.SelectedRow.Should().BeNull("the view model follows the list");
			IsOnScreen(Named<TextBlock>(window, "PenFormEmptyHint")).Should().BeTrue();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANameTypedThenTheFilterBoxTypedAndEnterPressed_WritesOnceAndTheFilterWritesNothing()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			Retype(window, Named<TextBox>(window, "FormName"), "Gauge");

			HeadlessInput.Type(window, FilterBoxOf(window), "7");
			HeadlessInput.Press(window, PhysicalKey.Enter);

			_editor.Changes.Should().Equal(new FakeEditorCall.Change(Pressure, new PenSettingChange.Name("Gauge")));
			RealisedPens(PenListOf(window)).Select(pen => pen.Id).Should().Equal(7);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARefreshWithNoPenSelected_SelectsNoRowAndShowsTheHint()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			_editor.RegisterResult = Result.Ok(0);
			_editor.ReadResult = Result.Ok(ViewCatalogue);

			HeadlessInput.Click(window, Named<Button>(window, "PenEditorRefreshButton"));

			PenListOf(window).SelectedItem.Should().BeNull();
			viewModel.SelectedRow.Should().BeNull();
			IsOnScreen(Named<TextBlock>(window, "PenFormEmptyHint")).Should().BeTrue();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARefreshThatDropsTheSelectedPen_SelectsNoRowAndShowsTheHint()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			_editor.RegisterResult = Result.Ok(0);
			_editor.ReadResult = Result.Ok(new PenCatalogue([Argon, Power], [Chamber, Spare]));

			HeadlessInput.Click(window, Named<Button>(window, "PenEditorRefreshButton"));

			RealisedPens(PenListOf(window)).Should().Equal(Argon, Power);
			PenListOf(window).SelectedItem.Should().BeNull("the selected pen is gone");
			viewModel.SelectedRow.Should().BeNull();
			IsOnScreen(Named<TextBlock>(window, "PenFormEmptyHint")).Should().BeTrue();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANameTypedAndTabbedOut_WritesOnceAndTheRowShowsIt()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));

			Retype(window, Named<TextBox>(window, "FormName"), "Chamber pressure A");
			HeadlessInput.Press(window, PhysicalKey.Tab);

			_editor.Changes.Should().Equal(
				new FakeEditorCall.Change(Pressure, new PenSettingChange.Name("Chamber pressure A")));
			TextOf(RowAt(window, 1), "RowName").Should().Be("Chamber pressure A");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AnUnusableMask_WritesNothingRevertsMarksAndSaysWhyWithoutResizing()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var mask = Named<TextBox>(window, "FormMask");
			var panel = Named<Border>(window, "PenFormPanel");
			var windowBounds = window.Bounds;
			var panelBounds = panel.Bounds;

			Retype(window, mask, "%0.0");
			HeadlessInput.Press(window, PhysicalKey.Tab);

			_editor.Calls.Should().BeEmpty();
			mask.Text.Should().Be(Pressure.Format, "the field shows the committed mask again");
			mask.Classes.Should().Contain("invalid");
			Named<TextBlock>(window, "FormMessage").Text.Should().Be(Resources.PenFormMaskInvalid);
			window.Bounds.Should().Be(windowBounds);
			panel.Bounds.Should().Be(panelBounds);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANameTheArchiveRefuses_RevertsMarksAndReportsOnce()
	{
		var refusal = FakePenCatalogueEditor.Refusal(ArchiveFault.NameTaken, Pressure.Name);
		_editor.ChangeResult = Result.Fail(refusal);
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var name = Named<TextBox>(window, "FormName");

			Retype(window, name, "Argon flow");
			HeadlessInput.Press(window, PhysicalKey.Tab);

			_editor.Changes.Should().ContainSingle();
			name.Text.Should().Be(Pressure.Name);
			name.Classes.Should().Contain("invalid");
			Named<TextBlock>(window, "FormMessage").Text.Should().Be(ArchiveFailureMapper.Map(refusal).Title);
			_messagePanel.Entries.Should().ContainSingle();
			TextOf(RowAt(window, 1), "RowName").Should().Be(Pressure.Name);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheScalePair_WritesOnceWhenFocusLeavesBothBounds()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 0));

			HeadlessInput.Type(window, Named<TextBox>(window, "FormScaleMinOnStart"), "5");
			HeadlessInput.Press(window, PhysicalKey.Tab);

			Named<TextBox>(window, "FormScaleMaxOnStart").IsFocused.Should()
				.BeTrue("Tab moves from the minimum to the maximum");
			_editor.Calls.Should().BeEmpty("half a pair is never written");

			HeadlessInput.Type(window, Named<TextBox>(window, "FormScaleMaxOnStart"), "50");
			HeadlessInput.Press(window, PhysicalKey.Tab);

			Named<Grid>(window, "FormScale").IsKeyboardFocusWithin.Should().BeFalse();
			_editor.Changes.Should().Equal(new FakeEditorCall.Change(Argon, new PenSettingChange.ScaleOnStart(5, 50)));
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANameTypedThenAnotherRowClicked_WritesToThePenTheFieldShowed()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 0));
			Retype(window, Named<TextBox>(window, "FormName"), "Argon");

			HeadlessInput.Click(window, RowAt(window, 1));

			_editor.Changes.Should().Equal(new FakeEditorCall.Change(Argon, new PenSettingChange.Name("Argon")));
			viewModel.SelectedRow!.Pen.Should().Be(Pressure);
			Named<TextBox>(window, "FormName").Text.Should().Be(Pressure.Name);
			TextOf(RowAt(window, 0), "RowName").Should().Be("Argon");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void BothBoundsTypedThenAnotherRowClicked_WritesOneScaleChangeToTheFirstPen()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 0));
			HeadlessInput.Type(window, Named<TextBox>(window, "FormScaleMinOnStart"), "1");
			HeadlessInput.Type(window, Named<TextBox>(window, "FormScaleMaxOnStart"), "2");

			HeadlessInput.Click(window, RowAt(window, 1));

			_editor.Changes.Should().Equal(new FakeEditorCall.Change(Argon, new PenSettingChange.ScaleOnStart(1, 2)));
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANameTypedThenAFormLabelClicked_WritesOnce()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var name = Named<TextBox>(window, "FormName");
			Retype(window, name, "Chamber pressure A");

			HeadlessInput.Click(window, FormLabel(window, Resources.PenEditorColumnName));
			HeadlessInput.Click(window, FormLabel(window, Resources.PenEditorColumnUnit));

			name.IsFocused.Should().BeFalse("a click on a label takes the keyboard from the field");
			_editor.Changes.Should().Equal(
				new FakeEditorCall.Change(Pressure, new PenSettingChange.Name("Chamber pressure A")));
			TextOf(RowAt(window, 1), "RowName").Should().Be("Chamber pressure A");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void BothBoundsTypedThenTheFormsEmptyAreaClicked_WritesOneScaleChange()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 0));
			var message = Named<TextBlock>(window, "FormMessage");
			HeadlessInput.Type(window, Named<TextBox>(window, "FormScaleMinOnStart"), "1");
			HeadlessInput.Type(window, Named<TextBox>(window, "FormScaleMaxOnStart"), "2");
			message.Text.Should().BeNullOrEmpty("the empty message line is the form's empty area");

			HeadlessInput.Click(window, message);

			Named<Grid>(window, "FormScale").IsKeyboardFocusWithin.Should().BeFalse();
			_editor.Changes.Should().Equal(new FakeEditorCall.Change(Argon, new PenSettingChange.ScaleOnStart(1, 2)));
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANameTypedThenTheWindowClosed_WritesOnceBeforeTheWindowIsGone()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		int? changesWhenClosed = null;
		window.Closed += (_, _) => changesWhenClosed = _editor.Changes.Count();

		HeadlessInput.Click(window, RowAt(window, 0));
		Retype(window, Named<TextBox>(window, "FormName"), "Argon");
		var write = _editor.HoldNextCall();

		window.Close();
		Dispatcher.UIThread.RunJobs();
		window.Close();
		Dispatcher.UIThread.RunJobs();

		window.IsVisible.Should().BeTrue("the close waits for the write in flight");
		_editor.Changes.Should().Equal(new FakeEditorCall.Change(Argon, new PenSettingChange.Name("Argon")));

		write.SetResult();
		Dispatcher.UIThread.RunJobs();

		window.IsVisible.Should().BeFalse();
		changesWhenClosed.Should().Be(1, "the name is written once, before the window is gone");
	}

	[AvaloniaFact]
	public void ALineStyleChosenAndTheStartBoxClicked_WriteOneChangeEach()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 0));

			ChooseLineStyle(window, Named<ComboBox>(window, "FormLineStyle"), PenLineStyle.Interpolated);
			HeadlessInput.Click(window, Named<CheckBox>(window, "FormEnabledOnStart"));

			_editor.Changes.Should().Equal(
				new FakeEditorCall.Change(Argon, new PenSettingChange.LineStyle(PenLineStyle.Interpolated)),
				new FakeEditorCall.Change(
					Argon with { LineStyle = PenLineStyle.Interpolated }, new PenSettingChange.EnabledOnStart(true)));
			Descendant<PathIcon>(RowAt(window, 0), "RowEnabledOnStart").IsVisible.Should()
				.BeTrue("the row shows the flag written");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ASwatchPickedInThePicker_WritesOneColourChange()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));

			var picked = PickColor(window);

			var hex = PenColorConverters.Format(picked);
			_editor.Changes.Should().Equal(new FakeEditorCall.Change(Pressure, new PenSettingChange.Color(hex)));
			Named<TextBox>(window, "FormColor").Text.Should().Be(hex);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AfterAPickInOneRow_ThePickerShowsTheNextRowsColour()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var picker = Named<ColorPicker>(window, "FormColorPicker");
			HeadlessInput.Click(window, RowAt(window, 1));
			PickColor(window);

			HeadlessInput.Click(window, RowAt(window, 0));

			picker.Color.Should().Be(Color.Parse(Argon.Color!), "the picker still reads the selected pen's draft");
			_editor.Changes.Should().ContainSingle();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void APenWithNoColour_OpeningAndDismissingThePicker_WritesNothing()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 2));
			var flyout = ColorFlyoutOf(window);

			HeadlessInput.Click(window, Named<ColorPicker>(window, "FormColorPicker"));
			flyout.IsOpen.Should().BeTrue();
			HeadlessInput.Press(TopLevelOf((Control)flyout.Content!), PhysicalKey.Escape);

			flyout.IsOpen.Should().BeFalse();
			_editor.Calls.Should().BeEmpty();
			Named<TextBox>(window, "FormColor").Classes.Should()
				.Contain("invalid", "a pen with no colour opens invalid");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ALowerCaseHexTypedWithoutTheHash_WritesTheUpperCaseColour()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 2));
			var color = Named<TextBox>(window, "FormColor");

			HeadlessInput.Type(window, color, "1f77b4");
			HeadlessInput.Press(window, PhysicalKey.Tab);

			_editor.Changes.Should().Equal(new FakeEditorCall.Change(Power, new PenSettingChange.Color("#1F77B4")));
			color.Text.Should().Be("#1F77B4");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AStoredLowerCaseColour_ShowsAsStoredAndWritesNothingOnFocusAndLeave()
	{
		var lowerCaseArgon = Argon with { Color = "#1f77b4" };
		using var viewModel = _editor.EditorOver(new PenCatalogue([lowerCaseArgon, Pressure, Power], []), _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 0));
			var color = Named<TextBox>(window, "FormColor");
			color.Text.Should().Be("#1f77b4", "the box shows the colour as stored");

			HeadlessInput.Click(window, color);
			HeadlessInput.Press(window, PhysicalKey.Tab);

			color.Text.Should().Be("#1f77b4");
			_editor.Calls.Should().BeEmpty("a focus and a leave are no edit");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARefusedDraftRevertedOnEnter_ShowsTheStoredColourAsStored()
	{
		var namedColorArgon = Argon with { Color = "red" };
		using var viewModel = _editor.EditorOver(new PenCatalogue([namedColorArgon, Pressure, Power], []), _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 0));
			var color = Named<TextBox>(window, "FormColor");

			HeadlessInput.Type(window, color, "1");
			HeadlessInput.Press(window, PhysicalKey.Enter);

			color.Text.Should().Be("red", "the revert is a binding write and stays as set");
			color.Classes.Should().Contain("invalid");
			Named<TextBlock>(window, "FormMessage").Text.Should().Be(Resources.PenFormColorInvalid);
			_editor.Changes.Should().BeEmpty();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARefreshThatChangesTheCatalogue_RebindsThePenListAndTheGroupsPane()
	{
		var registered = NumberedPen(4242);
		var vacuum = new StoredGroup(9, "Vacuum", [4242]);
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var groupsBefore = viewModel.Groups;
			_editor.RegisterResult = Result.Ok(1);
			_editor.ReadResult = Result.Ok(
				new PenCatalogue([Argon, Pressure, Power, registered], [Chamber, Spare, vacuum]));

			HeadlessInput.Click(window, Named<Button>(window, "PenEditorRefreshButton"));

			TextOf(RowAt(window, 3), "RowName").Should().Be(registered.Name);
			TextOf(RowAt(window, 3), "RowGroups").Should().Be(vacuum.Name);

			viewModel.Groups.Should().NotBeSameAs(groupsBefore);
			GroupListOf(window).ItemsSource.Should().BeSameAs(viewModel.Groups.Groups);
			TextOf(GroupRowAt(window, 2), "GroupName").Should().Be(vacuum.Name);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANameTypedThenRefreshClicked_WritesBeforeTheRegistration()
	{
		_editor.ReadResult = Result.Ok(ViewCatalogue);
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			Retype(window, Named<TextBox>(window, "FormName"), "Chamber pressure A");

			HeadlessInput.Click(window, Named<Button>(window, "PenEditorRefreshButton"));
			HeadlessInput.Press(window, PhysicalKey.Tab);

			_editor.Calls.Should().Equal(
				new FakeEditorCall.Change(Pressure, new PenSettingChange.Name("Chamber pressure A")),
				new FakeEditorCall.RegisterNewPens(),
				new FakeEditorCall.Read());
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANameTypedWhileARefreshRuns_ReachesNoFieldAndWritesNothing()
	{
		_editor.ReadResult = Result.Ok(ViewCatalogue);
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var name = Named<TextBox>(window, "FormName");
			var registration = _editor.HoldNextCall();
			HeadlessInput.Click(window, Named<Button>(window, "PenEditorRefreshButton"));

			name.IsEffectivelyEnabled.Should().BeFalse("the form waits for the rebuild");
			HeadlessInput.Type(window, name, " typed during refresh");
			registration.SetResult();
			Dispatcher.UIThread.RunJobs();
			name.IsEffectivelyEnabled.Should().BeTrue("the rebuild finished");
			HeadlessInput.Click(window, name);
			HeadlessInput.Press(window, PhysicalKey.Tab);

			name.Text.Should().Be(Pressure.Name);
			_editor.Calls.Should().Equal(new FakeEditorCall.RegisterNewPens(), new FakeEditorCall.Read());
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANameEnteredWhileItsWriteIsHeldThenTabbedOut_WritesOnce()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			Retype(window, Named<TextBox>(window, "FormName"), "Chamber pressure A");
			var write = _editor.HoldNextCall();

			HeadlessInput.Press(window, PhysicalKey.Enter);
			HeadlessInput.Press(window, PhysicalKey.Tab);
			write.SetResult();
			Dispatcher.UIThread.RunJobs();

			_editor.Changes.Should().Equal(
				new FakeEditorCall.Change(Pressure, new PenSettingChange.Name("Chamber pressure A")));
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANameEnteredThenTypedOnAndTabbedOut_WritesEachEditOnce()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var name = Named<TextBox>(window, "FormName");
			Retype(window, name, "Chamber");

			HeadlessInput.Press(window, PhysicalKey.Enter);
			HeadlessInput.Type(window, name, " A");
			HeadlessInput.Press(window, PhysicalKey.Tab);

			name.Text.Should().NotBe("Chamber", "the edit after Enter reached the field");
			_editor.Changes.Select(call => call.Setting).Should().Equal(
				new PenSettingChange.Name("Chamber"),
				new PenSettingChange.Name(name.Text!));
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AScalePairEnteredWhileItsWriteIsHeldThenTabbedOut_WritesOnce()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 0));
			HeadlessInput.Type(window, Named<TextBox>(window, "FormScaleMinOnStart"), "1");
			HeadlessInput.Type(window, Named<TextBox>(window, "FormScaleMaxOnStart"), "2");
			var write = _editor.HoldNextCall();

			HeadlessInput.Press(window, PhysicalKey.Enter);
			HeadlessInput.Press(window, PhysicalKey.Tab);
			write.SetResult();
			Dispatcher.UIThread.RunJobs();

			Named<Grid>(window, "FormScale").IsKeyboardFocusWithin.Should().BeFalse();
			_editor.Changes.Should().Equal(new FakeEditorCall.Change(Argon, new PenSettingChange.ScaleOnStart(1, 2)));
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AScalePairTypedThenTheWindowClosed_WritesOnceBeforeTheWindowIsGone()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		int? changesWhenClosed = null;
		window.Closed += (_, _) => changesWhenClosed = _editor.Changes.Count();

		HeadlessInput.Click(window, RowAt(window, 0));
		HeadlessInput.Type(window, Named<TextBox>(window, "FormScaleMinOnStart"), "1");
		HeadlessInput.Type(window, Named<TextBox>(window, "FormScaleMaxOnStart"), "2");
		var write = _editor.HoldNextCall();

		window.Close();
		Dispatcher.UIThread.RunJobs();

		window.IsVisible.Should().BeTrue("the close waits for the write in flight");
		_editor.Changes.Should().Equal(new FakeEditorCall.Change(Argon, new PenSettingChange.ScaleOnStart(1, 2)));

		write.SetResult();
		Dispatcher.UIThread.RunJobs();

		window.IsVisible.Should().BeFalse();
		changesWhenClosed.Should().Be(1, "the pair is written once, before the window is gone");
	}

	[AvaloniaFact]
	public void AfterChoicesInOneRow_TheComboAndTheBoxShowTheNextRowsValues()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var lineStyle = Named<ComboBox>(window, "FormLineStyle");
			var onStart = Named<CheckBox>(window, "FormEnabledOnStart");
			HeadlessInput.Click(window, RowAt(window, 1));
			ChooseLineStyle(window, lineStyle, PenLineStyle.Stepped);
			HeadlessInput.Click(window, onStart);

			HeadlessInput.Click(window, RowAt(window, 2));

			lineStyle.SelectedItem.Should().Be(Power.LineStyle, "the combo still reads the selected pen's draft");
			onStart.IsChecked.Should().Be(Power.EnabledOnStart, "the box still reads the selected pen's draft");
			_editor.Changes.Should().HaveCount(2, "showing another pen writes nothing");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AStartBoxTheArchiveRefuses_PutsTheBoxBackSaysWhyAndReportsOnce()
	{
		var refusal = FakePenCatalogueEditor.Refusal(ArchiveFault.RowGone, Pressure.Name);
		_editor.ChangeResult = Result.Fail(refusal);
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var onStart = Named<CheckBox>(window, "FormEnabledOnStart");

			HeadlessInput.Click(window, onStart);

			onStart.IsChecked.Should().Be(Pressure.EnabledOnStart, "the refused choice puts the box back");
			_editor.Changes.Should().ContainSingle("the box put back writes nothing");
			Named<TextBlock>(window, "FormMessage").Text.Should().Be(ArchiveFailureMapper.Map(refusal).Title);
			_messagePanel.Entries.Should().ContainSingle();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheLogBoxClicked_WritesTheFlag()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 0));
			var box = Named<CheckBox>(window, "FormLogScaleOnStart");
			box.IsChecked.Should().Be(Argon.LogScaleOnStart);

			HeadlessInput.Click(window, box);

			_editor.Changes.Should().Equal(
				new FakeEditorCall.Change(Argon, new PenSettingChange.LogScaleOnStart(true)));
			box.IsChecked.Should().BeTrue();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheLogBoxOverAStoredMinimumOfZero_PutsTheBoxBackAndSaysWhyWithoutResizing()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var box = Named<CheckBox>(window, "FormLogScaleOnStart");
			var panel = Named<Border>(window, "PenFormPanel");
			var message = Named<TextBlock>(window, "FormMessage");
			var panelBounds = panel.Bounds;

			HeadlessInput.Click(window, box);

			_editor.Calls.Should().BeEmpty();
			box.IsChecked.Should().BeFalse("the refused tick puts the box back");
			message.Text.Should().Be(Resources.ScaleLogMinimumPositive);
			panel.Bounds.Should().Be(panelBounds);
			LeftOf(box, window).Should().BeApproximately(
				LeftOf(Named<TextBox>(window, "FormScaleMinOnStart"), window), 1, "the box lines up under the bounds");
			BottomOf(box, window).Should().BeLessThanOrEqualTo(
				TopOf(message, window), "the box sits above the message line");
			BottomOf(message, window).Should().BeLessThanOrEqualTo(
				BottomOf(panel, window), "the message line stays inside");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void APositiveMinimumTypedThenTheLogBoxClicked_WritesThePairThenTheFlag()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			Retype(window, Named<TextBox>(window, "FormScaleMinOnStart"), "5");
			var pairWrite = _editor.HoldNextCall();

			HeadlessInput.Click(window, Named<CheckBox>(window, "FormLogScaleOnStart"));

			_editor.Changes.Should().Equal(
				new FakeEditorCall.Change(Pressure, new PenSettingChange.ScaleOnStart(5, 100)));
			Named<CheckBox>(window, "FormLogScaleOnStart").IsChecked.Should().BeTrue(
				"the rule reads the minimum queued, not the 0 still stored");
			Named<TextBlock>(window, "FormMessage").Text.Should().BeNullOrEmpty();

			pairWrite.SetResult();
			Dispatcher.UIThread.RunJobs();

			var rescaled = Pressure with { ScaleMinOnStart = 5 };
			_editor.Changes.Should().Equal(
				new FakeEditorCall.Change(Pressure, new PenSettingChange.ScaleOnStart(5, 100)),
				new FakeEditorCall.Change(rescaled, new PenSettingChange.LogScaleOnStart(true)));
			Named<CheckBox>(window, "FormLogScaleOnStart").IsChecked.Should().BeTrue();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AZeroMinimumTypedWhileTheFlagIsOn_MarksTheMinimumAndWritesNothing()
	{
		var logArgon = Argon with { LogScaleOnStart = true };
		using var viewModel = _editor.EditorOver(new PenCatalogue([logArgon, Pressure, Power], []), _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 0));
			var minimum = Named<TextBox>(window, "FormScaleMinOnStart");
			var maximum = Named<TextBox>(window, "FormScaleMaxOnStart");

			HeadlessInput.Type(window, minimum, "0");
			HeadlessInput.Type(window, maximum, "100");

			minimum.Classes.Should().Contain("invalid");
			maximum.Classes.Should().NotContain("invalid", "the rule names the minimum");
			Named<TextBlock>(window, "FormMessage").Text.Should().Be(Resources.ScaleLogMinimumPositive);

			HeadlessInput.Click(window, Named<TextBlock>(window, "FormMessage"));

			Named<Grid>(window, "FormScale").IsKeyboardFocusWithin.Should().BeFalse();
			_editor.Calls.Should().BeEmpty();
			minimum.Text.Should().BeNullOrEmpty("the refused pair goes back to the stored empty pair");
			minimum.Classes.Should().Contain("invalid", "the refusal marks the pair");
		}
		finally
		{
			Close(window);
		}
	}

	private static void ChooseLineStyle(PenEditorWindow window, ComboBox lineStyle, PenLineStyle choice)
	{
		HeadlessInput.Pick(window, lineStyle, choice);
	}

	private static TextBlock FormLabel(PenEditorWindow window, string text)
	{
		return FormLabels(window).Single(label => label.Text == text);
	}

	private static TopLevel TopLevelOf(Control control)
	{
		return TopLevel.GetTopLevel(control) ?? throw new InvalidOperationException("The control is not realised.");
	}

	private static Flyout ColorFlyoutOf(PenEditorWindow window)
	{
		return Named<ColorPicker>(window, "FormColorPicker").GetVisualDescendants()
			.OfType<DropDownButton>()
			.Single()
			.Flyout.Should().BeOfType<Flyout>().Subject;
	}

	private static Color PickColor(PenEditorWindow window)
	{
		var picker = Named<ColorPicker>(window, "FormColorPicker");
		var flyout = ColorFlyoutOf(window);
		HeadlessInput.Click(window, picker);
		var topLevel = TopLevelOf(flyout.Content.Should().BeAssignableTo<Control>().Subject);

		picker.Color = _pickedColor;
		HeadlessInput.Press(topLevel, PhysicalKey.Escape);

		flyout.IsOpen.Should().BeFalse("Escape dismisses the picker");

		return _pickedColor;
	}
}
