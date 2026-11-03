using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;

using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.PenEditor;

using Xunit;

using static SemiPlot.Tests.Unit.UI.InterMeasure;
using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorFixture;
using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorWindowDriver;
using static SemiPlot.Tests.Unit.UI.ViewProbe;

namespace SemiPlot.Tests.Unit.UI.PenEditor;

/// <summary>The pen pane's layout: the empty hint, the form's sections, the field widths and the pane width.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class PenFormPaneViewTests : IDisposable
{
	private const double PaneWidth = 420;

	private const double LabelColumnWidth = 112;

	private const double FormColumnSpacing = 12;

	private readonly FakePenCatalogueEditor _editor = new();

	private readonly MessagePanelViewModel _messagePanel = new();

	public void Dispose()
	{
		_messagePanel.Dispose();
	}

	[AvaloniaFact]
	public void TheWindow_OpensOnA1280PxScreenAndAtItsMinimumWidthKeepsThePaneAndEveryColumnMinimum()
	{
		const double SmallestScreenWidth = 1280;
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			window.Width.Should().BeLessThan(SmallestScreenWidth, "the window opens whole on a 1280 px screen");

			window.Width = window.MinWidth;
			Dispatcher.UIThread.RunJobs();

			var list = PenListOf(window);
			Named<Border>(window, "PenFormPanel").Bounds.Width.Should().Be(PaneWidth);
			list.Columns.Should().AllSatisfy(column => column.ActualWidth.Should().BeGreaterThanOrEqualTo(column.MinWidth));
			list.Columns.Sum(column => column.ActualWidth).Should()
				.BeLessThanOrEqualTo(ViewportOf(list) + 0.5, "every column fits the list without scrolling");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void WithNoPenSelected_ThePaneShowsOnlyTheHint()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var hint = Named<TextBlock>(window, "PenFormEmptyHint");

			IsOnScreen(hint).Should().BeTrue("the pane says what to do with no pen selected");
			hint.Text.Should().Be(Resources.PenFormEmptyHint);
			Named<ScrollViewer>(window, "PenFormScroller").IsEffectivelyVisible.Should().BeFalse("no form shows");
			_editor.Calls.Should().BeEmpty();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AClickedPen_FillsTheFormUnderAOneLineTitle()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var name = Named<TextBox>(window, "FormName");

			HeadlessInput.Click(window, RowAt(window, 1));

			Named<TextBlock>(window, "PenFormEmptyHint").IsEffectivelyVisible.Should().BeFalse("the hint gave way");
			var titleId = Named<TextBlock>(window, "PenFormTitleId");
			var titleName = Named<TextBlock>(window, "PenFormTitleName");
			titleId.Text.Should().Be("7");
			titleName.Text.Should().Be(Pressure.Name);
			TopOf(titleId, window).Should().Be(TopOf(titleName, window), "the title is one line");
			RightOf(titleId, window).Should().BeLessThan(LeftOf(titleName, window), "the id stands first");
			name.IsEffectivelyEnabled.Should().BeTrue();
			name.Text.Should().Be(Pressure.Name);
			Named<TextBox>(window, "FormUnit").Text.Should().Be(Pressure.Unit);
			Named<TextBox>(window, "FormMask").Text.Should().Be(Pressure.Format);
			Named<TextBlock>(window, "FormMaskPreview").Text.Should()
				.Be(PenValueFormat.Format(PenFormViewModel.MaskPreviewSample, Pressure.Format));
			Named<TextBox>(window, "FormColor").Text.Should().Be(Pressure.Color);
			Named<TextBox>(window, "FormColor").PlaceholderText.Should().Be(PenColorConverters.FieldFormat);
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
	public void TheForm_HasThreeSectionsOnOneLabelColumnAndOneFieldEdge()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var form = Named<Panel>(window, "PenForm");

			var sections = Descendants<TextBlock>(form)
				.Where(block => block.Classes.Contains("form-section"))
				.OrderBy(block => TopOf(block, window))
				.Select(block => block.Text);
			sections.Should().Equal(
				Resources.PenFormSectionLabel, Resources.PenFormSectionAppearance, Resources.PenFormSectionOnStart);
			var labelEdges = FormLabels(window).Select(label => LeftOf(label, window)).Distinct().ToList();
			labelEdges.Should().ContainSingle("every label starts at one edge");
			FormFields(window).Should().AllSatisfy(field => (LeftOf(field, window) - labelEdges[0]).Should()
				.BeApproximately(LabelColumnWidth + FormColumnSpacing, 0.5, $"{field.Name} starts at the field edge"));
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void EveryField_HasTheWidthOfItsContentType()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));

			Named<TextBox>(window, "FormName").Bounds.Width.Should().Be(240);
			Named<ComboBox>(window, "FormLineStyle").Bounds.Width.Should().Be(240);
			Named<TextBox>(window, "FormUnit").Bounds.Width.Should().Be(120);
			Named<TextBox>(window, "FormMask").Bounds.Width.Should().Be(120);
			Named<TextBox>(window, "FormColor").Bounds.Width.Should().Be(120);
			Named<TextBox>(window, "FormScaleMinOnStart").Bounds.Width.Should().Be(120);
			Named<TextBox>(window, "FormScaleMaxOnStart").Bounds.Width.Should().Be(120);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheColourSwatchPicker_IsSquare()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var picker = Named<ColorPicker>(window, "FormColorPicker");
			var color = Named<TextBox>(window, "FormColor");

			picker.Bounds.Size.Should().Be(new Size(32, 32));
			(LeftOf(picker, window) - RightOf(color, window)).Should().BeApproximately(8, 0.5);
			Descendant<Control>(picker, "DropDownGlyph").IsEffectivelyVisible.Should()
				.BeFalse("the picker is the swatch alone");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ThePane_Stays420PxWideWhenTheWindowWidens()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var pane = Named<Border>(window, "PenFormPanel");
			var list = PenListOf(window);
			var listWidth = list.Bounds.Width;
			pane.Bounds.Width.Should().Be(PaneWidth);

			window.Width += 300;
			Dispatcher.UIThread.RunJobs();

			pane.Bounds.Width.Should().Be(PaneWidth);
			list.Bounds.Width.Should().BeApproximately(listWidth + 300, 0.5, "the list takes the width");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AnInvalidField_ShowsTheWarningGlyphInsideTheBoxWithoutResizing()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var mask = Named<TextBox>(window, "FormMask");
			var name = Named<TextBox>(window, "FormName");
			var pane = Named<Border>(window, "PenFormPanel");
			var (maskBounds, nameBounds, paneBounds, windowBounds) = (mask.Bounds, name.Bounds, pane.Bounds, window.Bounds);
			window.TryFindResource("WarningGeometry", out var warning).Should().BeTrue();

			Retype(window, mask, "%0.0");
			HeadlessInput.Clear(window, name);

			var glyphs = new[] { mask, name }.Select(box =>
			{
				box.Classes.Should().Contain("invalid");
				var glyph = box.InnerRightContent.Should().BeOfType<PathIcon>().Subject;
				glyph.IsEffectivelyVisible.Should().BeTrue();
				glyph.Data.Should().BeSameAs(warning);
				LeftOf(glyph, box).Should().BeGreaterThanOrEqualTo(0);
				RightOf(glyph, box).Should().BeLessThanOrEqualTo(box.Bounds.Width, "the glyph sits inside the box");
				TopOf(glyph, box).Should().BeGreaterThanOrEqualTo(0);
				BottomOf(glyph, box).Should().BeLessThanOrEqualTo(box.Bounds.Height);

				return glyph;
			}).ToList();

			glyphs[0].Should().NotBeSameAs(glyphs[1], "each box carries a glyph of its own");
			(mask.Bounds, name.Bounds, pane.Bounds, window.Bounds).Should()
				.Be((maskBounds, nameBounds, paneBounds, windowBounds));
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AValidField_ShowsNoWarningGlyph()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var name = Named<TextBox>(window, "FormName");

			name.Classes.Should().NotContain("invalid");
			name.InnerRightContent.Should().BeNull();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AtTheOpeningSize_ThePaneShowsTheWholeForm()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var scroller = Named<ScrollViewer>(window, "PenFormScroller");

			window.Bounds.Size.Should().Be(new Size(window.Width, window.Height));
			scroller.Extent.Height.Should().BePositive();
			scroller.Extent.Height.Should().BeLessThanOrEqualTo(scroller.Viewport.Height, "the form needs no scrolling");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void EveryPenFormLabel_FitsTheLabelColumnInInter(string culture)
	{
		const int LabelCount = 10;
		using var cultureScope = new CultureScope(culture);
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, RowAt(window, 1));
			var labels = FormLabels(window).ToList();
			using var typeface = InterTypeface();

			labels.Should().HaveCount(LabelCount);
			labels.Should().AllSatisfy(label =>
			{
				using var font = InterFont(typeface, label.FontSize);
				font.MeasureText(label.Text).Should().BeLessThanOrEqualTo((float)LabelColumnWidth, label.Text);
			});
		}
		finally
		{
			Close(window);
		}
	}

	private static IEnumerable<Control> FormFields(PenEditorWindow window)
	{
		return
		[
			Named<TextBox>(window, "FormName"),
			Named<TextBox>(window, "FormUnit"),
			Named<TextBox>(window, "FormMask"),
			Named<TextBlock>(window, "FormMaskPreview"),
			Named<TextBox>(window, "FormColor"),
			Named<ComboBox>(window, "FormLineStyle"),
			Named<CheckBox>(window, "FormEnabledOnStart"),
			Named<TextBox>(window, "FormScaleMinOnStart"),
			Named<TextBox>(window, "FormScaleMaxOnStart"),
			Named<CheckBox>(window, "FormLogScaleOnStart"),
		];
	}
}
