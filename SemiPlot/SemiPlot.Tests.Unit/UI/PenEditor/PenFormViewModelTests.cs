using System.Globalization;

using Avalonia.Headless.XUnit;
using Avalonia.Media;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.PenEditor;

using Xunit;

using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorFixture;

namespace SemiPlot.Tests.Unit.UI.PenEditor;

// The scale drafts read under CultureInfo.CurrentCulture, so a test that types a number sets the culture
// itself and restores it in finally.
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class PenFormViewModelTests : IDisposable
{
	private readonly FakePenCatalogueEditor _editor = new();

	private readonly EditorCallQueue _queue = new(() => { });

	private readonly MessagePanelViewModel _messagePanel = new();

	public void Dispose()
	{
		_messagePanel.Dispose();
	}

	[AvaloniaFact]
	public async Task AValidEdit_WritesTheOneChangeItNamesAndUpdatesTheRow()
	{
		var form = FormFor(Pressure);

		form.Name = "Chamber pressure, main";
		await form.EndEditAsync(PenField.Name);

		_editor.Calls.Should().Equal(
			new FakeEditorCall.Change(Pressure, new PenSettingChange.Name("Chamber pressure, main")));
		form.Row.Pen.Should().Be(Pressure with { Name = "Chamber pressure, main" });
		form.IsNameValid.Should().BeTrue();
		form.Message.Should().BeEmpty();
	}

	[AvaloniaTheory]
	[InlineData(PenField.Name)]
	[InlineData(PenField.Unit)]
	[InlineData(PenField.Mask)]
	[InlineData(PenField.Color)]
	[InlineData(PenField.LineStyle)]
	[InlineData(PenField.EnabledOnStart)]
	[InlineData(PenField.Scale)]
	public async Task AnUnchangedField_WritesNothing(PenField field)
	{
		var form = FormFor(Pressure);

		await form.EndEditAsync(field);

		_editor.Calls.Should().BeEmpty();
		form.Message.Should().BeEmpty();
	}

	[AvaloniaTheory]
	[InlineData(PenField.Name, "", nameof(Resources.PenFormNameRequired))]
	[InlineData(PenField.Name, "   ", nameof(Resources.PenFormNameRequired))]
	[InlineData(PenField.Mask, "%0.0", nameof(Resources.PenFormMaskInvalid))]
	[InlineData(PenField.Mask, "qqq", nameof(Resources.PenFormMaskInvalid))]
	[InlineData(PenField.Color, "", nameof(Resources.PenFormColorInvalid))]
	[InlineData(PenField.Color, "red", nameof(Resources.PenFormColorInvalid))]
	[InlineData(PenField.Color, "#1F77B", nameof(Resources.PenFormColorInvalid))]
	[InlineData(PenField.Color, "# 1F77B", nameof(Resources.PenFormColorInvalid))]
	public async Task ARefusedDraft_NeverReachesTheEditorAndRevertsMarksAndSaysWhy(
		PenField field, string draft, string ruleKey)
	{
		var form = FormFor(Pressure);

		SetText(form, field, draft);
		await form.EndEditAsync(field);

		_editor.Calls.Should().BeEmpty();
		TextOf(form, field).Should().Be(TextOf(FormFor(Pressure), field));
		IsValid(form, field).Should().BeFalse();
		form.Message.Should().Be(Text(ruleKey));
	}

	[AvaloniaTheory]
	[InlineData("5", "", nameof(Resources.PenFormScaleHalfSet))]
	[InlineData("", "5", nameof(Resources.PenFormScaleHalfSet))]
	[InlineData("5", "1", nameof(Resources.PenFormScaleInverted))]
	[InlineData("5", "5", nameof(Resources.PenFormScaleInverted))]
	[InlineData("five", "10", nameof(Resources.PenFormScaleBoundInvalid))]
	public async Task ARefusedScalePair_NeverReachesTheEditorAndRevertsBothBounds(
		string minimum, string maximum, string ruleKey)
	{
		var form = FormFor(Pressure);

		form.ScaleMin = minimum;
		form.ScaleMax = maximum;
		await form.EndEditAsync(PenField.Scale);

		_editor.Calls.Should().BeEmpty();
		form.ScaleMin.Should().Be("0");
		form.ScaleMax.Should().Be("100");
		form.IsScaleMinValid.Should().BeFalse();
		form.IsScaleMaxValid.Should().BeFalse();
		form.Message.Should().Be(Text(ruleKey));
	}

	[AvaloniaFact]
	public async Task AValidScalePair_WritesBothBoundsInOneChange()
	{
		var form = FormFor(Pressure);

		form.ScaleMin = "-20";
		form.ScaleMax = "80";
		await form.EndEditAsync(PenField.Scale);

		_editor.Changes.Select(call => call.Setting).Should().Equal(new PenSettingChange.Scale(-20, 80));
		form.Row.Pen.Should().Be(Pressure with { ScaleMin = -20, ScaleMax = 80 });
	}

	[AvaloniaFact]
	public async Task EmptyingBothScaleBounds_WritesAPairOfNulls()
	{
		var form = FormFor(Pressure);

		form.ScaleMin = string.Empty;
		form.ScaleMax = string.Empty;
		await form.EndEditAsync(PenField.Scale);

		_editor.Changes.Select(call => call.Setting).Should().Equal(new PenSettingChange.Scale(null, null));
	}

	[AvaloniaFact]
	public async Task AnEmptyMask_WritesFormatNull()
	{
		var form = FormFor(Pressure);

		form.Mask = string.Empty;
		await form.EndEditAsync(PenField.Mask);

		_editor.Changes.Select(call => call.Setting).Should().Equal(new PenSettingChange.Format(null));
		form.Row.Pen.Format.Should().BeNull();
	}

	[AvaloniaFact]
	public async Task AStoredNullColour_OpensAsAnEmptyInvalidFieldAndWritesNothing()
	{
		var form = FormFor(Pressure with { Color = null });

		form.Color.Should().BeEmpty();
		form.IsColorValid.Should().BeFalse();
		form.Message.Should().Be(Resources.PenFormColorInvalid);

		await form.EndEditAsync(PenField.Color);

		_editor.Calls.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task UnderTheRussianCulture_ACommaBoundCommitsItsValue()
	{
		var previous = CultureInfo.CurrentCulture;

		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
			var form = FormFor(Pressure with { ScaleMin = null, ScaleMax = null });

			form.ScaleMin = "1,5";
			form.ScaleMax = "2,25";
			await form.EndEditAsync(PenField.Scale);

			_editor.Changes.Select(call => call.Setting).Should().Equal(new PenSettingChange.Scale(1.5, 2.25));
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}

	[AvaloniaTheory]
	[InlineData("NaNSymbol")]
	[InlineData("PositiveInfinitySymbol")]
	[InlineData("1e999")]
	public async Task UnderTheRussianCulture_ANonFiniteBoundIsRefused(string bound)
	{
		var previous = CultureInfo.CurrentCulture;

		try
		{
			var russian = CultureInfo.GetCultureInfo("ru-RU");
			CultureInfo.CurrentCulture = russian;
			var form = FormFor(Pressure);

			form.ScaleMax = bound switch
			{
				"NaNSymbol" => russian.NumberFormat.NaNSymbol,
				"PositiveInfinitySymbol" => russian.NumberFormat.PositiveInfinitySymbol,
				_ => bound
			};
			await form.EndEditAsync(PenField.Scale);

			_editor.Calls.Should().BeEmpty();
			form.IsScaleMaxValid.Should().BeFalse();
			form.Message.Should().Be(Resources.PenFormScaleBoundInvalid);
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}

	[AvaloniaFact]
	public async Task AFailedWrite_RevertsMarksSaysWhyAndReportsOnce()
	{
		var refusal = new ArchiveError(ArchiveFault.NameTaken, "bench", 5432, "semiplot_dev", "Chamber pressure");
		_editor.ChangeResult = Result.Fail(refusal);
		var form = FormFor(Pressure);

		form.Name = "Taken";
		await form.EndEditAsync(PenField.Name);

		_editor.Calls.Should().ContainSingle();
		form.Name.Should().Be(Pressure.Name);
		form.Row.Pen.Should().Be(Pressure);
		form.IsNameValid.Should().BeFalse();
		form.Message.Should().Be(ArchiveFailureMapper.Map(refusal).Title);
		_messagePanel.Entries.Should().ContainSingle();
	}

	[AvaloniaFact]
	public async Task TheMark_ClearsWhenTheOperatorNextChangesThatField()
	{
		var form = FormFor(Pressure);

		form.Color = "red";
		await form.EndEditAsync(PenField.Color);
		form.Color = "#00FF0";

		form.IsColorValid.Should().BeFalse("the new draft breaks the rule itself");

		form.Color = "#00FF00";

		form.IsColorValid.Should().BeTrue();
		form.Message.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task TwoCommitsWhileTheFirstIsHeld_BothReachTheEditorInTheOrderIssued()
	{
		var form = FormFor(Pressure);
		var gate = _editor.HoldNextCall();

		form.Name = "First";
		var nameWritten = form.EndEditAsync(PenField.Name);
		form.Unit = "kPa";
		var unitWritten = form.EndEditAsync(PenField.Unit);

		_editor.Calls.Should().ContainSingle("the second commit waits for the first");

		gate.SetResult();
		await _queue.WhenIdleAsync();
		await Task.WhenAll(nameWritten, unitWritten);

		var renamed = Pressure with { Name = "First" };
		_editor.Calls.Should().Equal(
			new FakeEditorCall.Change(Pressure, new PenSettingChange.Name("First")),
			new FakeEditorCall.Change(renamed, new PenSettingChange.Unit("kPa")));
		form.Row.Pen.Should().Be(renamed with { Unit = "kPa" });
	}

	[AvaloniaFact]
	public async Task AStartBoxUndoneWhileTheFirstWriteIsHeld_WritesBothInOrderAndEndsAsLastChosen()
	{
		var form = FormFor(Pressure);
		var gate = _editor.HoldNextCall();

		var unticked = form.ChooseEnabledOnStartAsync(false);
		var ticked = form.ChooseEnabledOnStartAsync(true);

		gate.SetResult();
		await _queue.WhenIdleAsync();
		await Task.WhenAll(unticked, ticked);

		_editor.Changes.Select(call => call.Setting).Should().Equal(
			new PenSettingChange.EnabledOnStart(false),
			new PenSettingChange.EnabledOnStart(true));
		form.Row.Pen.EnabledOnStart.Should().BeTrue();
		form.EnabledOnStart.Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task ANameTypedBackWhileTheFirstWriteIsHeld_WritesBothInOrderAndEndsOnTheOriginal()
	{
		var form = FormFor(Pressure);
		var gate = _editor.HoldNextCall();

		form.Name = "Load lock pressure";
		var renamed = form.EndEditAsync(PenField.Name);
		form.Name = Pressure.Name;
		var restored = form.EndEditAsync(PenField.Name);

		gate.SetResult();
		await _queue.WhenIdleAsync();
		await Task.WhenAll(renamed, restored);

		_editor.Changes.Select(call => call.Setting).Should().Equal(
			new PenSettingChange.Name("Load lock pressure"),
			new PenSettingChange.Name(Pressure.Name));
		form.Row.Pen.Should().Be(Pressure);
		form.Name.Should().Be(Pressure.Name);
	}

	[AvaloniaFact]
	public async Task AFailedWriteWithALaterOneQueued_EndsOnTheLaterValueAndWritesNothingMore()
	{
		_editor.ChangeResult = Result.Fail(FakePenCatalogueEditor.Refusal(ArchiveFault.Unreachable, string.Empty));
		var form = FormFor(Pressure);
		var gate = _editor.HoldNextCall();

		form.Name = "First";
		var failed = form.EndEditAsync(PenField.Name);
		form.Name = "Second";
		var written = form.EndEditAsync(PenField.Name);

		gate.SetResult();
		_editor.ChangeResult = Result.Ok();
		await _queue.WhenIdleAsync();
		await Task.WhenAll(failed, written);

		form.Row.Pen.Name.Should().Be("Second");
		form.Name.Should().Be("Second", "a failed write never reverts past a later one still queued");
		form.IsNameValid.Should().BeTrue("the later write succeeded");
		_messagePanel.Entries.Should().ContainSingle();

		await form.EndEditAsync(PenField.Name);

		_editor.Changes.Select(call => call.Setting).Should().Equal(
			new PenSettingChange.Name("First"),
			new PenSettingChange.Name("Second"));
	}

	[AvaloniaFact]
	public async Task AFailedWriteAfterASuccessfulOne_RevertsToTheValueLastWritten()
	{
		var form = FormFor(Pressure);
		var gate = _editor.HoldNextCall();

		form.Unit = "kPa";
		var written = form.EndEditAsync(PenField.Unit);
		form.Unit = "mbar";
		var failed = form.EndEditAsync(PenField.Unit);

		_editor.ChangeResult = Result.Ok();
		gate.SetResult();
		_editor.ChangeResult = Result.Fail(FakePenCatalogueEditor.Refusal(ArchiveFault.Unreachable, string.Empty));
		await _queue.WhenIdleAsync();
		await Task.WhenAll(written, failed);

		form.Row.Pen.Unit.Should().Be("kPa");
		form.Unit.Should().Be("kPa");
		form.IsUnitValid.Should().BeFalse();
	}

	[AvaloniaTheory]
	[InlineData(PenField.Color)]
	[InlineData(PenField.LineStyle)]
	[InlineData(PenField.EnabledOnStart)]
	public async Task AFailedChoice_RevertsTheDraftSaysWhyAndReportsOnce(PenField field)
	{
		var refusal = FakePenCatalogueEditor.Refusal(ArchiveFault.RowGone, Pressure.Name);
		_editor.ChangeResult = Result.Fail(refusal);
		var form = FormFor(Pressure);

		await ChooseAsync(form, field);

		_editor.Changes.Should().ContainSingle();
		form.Color.Should().Be(Pressure.Color);
		form.LineStyle.Should().Be(Pressure.LineStyle);
		form.EnabledOnStart.Should().Be(Pressure.EnabledOnStart);
		form.Row.Pen.Should().Be(Pressure);
		form.Message.Should().Be(ArchiveFailureMapper.Map(refusal).Title);
		_messagePanel.Entries.Should().ContainSingle();
	}

	[AvaloniaTheory]
	[InlineData("0.00")]
	[InlineData("#,##0.0;(#,##0.0)")]
	[InlineData("%0.0")]
	[InlineData("")]
	public void TheMaskPreview_IsTheSampleUnderTheDraft(string mask)
	{
		var form = FormFor(Pressure);

		form.Mask = mask;

		form.MaskPreview.Should().Be(PenValueFormat.Format(PenFormViewModel.MaskPreviewSample, mask));
	}

	[AvaloniaFact]
	public async Task APickedColour_WritesItAsRgbHex()
	{
		var form = FormFor(Pressure);

		await form.PickColorAsync(Color.FromRgb(0xD6, 0x27, 0x28));

		_editor.Changes.Select(call => call.Setting).Should().Equal(new PenSettingChange.Color("#D62728"));
	}

	[AvaloniaFact]
	public async Task AChoiceEqualToTheStoredOne_WritesNothing()
	{
		var form = FormFor(Pressure);

		await form.ChooseLineStyleAsync(PenLineStyle.Interpolated);
		await form.ChooseEnabledOnStartAsync(true);
		await form.ChooseLineStyleAsync(PenLineStyle.Stepped);
		await form.ChooseEnabledOnStartAsync(false);

		_editor.Changes.Select(call => call.Setting).Should().Equal(
			new PenSettingChange.LineStyle(PenLineStyle.Stepped),
			new PenSettingChange.EnabledOnStart(false));
	}

	private PenFormViewModel FormFor(StoredPen pen)
	{
		return new PenFormViewModel(
			new PenRowViewModel(pen, []), _editor, _queue, _messagePanel, NullLogger.Instance);
	}

	private static Task ChooseAsync(PenFormViewModel form, PenField field)
	{
		return field switch
		{
			PenField.Color => form.PickColorAsync(Color.FromRgb(0xD6, 0x27, 0x28)),
			PenField.LineStyle => form.ChooseLineStyleAsync(PenLineStyle.Stepped),
			PenField.EnabledOnStart => form.ChooseEnabledOnStartAsync(false),
			_ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
		};
	}

	private static void SetText(PenFormViewModel form, PenField field, string text)
	{
		switch (field)
		{
			case PenField.Name:
				form.Name = text;
				break;
			case PenField.Mask:
				form.Mask = text;
				break;
			case PenField.Color:
				form.Color = text;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(field), field, null);
		}
	}

	private static string TextOf(PenFormViewModel form, PenField field)
	{
		return field switch
		{
			PenField.Name => form.Name,
			PenField.Mask => form.Mask,
			PenField.Color => form.Color,
			_ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
		};
	}

	private static bool IsValid(PenFormViewModel form, PenField field)
	{
		return field switch
		{
			PenField.Name => form.IsNameValid,
			PenField.Mask => form.IsMaskValid,
			PenField.Color => form.IsColorValid,
			_ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
		};
	}

	private static string Text(string key)
	{
		var value = Resources.ResourceManager.GetString(key, Resources.Culture);
		value.Should().NotBeNullOrWhiteSpace();

		return value;
	}
}
