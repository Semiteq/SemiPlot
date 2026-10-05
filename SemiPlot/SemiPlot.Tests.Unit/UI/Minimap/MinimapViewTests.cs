using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;

using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Minimap;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Minimap;

[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class MinimapViewTests
{
	private const double LabelEdgeSlack = 8.0;

	[AvaloniaFact]
	public async Task TheStrip_HoldsOneBandBelowTheMarker()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var children = stand.StripCanvas.Children;

		var bandLayer = children.OfType<MinimapBand>().Should().ContainSingle().Subject;

		children.IndexOf(bandLayer).Should().BeLessThan(children.IndexOf(stand.Named<Border>("WindowHighlight")));
		bandLayer.IsHitTestVisible.Should().BeFalse("the strip under the band takes the pointer");
	}

	[AvaloniaFact]
	public async Task TheStrip_DrawsNoBaselineAndLabelsItsEndsUnderTheStrip()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var view = stand.View;
		var strip = stand.AreaInView(stand.StripCanvas);
		var first = stand.AreaInView(stand.Named<TextBlock>("ExtentFirstLabel"));
		var last = stand.AreaInView(stand.Named<TextBlock>("ExtentLastLabel"));

		stand.StripCanvas.Children.Select(child => child.Name).Should().Equal(
			["BandLayer", "WindowHighlight", "HoverLine"],
			"the strip draws the band, the marker over it and the hover line over both, and no line of its own");
		foreach (var label in new[] { first, last })
		{
			label.Width.Should().BePositive("each end label carries its time");
			label.Top.Should().BeGreaterThanOrEqualTo(strip.Bottom, "the labels sit under the strip, not on it");
			label.Bottom.Should().BeLessThanOrEqualTo(view.Bounds.Height, "the view must show the whole label");
		}

		first.Left.Should().BeInRange(0.0, LabelEdgeSlack, "the first sample's label sits at the left edge");
		last.Right.Should().BeInRange(
			view.Bounds.Width - LabelEdgeSlack, view.Bounds.Width, "the last sample's label sits at the right edge");
		first.Right.Should().BeLessThan(last.Left, "the two end labels must not overlap");
	}

	[AvaloniaFact]
	public async Task TheView_KeepsItsHeightWhenTheExtentFillsItsLabels()
	{
		using var stand = MinimapViewStand.ShowWithoutExtent();
		var lastLabel = stand.Named<TextBlock>("ExtentLastLabel");
		lastLabel.Text.Should().BeEmpty("no extent is read yet");
		var emptyHeight = stand.View.Bounds.Height;

		await stand.Model.LoadExtentAsync();
		Dispatcher.UIThread.RunJobs();

		lastLabel.Text.Should().NotBeEmpty("the extent landed");
		stand.View.Bounds.Height.Should().Be(emptyHeight, "the minimap row never jumps when the labels fill");
	}

	[AvaloniaFact]
	public async Task TheBand_CoversTheStrip()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);

		stand.BandLayer.Bounds.Size.Should().Be(stand.StripCanvas.Bounds.Size);
	}

	[AvaloniaFact]
	public async Task APublishedBand_GivesTheControlFiguresInThePenColour()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var model = stand.Model;
		model.Band.Should().NotBeNull();

		stand.BandLayer.Figures.Should().NotBeEmpty();
		stand.BandLayer.Figures.Should().BeEquivalentTo(
			MinimapGeometry.BandFigures(
				model.Band!,
				model.ViewModel.ExtentFirst,
				model.ViewModel.ExtentLast,
				stand.StripCanvas.Bounds.Width,
				stand.StripCanvas.Bounds.Height),
			options => options.WithStrictOrdering());
		stand.BandLayer.BandColor.Should().BeOfType<SolidColorBrush>()
			.Which.Color.Should().Be(Color.Parse(model.ViewModel.BandFeed.BandColor!));
	}

	[AvaloniaFact]
	public async Task APan_LeavesTheBandFiguresAsTheyWere()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var viewModel = stand.Model.ViewModel;
		var figures = stand.BandLayer.Figures;
		var startBefore = viewModel.WindowStartFraction;

		stand.Model.Navigation.PanBy(TimeSpan.FromHours(-12.0));

		viewModel.WindowStartFraction.Should().NotBe(startBefore, "the pan must move the marker");
		stand.BandLayer.Figures.Should().BeSameAs(figures, "only the bounds, the band and the size reshape the band");
	}

	[AvaloniaFact]
	public async Task HidingTheDrawnPen_TakesTheFiguresAway()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var chart = stand.Model.Chart;
		stand.BandLayer.Figures.Should().NotBeEmpty();

		chart.SetPenVisibility(1, isVisible: false);
		stand.LandBandRead();
		chart.SetPenVisibility(2, isVisible: false);
		stand.LandBandRead();

		stand.Model.Band.Should().BeNull();
		stand.BandLayer.Figures.Should().BeEmpty();
	}
}
