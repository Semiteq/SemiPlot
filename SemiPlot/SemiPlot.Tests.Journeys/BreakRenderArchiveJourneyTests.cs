using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using ScottPlot;

using SemiPlot.Core.Data;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tests.Data.Integration;
using SemiPlot.Tools.ArchiveSeeder;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Journeys;

// The join of two halves this repository already proves apart. PostgresHistoryReadTests reads the seeded
// break out of a real archive and counts the fold's one NaN anchor; ChartGapRenderTests hands a NaN column
// to the chart and measures the pixels it leaves blank. Neither says the archive's own break reaches the
// canvas. This journey drives the composed path — AddPostgresData, TrendCoordinator, TrendChartViewModel —
// over a clone of the seeded template and measures the rendered pixels, so a change that loses the break
// anywhere between the statement and the canvas fails here rather than on a screen.
//
// The pixel technique is ChartGapRenderTests': Plot.GetImage rasterises through SkiaSharp with no Avalonia
// in the loop and populates RenderManager.LastRender, so DataRect comes from the same render the pixels do.
// Sampling is by band, never by byte. The probe differs in one way, because the pen colours here come out
// of the archive's own catalogue rather than being chosen by the test: it asks whether a column carries any
// chroma at all, which on this plot means drawn data — the frame, the grid and the labels are greyscale and
// every catalogue colour is a saturated hue.
[Collection(ArchiveJourneyCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Integration")]
public sealed class BreakRenderArchiveJourneyTests(
	PostgresContainerFixture postgresContainerFixture,
	SeededArchive seededArchive)
	: IClassFixture<SeededArchive>
{
	private const int PlotWidth = 800;
	private const int PlotHeight = 500;

	// The chroma a column carries to count as drawn data, and the chroma a pen colour carries to be worth
	// probing for. It is the threshold ChartGapRenderTests measures its red dominance against, chosen so an
	// antialiased edge of a grid line or a tick label does not answer it while the band, drawn at a fifth of
	// the pen's opacity, does.
	private const int ChromaThreshold = 24;

	// Guards the window-to-break ratio, the way ChartGapRenderTests guards its own: a later change to the
	// navigation controller's opening width that shrinks the break towards a single pixel column fails here
	// rather than quietly turning the blank-column assertion into a formality.
	private const int MinimumBreakColumns = 12;

	// Columns dropped from each end of the break before probing it, for the same reason ColumnCarriesPenColor
	// insets the rows it reads from the data rectangle. A bound falls between two pixel columns rather than on
	// one, so the segment terminating at the q = 32 row and the segment resuming at the q = 16 row are
	// antialiased into the column the bound rounds away from. That is the break's edge, not a line across it.
	private const int BoundaryInsetColumns = 1;

	// How far either side of the break the curves must still be drawn. Well inside BreakPlan.MinimumRun's
	// five minutes of archiving on both sides of every break, and well over the 40 s cap the seeder puts on
	// the interval between two rows (RawLayerGenerator's IntervalCapFactor times the default
	// --change-seconds), so the probe column falls inside a drawn run rather than between two of its rows.
	private static readonly TimeSpan _probeOffset = TimeSpan.FromMinutes(1);

	private static readonly ArchiveTimeConverter _timeConverter = new(ArchiveProviderFactory.SourceTimeZone);

	[AvaloniaFact]
	public async Task TheFirstSeededBreakLeavesTheRenderedCurvesBroken()
	{
		postgresContainerFixture.RequireAvailable();

		await using var services = ArchiveProviderFactory.Build(seededArchive.Database.ReaderConnectionString);
		var dataProvider = services.GetRequiredService<IDataProvider>();
		var dataScheduler = services.GetRequiredService<IScheduler>();
		var catalogue = await dataProvider.QueryPensAsync();
		var extent = await dataProvider.QueryArchiveExtentAsync();

		catalogue.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(catalogue));
		extent.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(extent));
		catalogue.Value.Should().AllSatisfy(pen => Chroma(pen.Color).Should().BeGreaterThanOrEqualTo(
			ChromaThreshold,
			"the blank-column probe reads chroma, so a greyscale pen would be invisible to it"));

		using var coordinator = new TrendCoordinator(
			dataProvider, catalogue.Value, dataScheduler, ImmediateScheduler.Instance);
		using var chart = new TrendChartViewModel(
			coordinator, dataScheduler, ImmediateScheduler.Instance, NullLogger<TrendChartViewModel>.Instance);

		foreach (var pen in catalogue.Value)
		{
			chart.AddPen(pen);
		}

		var stopped = FirstBreak();
		OpenWindowOn(chart.Navigation, extent.Value.FirstUtc, stopped);
		coordinator.Start();

		var historyApplied = chart.HistoryApplied.FirstAsync().ToTask();
		chart.RequestInitialHistory();
		await historyApplied;

		var (pixels, dataRect) = Render(chart);

		AssertTheBreakIsDrawnAsOne(pixels, dataRect, chart.Plot, stopped);
	}

	// The seeder's own break, computed rather than read back out of the archive: BreakPlan is what placed
	// it, and every clone comes from a template seeded with ArchiveTemplate.Slice — the pairing
	// PostgresHistoryReadTests generates its own expectations from.
	private static ArchiveBreak FirstBreak()
	{
		var stopped = BreakPlan.Create(ArchiveTemplate.Slice).Breaks[0];

		return new ArchiveBreak(_timeConverter.ToUtc(stopped.Start), _timeConverter.ToUtc(stopped.End));
	}

	// The window the application opens at startup is the archive's last hour (App.axaml.cs:137, through
	// ChartNavigationController.SeedFromArchiveExtent), which holds no break. This opens the same controller
	// on the break instead, through the entry point SeedFromArchiveExtent itself calls, and keeps the width
	// it was constructed with. No zoom and no pan, deliberately: a navigation gesture re-queries history
	// through the debouncer on the data scheduler, and that result would land on the pen states while this
	// thread is rendering them.
	private static void OpenWindowOn(
		ChartNavigationController navigation,
		DateTime firstSampleUtc,
		ArchiveBreak stopped)
	{
		var margin = (navigation.To - navigation.From - (stopped.EndUtc - stopped.StartUtc)) / 2;

		navigation.TrackDataExtents(firstSampleUtc, stopped.EndUtc + margin);
	}

	// The X limits are the view's own seam — TrendChartView.ApplyWindow is their only writer, called once
	// while attaching the view model and again from OnNavigationWindowChanged on every later gesture — and
	// this journey builds no view, so it stands in for that one line. Everything else the render reads is
	// the view model's own work: the curves, and the per-pen Y scales ChartAxisBinder applied.
	private static (byte[,,] Pixels, PixelRect DataRect) Render(TrendChartViewModel chart)
	{
		chart.Plot.Axes.SetLimitsX(
			LocalTimeAxis.ToAxis(chart.Navigation.From), LocalTimeAxis.ToAxis(chart.Navigation.To));

		using var image = chart.Plot.GetImage(PlotWidth, PlotHeight);

		return (image.GetArrayRGB(), chart.Plot.RenderManager.LastRender.Layout.DataRect);
	}

	// The break's own bounds rather than the marker row's: the q = 32 row is the last row of the archiving
	// run, so it sits at or before the break opens, and the q = 16 row opens the resumed run exactly at the
	// break's end. Every column strictly between the two, less the inset at each end, is an interval no pen
	// has a sample in.
	private static void AssertTheBreakIsDrawnAsOne(
		byte[,,] pixels,
		PixelRect dataRect,
		Plot plot,
		ArchiveBreak stopped)
	{
		var firstProbedColumn = ColumnAt(plot, stopped.StartUtc) + 1 + BoundaryInsetColumns;
		var lastProbedColumn = ColumnAt(plot, stopped.EndUtc) - 1 - BoundaryInsetColumns;
		var breakColumns = Enumerable
			.Range(firstProbedColumn, lastProbedColumn - firstProbedColumn + 1)
			.ToList();

		breakColumns.Count.Should().BeGreaterThanOrEqualTo(
			MinimumBreakColumns,
			"the window-to-break ratio must leave the break several pixel columns wide, or a blank column "
			+ "proves only that antialiasing missed it");
		breakColumns
			.Where(column => ColumnCarriesPenColor(pixels, dataRect, column))
			.Should().BeEmpty(
				"no curve may cross the interval the SCADA project spent stopped: the archive's own break "
				+ "has to reach the canvas as a break rather than as a straight segment across it (probed "
				+ "columns " + firstProbedColumn + " to " + lastProbedColumn + ")");
		ColumnCarriesPenColor(pixels, dataRect, ColumnAt(plot, stopped.StartUtc - _probeOffset))
			.Should().BeTrue(
				"the archiving run before the break is drawn, so the break columns are not blank for want "
				+ "of any rendering");
		ColumnCarriesPenColor(pixels, dataRect, ColumnAt(plot, stopped.EndUtc + _probeOffset))
			.Should().BeTrue("the curves resume on the q = 16 row instead of staying broken to the window's end");
	}

	private static int ColumnAt(Plot plot, DateTime timestampUtc)
	{
		var pixel = plot.GetPixel(new Coordinates(LocalTimeAxis.ToAxis(timestampUtc), 0.0));

		return (int)Math.Round(pixel.X);
	}

	private static bool ColumnCarriesPenColor(byte[,,] pixels, PixelRect dataRect, int column)
	{
		var firstRow = (int)Math.Ceiling(dataRect.Top) + 1;
		var lastRow = (int)Math.Floor(dataRect.Bottom) - 1;

		for (var row = firstRow; row <= lastRow; row++)
		{
			if (Chroma(pixels[row, column, 0], pixels[row, column, 1], pixels[row, column, 2]) >= ChromaThreshold)
			{
				return true;
			}
		}

		return false;
	}

	private static int Chroma(string colorHex)
	{
		var color = new Color(colorHex);

		return Chroma(color.R, color.G, color.B);
	}

	private static int Chroma(byte red, byte green, byte blue)
	{
		return Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue));
	}

	private readonly record struct ArchiveBreak(DateTime StartUtc, DateTime EndUtc);
}
