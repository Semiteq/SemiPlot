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
using SemiPlot.Tools.ArchiveSeeder;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Integration.Journeys;

// Proves the composed path — AddPostgresData, TrendCoordinator, TrendChartViewModel — draws the archive's
// own break as a gap, using ChartGapRenderTests' pixel technique (Plot.GetImage through SkiaSharp, sampled
// by band): a column counts as drawn when it carries any chroma, since the frame, grid and labels are grey.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Integration")]
public sealed class BreakRenderArchiveJourneyTests(
	SeededArchive seededArchive)
	: IClassFixture<SeededArchive>
{
	private const int PlotWidth = 800;
	private const int PlotHeight = 500;

	// The chroma a column must carry to count as drawn data: high enough that an antialiased grid line or
	// tick label doesn't answer it, low enough that a pen's band at a fifth opacity does.
	private const int ChromaThreshold = 24;

	// Guards the window-to-break ratio, the way ChartGapRenderTests guards its own: a later change to the
	// navigation controller's opening width that shrinks the break towards a single pixel column fails here
	// rather than quietly turning the blank-column assertion into a formality.
	private const int MinimumBreakColumns = 12;

	// Columns dropped from each end of the break before probing, since a bound falls between two pixel
	// columns: the segment ending at q = 32 and the segment resuming at q = 16 antialias into the column
	// the bound rounds away from — that is the break's edge, not a line across it.
	private const int BoundaryInsetColumns = 1;

	// How far either side of the break the curves must still be drawn: inside BreakPlan.MinimumRun's five
	// minutes of archiving on both sides, and past the seeder's row-interval cap, so the probe column falls
	// inside a drawn run rather than between two of its rows.
	private static readonly TimeSpan _probeOffset = TimeSpan.FromMinutes(1);

	private static readonly ArchiveTimeConverter _timeConverter = new(ArchiveProviderFactory.SourceTimeZone);

	[AvaloniaFact]
	public async Task TheFirstSeededBreakLeavesTheRenderedCurvesBroken()
	{
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

	// Opens the navigation controller on the break, not the startup window (App.axaml.cs:137's last hour,
	// which holds no break), keeping its constructed width. No zoom or pan: a gesture would re-query history
	// through the debouncer and land pen states while this thread renders them.
	private static void OpenWindowOn(
		ChartNavigationController navigation,
		DateTime firstSampleUtc,
		ArchiveBreak stopped)
	{
		var margin = (navigation.To - navigation.From - (stopped.EndUtc - stopped.StartUtc)) / 2;

		navigation.TrackDataExtents(firstSampleUtc, stopped.EndUtc + margin);
	}

	// TrendChartView.ApplyWindow is the X limits' only writer; this journey builds no view, so it stands in
	// for that one line. Everything else rendered is the view model's own work: curves and per-pen Y scales.
	private static (byte[,,] Pixels, PixelRect DataRect) Render(TrendChartViewModel chart)
	{
		chart.Plot.Axes.SetLimitsX(
			LocalTimeAxis.ToAxis(chart.Navigation.From), LocalTimeAxis.ToAxis(chart.Navigation.To));

		using var image = chart.Plot.GetImage(PlotWidth, PlotHeight);

		return (image.GetArrayRGB(), chart.Plot.RenderManager.LastRender.Layout.DataRect);
	}

	// Uses the break's own bounds, not the marker rows': q = 32 sits at or before the break opens and q = 16
	// opens the resumed run at its end, so every column strictly between the two, less the inset, has no sample.
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
