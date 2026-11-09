using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using AwesomeAssertions;

using SemiPlot.UI.Localization;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.AxisScalePanelDriver;
using static SemiPlot.Tests.Unit.UI.Chart.ChartViewTestBuilder;
using static SemiPlot.Tests.Unit.UI.InterMeasure;

namespace SemiPlot.Tests.Unit.UI.Chart;

// The axis scale panel's layout and its invalid-field glyph, realised through Avalonia's real pipeline.
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
[Collection(ProcessGlobalStateCollection.Name)]
public sealed class AxisScalePanelLayoutTests
{
	[AvaloniaFact]
	public void AnInvalidBound_ShowsTheWarningGlyphInsideTheBoxWithoutResizing()
	{
		using var culture = new CultureScope("en-US");
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (_, panel) = OpenPanel(viewModel, shown);
		var maximum = panel.FindControl<TextBox>("AxisScaleMaximum")!;
		var minimum = panel.FindControl<TextBox>("AxisScaleMinimum")!;
		var message = panel.FindControl<TextBlock>("AxisScaleValidationMessage")!;
		var (sizes, maximumBounds, minimumBounds) = (SizesOf(panel, message), maximum.Bounds, minimum.Bounds);
		panel.TryFindResource("WarningGeometry", out var warning).Should().BeTrue();

		TypeInto(shown.Window, panel, "AxisScaleMaximum", "1x");
		HeadlessInput.Clear(shown.Window, minimum);

		var glyphs = new[] { maximum, minimum }.Select(box =>
		{
			box.Classes.Should().Contain("invalid");
			var glyph = box.InnerRightContent.Should().BeOfType<PathIcon>().Subject;
			glyph.IsEffectivelyVisible.Should().BeTrue();
			glyph.Data.Should().BeSameAs(warning);
			ViewProbe.LeftOf(glyph, box).Should().BeGreaterThanOrEqualTo(0);
			ViewProbe.RightOf(glyph, box).Should().BeLessThanOrEqualTo(box.Bounds.Width, "the glyph sits inside the box");
			ViewProbe.TopOf(glyph, box).Should().BeGreaterThanOrEqualTo(0);
			ViewProbe.BottomOf(glyph, box).Should().BeLessThanOrEqualTo(box.Bounds.Height);

			return glyph;
		}).ToList();

		glyphs[0].Should().NotBeSameAs(glyphs[1], "each box carries a glyph of its own");
		(SizesOf(panel, message), maximum.Bounds, minimum.Bounds).Should().Be((sizes, maximumBounds, minimumBounds));
	}

	[AvaloniaFact]
	public void AValidBound_ShowsNoWarningGlyph()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (_, panel) = OpenPanel(viewModel, shown);

