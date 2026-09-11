using System.Globalization;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using SemiPlot.UI.Localization;
using SemiPlot.UI.Toolbar;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Toolbar;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendToolbarViewTests
{
	[AvaloniaFact]
	public void ToolbarCaptions_ComeFromTheGeneratedResourceAccessor()
	{
		var (_, view) = CreateView();

		var captions = view.GetLogicalDescendants()
			.OfType<ContentControl>()
			.Select(control => control.Content as string)
			.ToList();

		captions.Should()
			.Contain(Resources.ToolbarAutoscale)
			.And.Contain(Resources.ToolbarSetLimits)
			.And.Contain(Resources.ToolbarJumpToNow)
			.And.Contain(Resources.ToolbarSticky)
			.And.Contain(Resources.ToolbarDelta);
	}

	[AvaloniaFact]
	public void LimitPlaceholders_ComeFromTheGeneratedResourceAccessor()
	{
		var (_, view) = CreateView();

		var placeholders = view.GetLogicalDescendants()
			.OfType<TextBox>()
			.Select(box => box.PlaceholderText)
			.ToList();

		placeholders.Should()
			.Contain(Resources.ToolbarMinPlaceholder)
			.And.Contain(Resources.ToolbarMaxPlaceholder);
	}

	[AvaloniaFact]
	public void LayerLabel_AppliesTheResourceFormatToTheActiveLayer()
	{
		var (toolbar, view) = CreateView();

		var expected = string.Format(
			CultureInfo.CurrentCulture, Resources.ToolbarLayerFormat, toolbar.ActiveLayer);

		view.GetLogicalDescendants()
			.OfType<TextBlock>()
			.Select(block => block.Text)
			.Should()
			.Contain(expected);
	}

	private static (TrendToolbarViewModel Toolbar, TrendToolbarView View) CreateView()
	{
		var (_, toolbar) = ToolbarTestBuilder.CreateToolbar();

		return (toolbar, new TrendToolbarView { DataContext = toolbar });
	}
}
