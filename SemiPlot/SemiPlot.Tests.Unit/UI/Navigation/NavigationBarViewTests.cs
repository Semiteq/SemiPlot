using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;

using AwesomeAssertions;

using SemiPlot.UI.Localization;
using SemiPlot.UI.Navigation;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Navigation;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class NavigationBarViewTests
{
	[AvaloniaFact]
	public void BarCaptions_ComeFromTheGeneratedResourceAccessor()
	{
		var view = CreateView();

		var captions = view.GetLogicalDescendants()
			.OfType<ContentControl>()
			.Select(control => control.Content as string)
			.ToList();

		captions.Should()
			.Contain(Resources.NavigationJumpToNow)
			.And.Contain(Resources.NavigationSticky)
			.And.Contain(Resources.NavigationDelta);
	}

	// No text box and no axis caption may return to the bar.
	[AvaloniaFact]
	public void TheBar_CarriesNoAxisEditor()
	{
		var view = CreateView();

		view.GetLogicalDescendants().OfType<TextBox>().Should().BeEmpty();
		view.GetLogicalDescendants()
			.OfType<ContentControl>()
			.Should()
			.HaveCount(3, "Jump to Now, Sticky and Delta are the whole bar");
	}

	[AvaloniaFact]
	public void TheTimeGroupAndTheToolGroup_AreSeparatedByAHairline()
	{
		var view = CreateView();

		var separator = view.GetLogicalDescendants()
			.OfType<Border>()
			.Single(border => border.Name == "GroupSeparator");

		separator.Width.Should().Be(1.0);
	}

	private static NavigationBarView CreateView()
	{
		var (_, bar) = NavigationBarTestBuilder.CreateBar();

		return new NavigationBarView { DataContext = bar };
	}
}