		foreach (var name in new[] { "AxisScaleMaximum", "AxisScaleMinimum" })
		{
			var box = panel.FindControl<TextBox>(name)!;
			box.Classes.Should().NotContain("invalid", name);
			box.InnerRightContent.Should().BeNull(name);
		}
	}

	[AvaloniaFact]
	public void ABoundCorrected_DropsTheWarningGlyph()
	{
		using var culture = new CultureScope("en-US");
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (_, panel) = OpenPanel(viewModel, shown);
		var maximum = panel.FindControl<TextBox>("AxisScaleMaximum")!;

		TypeInto(shown.Window, panel, "AxisScaleMaximum", "1x");
		maximum.InnerRightContent.Should().BeOfType<PathIcon>();

		TypeInto(shown.Window, panel, "AxisScaleMaximum", "50");

		maximum.Classes.Should().NotContain("invalid");
		maximum.InnerRightContent.Should().BeNull();
	}

	[AvaloniaTheory]
	[InlineData("en-US")]
	[InlineData("ru-RU")]
	public void TheLogarithmicBox_StartsAtTheFieldEdge_UnderTheScaleTypeLabel(string cultureName)
	{
		const double Tolerance = 0.5;
		using var culture = new CultureScope(cultureName);
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (_, panel) = OpenPanel(viewModel, shown);
		var box = panel.FindControl<CheckBox>("AxisScaleLogarithmic")!;
		var bounds = panel.FindControl<Grid>("AxisScaleBounds")!;
		var row = panel.FindControl<Grid>("AxisScaleType")!;
		var fieldEdge = ViewProbe.LeftOf(panel.FindControl<TextBox>("AxisScaleMaximum")!, panel);

		ViewProbe.LeftOf(box, panel).Should().BeApproximately(fieldEdge, Tolerance, "the box starts at the field edge");
		ViewProbe.LeftOf(panel.FindControl<TextBox>("AxisScaleMinimum")!, panel).Should()
			.BeApproximately(fieldEdge, Tolerance);
		row.GetVisualAncestors().Should().NotContain(bounds, "focus onto the box must leave the pair's grid");
		box.Parent.Should().BeSameAs(row);
		row.ColumnDefinitions[0].ActualWidth.Should()
			.BeApproximately(bounds.ColumnDefinitions[0].ActualWidth, Tolerance, "the two grids share one label column");
		var label = panel.FindControl<TextBlock>("AxisScaleTypeLabel")!;
		label.Text.Should().Be(Resources.PenFormScaleTypeLabel);
		label.Parent.Should().BeSameAs(row);
		Grid.GetColumn(label).Should().Be(0);
		box.Content.Should().Be(Resources.PenFormLogarithmic);
	}

	[AvaloniaFact]
	public void TheForm_TakesItsGapsFromTheSpacingScale()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (_, panel) = OpenPanel(viewModel, shown);
		var bounds = panel.FindControl<Grid>("AxisScaleBounds")!;
		var row = panel.FindControl<Grid>("AxisScaleType")!;
		var header = (Control)panel.FindControl<TextBlock>("AxisScalePenName")!.Parent!;
		var maximum = panel.FindControl<TextBox>("AxisScaleMaximum")!;
		var maximumLabel = bounds.Children.OfType<TextBlock>().First();

		(ViewProbe.LeftOf(maximum, bounds) - ViewProbe.RightOf(maximumLabel, bounds))
			.Should().Be(12, "label to field");
		(ViewProbe.LeftOf(panel.FindControl<CheckBox>("AxisScaleLogarithmic")!, row)
			- ViewProbe.RightOf(panel.FindControl<TextBlock>("AxisScaleTypeLabel")!, row))
			.Should().Be(12, "label to field");
		(ViewProbe.TopOf(panel.FindControl<TextBox>("AxisScaleMinimum")!, panel) - ViewProbe.BottomOf(maximum, panel))
			.Should().Be(8, "the row spacing");
		(ViewProbe.TopOf(row, panel) - ViewProbe.BottomOf(bounds, panel)).Should().Be(8, "the row spacing");
		(ViewProbe.TopOf(bounds, panel) - ViewProbe.BottomOf(header, panel)).Should().Be(8, "the row spacing");
	}

	[AvaloniaFact]
	public void AClickOnTheScaleTypeLabelOrBesideTheBox_LeavesTheFlagAndGivesThePanelTheKeyboard()
	{
		using var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 1.0, 90.0);
		using var shown = ShowChart(viewModel);
		var (flyout, panel) = OpenPanel(viewModel, shown);
		var box = panel.FindControl<CheckBox>("AxisScaleLogarithmic")!;
		var row = panel.FindControl<Grid>("AxisScaleType")!;
		var besideTheBox = new Point(
			(ViewProbe.RightOf(box, row) + row.Bounds.Width) / 2.0,
			row.Bounds.Height / 2.0);

		box.Bounds.Width.Should().BeLessThan(row.ColumnDefinitions[1].ActualWidth, "the box does not span its column");

		HeadlessInput.Click(shown.Window, panel.FindControl<TextBlock>("AxisScaleTypeLabel")!);

		viewModel.ScaleSettings[1].IsLogarithmic.Should().BeFalse("a click on the label does not toggle");
		box.IsChecked.Should().BeFalse();
		box.IsFocused.Should().BeFalse();
		panel.IsFocused.Should().BeTrue("the panel takes the keyboard");

		HeadlessInput.Click(shown.Window, row, besideTheBox);

		viewModel.ScaleSettings[1].IsLogarithmic.Should().BeFalse("a click beside the box does not toggle");
		box.IsChecked.Should().BeFalse();
		box.IsFocused.Should().BeFalse();
		panel.IsFocused.Should().BeTrue("the panel takes the keyboard");
		flyout.IsOpen.Should().BeTrue();
	}

	[AvaloniaTheory]
	[InlineData("en-US")]
	[InlineData("ru-RU")]
	public void TheScaleTypeRow_FitsThePanelInInter(string cultureName)
	{
		const double FieldSlack = 24.0;
		using var culture = new CultureScope(cultureName);
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (_, panel) = OpenPanel(viewModel, shown);
		var bounds = panel.FindControl<Grid>("AxisScaleBounds")!;
		var row = panel.FindControl<Grid>("AxisScaleType")!;
		var field = panel.FindControl<TextBox>("AxisScaleMaximum")!;
		var box = panel.FindControl<CheckBox>("AxisScaleLogarithmic")!;
		var labels = bounds.Children.OfType<TextBlock>()
			.Append(panel.FindControl<TextBlock>("AxisScaleTypeLabel")!)
			.ToList();
		labels.Select(label => label.Text).Should().Equal(
			Resources.AxisScaleMaximumLabel, Resources.AxisScaleMinimumLabel, Resources.PenFormScaleTypeLabel);
		using var typeface = InterTypeface();
		var labelWidths = labels.Select(label =>
		{
			using var font = InterFont(typeface, label.FontSize);
			return (double)font.MeasureText(label.Text);
		}).ToList();
		var labelColumn = labelWidths.Max();
		var boxWidth = ContentWidthInInter(shown.Window, box, typeface);
		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{cultureName}: labels {string.Join(", ", labelWidths.Select(width => $"{width:0.0}"))} px, "
			+ $"label column {labelColumn:0.0} px, box {boxWidth:0.0} px");

		(labelColumn + bounds.ColumnSpacing + field.Width).Should().BeLessThanOrEqualTo(panel.Width - FieldSlack);
		(labelColumn + row.ColumnSpacing + boxWidth).Should().BeLessThanOrEqualTo(panel.Width);
	}
}
