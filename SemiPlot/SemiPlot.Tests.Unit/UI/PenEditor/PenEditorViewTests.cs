using System.Globalization;

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

namespace SemiPlot.Tests.Unit.UI.PenEditor;

/// <summary>
/// The editor window and its pens tab as the operator drives them: values read off realised controls, input
/// clicked.
/// </summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class PenEditorViewTests : IDisposable
{
	private const int FixedColumnCount = 10;

	private const int PaletteTabIndex = 1;

	private const string RussianRefreshLabel =
		"\u041E\u0431\u043D\u043E\u0432\u0438\u0442\u044C "
		+ "\u0441\u043F\u0438\u0441\u043E\u043A \u043F\u0435\u0440\u044C\u0435\u0432";

	private static readonly PenCatalogue _catalogue = new([Argon, Pressure, Power], [Chamber, Spare]);

	private readonly FakePenCatalogueEditor _editor = new();

	private readonly MessagePanelViewModel _messagePanel = new();

	public void Dispose()
	{
		_messagePanel.Dispose();
	}

	[AvaloniaFact]
	public void TheBottomBar_StandsOnBothTabsRightAlignedAndTheVisibilityColumnReadsOnStart()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var refresh = Named<Button>(window, "PenEditorRefreshButton");
			var addedCount = Named<TextBlock>(window, "PenEditorAddedCount");
			var tabs = Named<TabControl>(window, "PenEditorTabs");
			var groupsTab = Named<TabItem>(window, "GroupsTab");
			_editor.RegisterResult = Result.Ok(2);
			_editor.ReadResult = Result.Ok(_catalogue);

			HeadlessInput.Click(window, refresh);

			addedCount.Text.Should().Be(Resources.FormatPenEditorAddedCount(2));
			refresh.Content.Should().Be(Resources.PenEditorRefresh, "the button names what it refreshes");
			RefreshLabelIn(CultureInfo.InvariantCulture).Should().Be("Refresh pen list");
			RefreshLabelIn(new CultureInfo("ru")).Should().Be(RussianRefreshLabel);
			IsOnScreen(refresh).Should().BeTrue("the refresh button stands on the pens tab");
			IsOnScreen(addedCount).Should().BeTrue("the added count stands on the pens tab");
			RightOf(refresh, window).Should().Be(RightOf(tabs, window), "the bar is right-aligned");
			RightOf(addedCount, window).Should().BeLessThan(LeftOf(refresh, window), "the count sits left of Refresh");
			Named<Button>(window, "SortByEnabledOnStart").Content.Should().Be(Resources.PenEditorColumnOnStart);

			HeadlessInput.Click(window, groupsTab);

			tabs.SelectedItem.Should().BeSameAs(groupsTab, "a click on the tab header switches to it");
			IsOnScreen(refresh).Should().BeTrue("the refresh button stands on the groups tab too");
			IsOnScreen(addedCount).Should().BeTrue("the added count stands on the groups tab too");
			IsOnScreen(Named<ListBox>(window, "PenTable")).Should().BeFalse("the pens tab gave way");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARow_ShowsThePenAsStoredUnderItsHeader()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var argon = RowAt(window, 0);
			var power = RowAt(window, 2);

			TextOf(argon, "RowId").Should().Be("3");
			TextOf(argon, "RowName").Should().Be(Argon.Name);
			TextOf(argon, "RowUnit").Should().Be(Argon.Unit);
			TextOf(argon, "RowLineStyle").Should().Be(Resources.PenLineStyleStepped);
			TextOf(argon, "RowGroups").Should().Be(Chamber.Name);
			Cell<CheckBox>(argon, "RowEnabledOnStart").IsChecked.Should().BeFalse();
			Cell<CheckBox>(argon, "RowEnabledOnStart").IsHitTestVisible.Should().BeFalse("the tick is read-only");
			Cell<Border>(argon, "RowColor").Background.Should().BeAssignableTo<ISolidColorBrush>()
				.Which.Color.Should().Be(Color.Parse(Argon.Color!));
			Cell<Border>(argon, "RowColor").Bounds.Width.Should().BePositive("the swatch renders");

			TextOf(power, "RowLineStyle").Should().Be(Resources.PenLineStyleInterpolated);
			TextOf(power, "RowScaleMaxOnStart").Should().Be(500.ToString(CultureInfo.CurrentCulture));
			Cell<CheckBox>(power, "RowEnabledOnStart").IsChecked.Should().BeTrue();
			Cell<Border>(power, "RowColor").Background.Should()
				.Be(Brushes.Transparent, "a pen with no colour draws no swatch");

			var header = Named<Grid>(window, "PenTableHeader");
			RowGridOf(argon).Bounds.Height.Should().Be(PenEditorWindow.RowHeight);
			LeftOf(RowGridOf(argon), window).Should().Be(LeftOf(header, window), "a header sits over its column");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ClickingRows_SelectsEachPenAndBuildsItsFormWritingNothing()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var table = Named<ListBox>(window, "PenTable");
			viewModel.SelectedForm.Should().BeNull("no row is selected when the window opens");

			HeadlessInput.Click(window, RowAt(window, 1));

			viewModel.SelectedRow.Should().BeSameAs(viewModel.Rows[1]);
			table.SelectedItem.Should().BeSameAs(viewModel.Rows[1]);
			viewModel.SelectedForm!.Row.Should().BeSameAs(viewModel.Rows[1]);

			HeadlessInput.Click(window, RowAt(window, 0));
			HeadlessInput.Click(window, RowAt(window, 2));

			viewModel.SelectedRow!.Pen.Should().Be(Power);
			_editor.Calls.Should().BeEmpty("choosing a pen, whatever its line style and start state, writes nothing");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AHeaderClick_ReordersTheRowsAndKeepsTheSelectedPen()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var table = Named<ListBox>(window, "PenTable");
			HeadlessInput.Click(window, RowAt(window, 1));
			var selected = viewModel.SelectedRow;
			var form = viewModel.SelectedForm;

			HeadlessInput.Click(window, Named<Button>(window, "SortByName"));

			RealisedPens(table).Should().Equal(Argon, Power, Pressure);
			viewModel.SelectedRow.Should().BeSameAs(selected);
			table.SelectedItem.Should().BeSameAs(selected, "the list keeps the row it held");
			viewModel.SelectedForm.Should().BeSameAs(form);

			HeadlessInput.Click(window, Named<Button>(window, "SortByName"));

			RealisedPens(table).Should().Equal(Pressure, Power, Argon);
			table.SelectedItem.Should().BeSameAs(selected);
			viewModel.SelectedForm.Should().BeSameAs(form);
			_editor.Calls.Should().BeEmpty();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AFullCatalogue_RealisesOnlyTheRowsOnScreen()
	{
		const int PenCount = 500;
		using var viewModel = _editor.EditorOver(new PenCatalogue(PensNumbered(PenCount), []), _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var realised = Named<ListBox>(window, "PenTable").GetRealizedContainers().Count();

			realised.Should().BePositive().And.BeLessThan(PenCount);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheWindow_OpensOnA1280PxScreenAndAtItsMinimumWidthClipsNoFixedColumn()
	{
		const double SmallestScreenWidth = 1280;
		const double MinimumGroupsColumnWidth = 100;
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			window.Width.Should().BeLessThan(SmallestScreenWidth, "the window opens whole on a 1280 px screen");
			var header = Named<Grid>(window, "PenTableHeader");
			var fixedColumnsWidth = header.ColumnDefinitions.Take(FixedColumnCount).Sum(column => column.Width.Value);

			window.Width = window.MinWidth;
			Dispatcher.UIThread.RunJobs();

			var buttonHeights = header.Children.OfType<Button>().Select(button => button.Bounds.Height).Distinct();
			header.Bounds.Width.Should().BeGreaterThanOrEqualTo(fixedColumnsWidth, "the fixed columns keep their widths");
			(header.Bounds.Width - fixedColumnsWidth).Should()
				.BeGreaterThanOrEqualTo(MinimumGroupsColumnWidth, "the groups column keeps a readable width");
			Named<ListBox>(window, "PenTable").GetVisualDescendants().OfType<ScrollViewer>().First().Extent.Width
				.Should().BeLessThanOrEqualTo(header.Bounds.Width, "the table holds nothing the header lacks");
			buttonHeights.Should().ContainSingle("every header button, wrapped or not, has the same reserved height");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheRefreshButton_RegistersRereadsAndShowsTheAddedCount()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			_editor.RegisterResult = Result.Ok(2);
			_editor.ReadResult = Result.Ok(_catalogue);

			HeadlessInput.Click(window, Named<Button>(window, "PenEditorRefreshButton"));

			_editor.Calls.Should().Equal(new FakeEditorCall.RegisterNewPens(), new FakeEditorCall.Read());
			Named<TextBlock>(window, "PenEditorAddedCount").Text.Should().Be(Resources.FormatPenEditorAddedCount(2));
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheForm_IsEmptyAndDisabledWithNoRowAndShowsTheClickedPen()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var name = Named<TextBox>(window, "FormName");
			name.IsEffectivelyEnabled.Should().BeFalse("no pen is selected");
			name.Text.Should().BeNullOrEmpty();
			Named<ComboBox>(window, "FormLineStyle").IsEffectivelyEnabled.Should().BeFalse();
			Named<ColorPicker>(window, "FormColorPicker").IsEffectivelyEnabled.Should().BeFalse();

			HeadlessInput.Click(window, RowAt(window, 1));

			name.IsEffectivelyEnabled.Should().BeTrue();
			name.Text.Should().Be(Pressure.Name);
			Named<TextBox>(window, "FormUnit").Text.Should().Be(Pressure.Unit);
			Named<TextBox>(window, "FormMask").Text.Should().Be(Pressure.Format);
			Named<TextBlock>(window, "FormMaskPreview").Text.Should()
				.Be(PenValueFormat.Format(PenFormViewModel.MaskPreviewSample, Pressure.Format));
			Named<TextBox>(window, "FormColor").Text.Should().Be(Pressure.Color);
			Named<ColorPicker>(window, "FormColorPicker").Color.Should().Be(Color.Parse(Pressure.Color!));
			Named<ComboBox>(window, "FormLineStyle").SelectedItem.Should().Be(Pressure.LineStyle);
			Named<CheckBox>(window, "FormEnabledOnStart").IsChecked.Should().Be(Pressure.EnabledOnStart);
			Named<TextBox>(window, "FormScaleMinOnStart").Text.Should().Be(0.ToString(CultureInfo.CurrentCulture));
			Named<TextBox>(window, "FormScaleMaxOnStart").Text.Should().Be(100.ToString(CultureInfo.CurrentCulture));
			Named<TextBlock>(window, "FormMessage").Text.Should().BeEmpty();
			_editor.Calls.Should().BeEmpty();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANameTypedAndTabbedOut_WritesOnceAndTheRowShowsIt()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
	public void AClickOnEmptySpaceWithNoEditInProgress_WritesNothing()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var name = Named<TextBox>(window, "FormName");
			HeadlessInput.Click(window, name);
			name.IsFocused.Should().BeTrue();

			HeadlessInput.Click(window, FormLabel(window, Resources.PenEditorColumnName));
			HeadlessInput.Click(window, Named<TextBlock>(window, "FormMessage"));

			name.IsFocused.Should().BeFalse();
			_editor.Calls.Should().BeEmpty("a field left unchanged writes nothing");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANameTypedThenTheWindowClosed_WritesOnceBeforeTheWindowIsGone()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
			TextOf(RowAt(window, 0), "RowLineStyle").Should().Be(Resources.PenLineStyleInterpolated);
			Cell<CheckBox>(RowAt(window, 0), "RowEnabledOnStart").IsChecked.Should().BeTrue();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ASwatchPickedInThePicker_WritesOneColourChange()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));

			var picked = PickSwatch(window, Color.Parse(Pressure.Color!));

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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var picker = Named<ColorPicker>(window, "FormColorPicker");
			HeadlessInput.Click(window, RowAt(window, 1));
			PickSwatch(window, Color.Parse(Pressure.Color!));

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
	public void AColourNamedInWords_WritesNothingAndMarksTheField()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var color = Named<TextBox>(window, "FormColor");

			Retype(window, color, "red");
			HeadlessInput.Press(window, PhysicalKey.Tab);

			_editor.Calls.Should().BeEmpty();
			color.Text.Should().Be(Pressure.Color);
			color.Classes.Should().Contain("invalid");
			Named<TextBlock>(window, "FormMessage").Text.Should().Be(Resources.PenFormColorInvalid);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void APenWithNoColour_OpeningAndDismissingThePicker_WritesNothing()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
	public void ARefreshThatChangesTheCatalogue_RebindsTheTableAndTheGroupsTab()
	{
		var registered = NumberedPen(4242);
		var vacuum = new StoredGroup(9, "Vacuum", [4242]);
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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

			OpenGroupsTab(window);

			viewModel.Groups.Should().NotBeSameAs(groupsBefore);
			Named<ListBox>(window, "GroupList").ItemsSource.Should().BeSameAs(viewModel.Groups.Groups);
			TextOf(GroupAt(window, 2), "GroupName").Should().Be(vacuum.Name);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANameTypedThenRefreshClicked_WritesBeforeTheRegistration()
	{
		_editor.ReadResult = Result.Ok(_catalogue);
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
	public async Task ANameTypedWhileARefreshRuns_ReachesNoFieldAndWritesNothing()
	{
		_editor.ReadResult = Result.Ok(_catalogue);
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
			await HeadlessWait.Until(() => name.IsEffectivelyEnabled);
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
	public void TheLogColumn_ShowsTheFlagReadOnlyAndSortsOnAHeaderClick()
	{
		var logArgon = Argon with { LogScaleOnStart = true };
		using var viewModel = _editor.EditorOver(new PenCatalogue([logArgon, Pressure, Power], []), _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var table = Named<ListBox>(window, "PenTable");
			var header = Named<Button>(window, "SortByLogScaleOnStart");
			header.Content.Should().Be(Resources.PenEditorColumnLogScaleOnStart);
			Cell<CheckBox>(RowAt(window, 0), "RowLogScaleOnStart").IsChecked.Should().BeTrue();
			Cell<CheckBox>(RowAt(window, 0), "RowLogScaleOnStart").IsHitTestVisible.Should().BeFalse(
				"the tick is read-only");
			Cell<CheckBox>(RowAt(window, 1), "RowLogScaleOnStart").IsChecked.Should().BeFalse();

			HeadlessInput.Click(window, header);

			RealisedPens(table).Should().Equal(Pressure, Power, logArgon);

			HeadlessInput.Click(window, header);

			RealisedPens(table).Should().Equal(logArgon, Power, Pressure);
			_editor.Calls.Should().BeEmpty();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheLogBoxClicked_WritesTheFlagAndTheRowShowsIt()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 0));
			var box = Named<CheckBox>(window, "FormLogScaleOnStart");
			box.IsChecked.Should().Be(Argon.LogScaleOnStart);

			HeadlessInput.Click(window, box);

			_editor.Changes.Should().Equal(
				new FakeEditorCall.Change(Argon, new PenSettingChange.LogScaleOnStart(true)));
			Cell<CheckBox>(RowAt(window, 0), "RowLogScaleOnStart").IsChecked.Should().BeTrue();
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
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
	public void AZeroMinimumTypedThenTheLogBoxClicked_RefusesTheFlagWhileThePairIsInFlight()
	{
		var positivePressure = Pressure with { ScaleMinOnStart = 1 };
		using var viewModel = _editor.EditorOver(new PenCatalogue([Argon, positivePressure, Power], []), _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			Retype(window, Named<TextBox>(window, "FormScaleMinOnStart"), "0");
			var pairWrite = _editor.HoldNextCall();

			HeadlessInput.Click(window, Named<CheckBox>(window, "FormLogScaleOnStart"));

			Named<CheckBox>(window, "FormLogScaleOnStart").IsChecked.Should().BeFalse(
				"the rule reads the minimum queued, not the 1 still stored");
			Named<TextBlock>(window, "FormMessage").Text.Should().Be(Resources.ScaleLogMinimumPositive);

			pairWrite.SetResult();
			Dispatcher.UIThread.RunJobs();

			_editor.Changes.Should().Equal(
				new FakeEditorCall.Change(positivePressure, new PenSettingChange.ScaleOnStart(0, 100)));
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ALogBoxTheArchiveRefuses_PutsTheBoxBackSaysWhyAndReportsOnce()
	{
		var refusal = FakePenCatalogueEditor.Refusal(ArchiveFault.RowGone, Argon.Name);
		_editor.ChangeResult = Result.Fail(refusal);
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 0));
			var box = Named<CheckBox>(window, "FormLogScaleOnStart");

			HeadlessInput.Click(window, box);

			box.IsChecked.Should().Be(Argon.LogScaleOnStart, "the refused choice puts the box back");
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
		HeadlessInput.Click(window, lineStyle);
		var item = lineStyle.ContainerFromItem(choice)
			?? throw new InvalidOperationException("The line style list realised no item.");
		HeadlessInput.Click(TopLevelOf(item), item);
	}

	private static TextBlock FormLabel(PenEditorWindow window, string text)
	{
		return Named<Panel>(window, "PenForm").GetVisualDescendants()
			.OfType<TextBlock>()
			.Single(label => label.Classes.Contains("form-label") && label.Text == text);
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

	private static Color PickSwatch(PenEditorWindow window, Color current)
	{
		var flyout = ColorFlyoutOf(window);
		HeadlessInput.Click(window, Named<ColorPicker>(window, "FormColorPicker"));
		var content = flyout.Content.Should().BeAssignableTo<Control>().Subject;
		var topLevel = TopLevelOf(content);

		HeadlessInput.Click(topLevel, content.GetVisualDescendants().OfType<TabItem>().ElementAt(PaletteTabIndex));
		var swatch = content.GetVisualDescendants()
			.OfType<ListBoxItem>()
			.First(item => item is { IsEffectivelyVisible: true, Bounds.Width: > 0, DataContext: Color color }
				&& color != current);
		HeadlessInput.Click(topLevel, swatch);
		HeadlessInput.Press(topLevel, PhysicalKey.Escape);

		flyout.IsOpen.Should().BeFalse("Escape dismisses the picker");

		return (Color)swatch.DataContext!;
	}

	private static IEnumerable<StoredPen> RealisedPens(ListBox table)
	{
		return Enumerable.Range(0, table.ItemCount)
			.Select(index => table.ContainerFromIndex(index)!.DataContext)
			.Cast<PenRowViewModel>()
			.Select(row => row.Pen);
	}

	private static Grid RowGridOf(Control row)
	{
		return row.GetVisualDescendants().OfType<Grid>().First(grid => grid.ColumnDefinitions.Count > FixedColumnCount);
	}

	private static double LeftOf(Control control, PenEditorWindow window)
	{
		return control.TranslatePoint(default, window)?.X
			?? throw new InvalidOperationException("The control is not in the window's visual tree.");
	}

	private static double RightOf(Control control, PenEditorWindow window)
	{
		return LeftOf(control, window) + control.Bounds.Width;
	}

	private static double TopOf(Control control, PenEditorWindow window)
	{
		return control.TranslatePoint(default, window)?.Y
			?? throw new InvalidOperationException("The control is not in the window's visual tree.");
	}

	private static double BottomOf(Control control, PenEditorWindow window)
	{
		return TopOf(control, window) + control.Bounds.Height;
	}

	private static string? RefreshLabelIn(CultureInfo culture)
	{
		return Resources.ResourceManager.GetString(nameof(Resources.PenEditorRefresh), culture);
	}
}
