using AwesomeAssertions;

using ScottPlot;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

// Each probe is a band of rows, since a 1 px stroke antialiases over two.
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class LogAxisRenderTests
{
	private const int PlotWidth = 800;
	private const int PlotHeight = 500;
	private const int PenId = 1;
	private const string PenColorHex = "#ff0000";
	private const double BottomDecade = 1e-6;
	private const double MiddleDecade = 1e-4;
	private const double TopDecade = 1e-2;
	private const double BelowTheAxis = 1e-9;
	private const double PinnedFloorPixels = 2.0;
	private const double MiddleTolerancePixels = 2.0;
	private const double FloorTolerancePixels = 0.5;
	private const int MiddleDecadePlateau = 1;
	private const int ZeroPlateau = 3;
	private const int BelowTheAxisPlateau = 4;

	private static readonly double[] _plateaus = [BottomDecade, MiddleDecade, TopDecade, 0.0, BelowTheAxis];
	private static readonly DateTime _start = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	private static readonly TimeSpan _plateauLength = TimeSpan.FromMinutes(1.0);

	[Fact]
	public void TheMiddleDecade_DrawsAtTheDataAreasVerticalMiddle()
	{
		var render = Render();

		var rows = render.RowsAtPlateau(MiddleDecadePlateau);

		rows.Should().NotBeEmpty("the plateau at 1e-4 lies inside the 1e-6..1e-2 axis");
		var middle = (render.DataRect.Top + render.DataRect.Bottom) / 2.0;
		BandCentre(rows).Should().BeApproximately(
			middle,
			MiddleTolerancePixels,
			"1e-4 is two decades above 1e-6 and two below 1e-2");
	}

	[Fact]
	public void AZeroPlateau_DrawsTwoPixelsAboveTheDataAreasBottomRow()
	{
		var render = Render();

		var rows = render.RowsAtPlateau(ZeroPlateau);

		rows.Should().NotBeEmpty("a non-positive sample is pinned above the axis frame, not hidden under it");
		BandCentre(rows).Should().BeApproximately(
			render.DataRect.Bottom - PinnedFloorPixels,
			FloorTolerancePixels);
	}

	[Fact]
	public void APlateauBelowTheAxisMinimum_DrawsOnTheFloorBesideZero()
	{
		var render = Render();

		var rows = render.RowsAtPlateau(BelowTheAxisPlateau);

		rows.Should().NotBeEmpty("a positive sample under the axis minimum lands on the floor, as zero does");
		BandCentre(rows).Should().BeApproximately(
			render.DataRect.Bottom - PinnedFloorPixels,
			FloorTolerancePixels);
	}

	private static double BandCentre(IReadOnlyList<int> rows)
	{
		// A row index names the pixel spanning [row, row + 1), so the band's centre sits half a pixel lower.
		return rows.Average() + 0.5;
	}

	private static RenderedLogAxis Render()
	{
		using var plot = new Plot();
		var line = new EnvelopeLine();
		line.Axes.XAxis = plot.Axes.Bottom;
		plot.Add.Plottable(line);

		var pen = new TrendPenState(
			new Pen(PenId, "Pressure", ["Group A"], PenColorHex, LineStyle: PenLineStyle.Stepped),
			line);
		pen.LoadHistory(SteppedHistory());

		var binder = new ChartAxisBinder(plot);
		binder.Apply(
			[new PenScale(PenId, BottomDecade, TopDecade, ScaleMode.Manual, IsActive: true, IsLogarithmic: true)],
			new Dictionary<int, TrendPenState> { [PenId] = pen });
		plot.Axes.SetLimitsX(LocalTimeAxis.ToAxis(_start), LocalTimeAxis.ToAxis(PlateauStart(_plateaus.Length)));

		using var image = plot.GetImage(PlotWidth, PlotHeight);
		var dataRect = plot.RenderManager.LastRender.Layout.DataRect;
		var plateauColumns = Enumerable
			.Range(0, _plateaus.Length)
			.Select(plateau => PlateauColumn(plot, dataRect, plateau))
			.ToList();

		return new RenderedLogAxis(image.GetArrayRGB(), dataRect, plateauColumns);
	}

	private static int PlateauColumn(Plot plot, PixelRect dataRect, int plateau)
	{
		var centre = LocalTimeAxis.ToAxis(PlateauStart(plateau) + (_plateauLength / 2));

		return (int)Math.Round(plot.Axes.Bottom.GetPixel(centre, dataRect));
	}

	// One sample opens each plateau and a last one closes the final plateau.
	private static PenHistoryEnvelope SteppedHistory()
	{
		var values = new List<double>([.. _plateaus, _plateaus[^1]]);
		var timestamps = Enumerable.Range(0, values.Count).Select(PlateauStart).ToList();

		return new PenHistoryEnvelope(PenId, timestamps, values, values, values);
	}

	private static DateTime PlateauStart(int plateau)
	{
		return _start + (_plateauLength * plateau);
	}

	private sealed record RenderedLogAxis(byte[,,] Pixels, PixelRect DataRect, IReadOnlyList<int> PlateauColumns)
	{
		public IReadOnlyList<int> RowsAtPlateau(int plateau)
		{
			return RedStroke.RowsIn(Pixels, DataRect, PlateauColumns[plateau]);
		}
	}
}
