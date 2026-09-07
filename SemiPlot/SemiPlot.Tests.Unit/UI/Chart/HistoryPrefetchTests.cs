using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class HistoryPrefetchTests
{
	private const int ReportedColumns = 512;
	private static readonly DateTime _archiveStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _windowFrom = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

	[Theory]
	[InlineData(1)]
	[InlineData(60)]
	[InlineData(1440)]
	public void Expand_WidensByOneWindowWidthOnEachSide(int windowMinutes)
	{
		var windowTo = _windowFrom.AddMinutes(windowMinutes);

		var range = HistoryPrefetch.Expand(
			_windowFrom, windowTo, AggregationLayer.Raw, ReportedColumns, _archiveStart);

		range.FromUtc.Should().Be(_windowFrom.AddMinutes(-windowMinutes));
		range.ToUtc.Should().Be(windowTo.AddMinutes(windowMinutes));
		range.WindowWidth.Should().Be(TimeSpan.FromMinutes(windowMinutes));
	}

	[Theory]
	[InlineData(256)]
	[InlineData(700)]
	[InlineData(2048)]
	public void Expand_KeepsOneColumnPerPixelAcrossTheWholeRange(int reportedColumns)
	{
		var range = HistoryPrefetch.Expand(
			_windowFrom, _windowFrom.AddHours(1.0), AggregationLayer.Minute, reportedColumns, _archiveStart);

		range.ColumnTarget.Should().Be(3 * reportedColumns);
		range.Layer.Should().Be(AggregationLayer.Minute);
	}

	[Fact]
	public void Expand_ClampsTheLeftEdgeToTheFirstStoredSample()
	{
		var windowFrom = _archiveStart.AddMinutes(20.0);
		var windowTo = windowFrom.AddHours(1.0);

		var range = HistoryPrefetch.Expand(
			windowFrom, windowTo, AggregationLayer.Raw, ReportedColumns, _archiveStart);

		range.FromUtc.Should().Be(_archiveStart);
		range.ToUtc.Should().Be(windowTo.AddHours(1.0));
		range.WindowWidth.Should().Be(TimeSpan.FromHours(1.0));
	}

	[Theory]
	[InlineData(0, true)]
	[InlineData(-30, true)]
	[InlineData(30, true)]
	[InlineData(-31, false)]
	[InlineData(31, false)]
	[InlineData(-60, false)]
	[InlineData(60, false)]
	public void Covers_HoldsUntilTheWindowLeavesTheInnerBand(int panMinutes, bool isCovered)
	{
		var fetched = OneHourFetch();
		var pannedFrom = _windowFrom.AddMinutes(panMinutes);

		var covers = HistoryPrefetch.Covers(
			fetched, pannedFrom, pannedFrom.AddHours(1.0), AggregationLayer.Raw, ReportedColumns);

		covers.Should().Be(isCovered);
	}

	[Fact]
	public void Covers_IsFalseAfterAZoom()
	{
		var fetched = OneHourFetch();

		var covers = HistoryPrefetch.Covers(
			fetched,
			_windowFrom.AddMinutes(15.0),
			_windowFrom.AddMinutes(45.0),
			AggregationLayer.Raw,
			ReportedColumns);

		covers.Should().BeFalse();
	}

	[Fact]
	public void Covers_IsFalseOnALayerChange()
	{
		var fetched = OneHourFetch();

		var covers = HistoryPrefetch.Covers(
			fetched, _windowFrom, _windowFrom.AddHours(1.0), AggregationLayer.Minute, ReportedColumns);

		covers.Should().BeFalse();
	}

	[Fact]
	public void Covers_IsFalseOnAColumnTargetChange()
	{
		var fetched = OneHourFetch();

		var covers = HistoryPrefetch.Covers(
			fetched, _windowFrom, _windowFrom.AddHours(1.0), AggregationLayer.Raw, ReportedColumns / 2);

		covers.Should().BeFalse();
	}

	// At the archive's first sample the left margin is clamped away, and a guard derived from the window
	// width would then reject every pan, including one the untouched right margin fully covers.
	[Theory]
	[InlineData(0, true)]
	[InlineData(30, true)]
	[InlineData(31, false)]
	public void Covers_AtTheArchiveStart_GuardsOnlyTheMarginItActuallyHas(int panMinutes, bool isCovered)
	{
		var windowFrom = _archiveStart;
		var fetched = HistoryPrefetch.Expand(
			windowFrom, windowFrom.AddHours(1.0), AggregationLayer.Raw, ReportedColumns, _archiveStart);

		fetched.FromUtc.Should().Be(_archiveStart);

		var pannedFrom = windowFrom.AddMinutes(panMinutes);

		var covers = HistoryPrefetch.Covers(
			fetched, pannedFrom, pannedFrom.AddHours(1.0), AggregationLayer.Raw, ReportedColumns);

		covers.Should().Be(isCovered);
	}

	private static FetchRange OneHourFetch()
	{
		return HistoryPrefetch.Expand(
			_windowFrom, _windowFrom.AddHours(1.0), AggregationLayer.Raw, ReportedColumns, _archiveStart);
	}
}
