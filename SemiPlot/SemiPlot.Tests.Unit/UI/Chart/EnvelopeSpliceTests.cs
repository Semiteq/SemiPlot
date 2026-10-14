using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class EnvelopeSpliceTests
{
	private const int ReportedColumns = 512;
	private static readonly DateTime _archiveStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _windowFrom = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _windowTo = _windowFrom.AddHours(1.0);

	// The margins read around the window: 07:00 to 10:00, one column per half hour.
	private static readonly FetchRange _expanded = HistoryPrefetch.Expand(
		_windowFrom, _windowTo, AggregationLayer.Raw, ReportedColumns, _archiveStart);

	private static readonly ReadSpan _expandedSpan = new(_expanded.FromUtc, _expanded.ToUtc);

	private static readonly PenHistoryEnvelope _inHand =
		Envelope(1.0, [.. Enumerable.Range(0, 7).Select(HalfHoursAfterSeven)]);

	[Fact]
	public void AReadWithBothMarginsReplacesEveryColumn()
	{
		var read = Envelope(5.0, [_windowFrom.AddMinutes(-50.0), _windowTo.AddMinutes(50.0)]);
		var range = HistoryPrefetch.Expand(
			_windowFrom.AddMinutes(10.0),
			_windowTo.AddMinutes(10.0),
			AggregationLayer.Raw,
			ReportedColumns,
			_archiveStart);

		EnvelopeSplice.Over(read, _inHand, range, _expandedSpan).Envelope.Should().BeSameAs(read);
	}

	[Fact]
	public void AReadWithoutMarginsKeepsTheColumnsInHandOnBothSidesOfIt()
	{
		var (from, to) = (_windowFrom.AddMinutes(-20.0), _windowTo.AddMinutes(-20.0));
		var read = Envelope(5.0, [from, from.AddMinutes(30.0)]);

		var (drawn, _) = EnvelopeSplice.Over(read, _inHand, Visible(from, to, withRightMargin: false), _expandedSpan);

		drawn.Timestamps.Should().Equal(
			HalfHoursAfterSeven(0), HalfHoursAfterSeven(1), from, from.AddMinutes(30.0),
			HalfHoursAfterSeven(4), HalfHoursAfterSeven(5), HalfHoursAfterSeven(6));
		drawn.Center.Should().Equal(1.0, 1.0, 5.0, 5.0, 1.0, 1.0, 1.0);
		drawn.Min.Should().Equal(drawn.Center);
		drawn.Max.Should().Equal(drawn.Center);
	}

	[Fact]
	public void TheSeedRowBeforeTheRangeBoundsTheColumnsKeptOnTheLeft()
	{
		var seed = HalfHoursAfterSeven(1).AddMinutes(-1.0);
		var read = Envelope(5.0, [seed, _windowFrom, _windowTo.AddMinutes(-1.0)]);

		var range = Visible(_windowFrom, _windowTo, withRightMargin: true);

		var (drawn, _) = EnvelopeSplice.Over(read, _inHand, range, _expandedSpan);

		drawn.Timestamps.Should().Equal(HalfHoursAfterSeven(0), seed, _windowFrom, _windowTo.AddMinutes(-1.0));
	}

	[Fact]
	public void AColumnOfTheReadAtItsRangeEndIsNotDrawnTwice()
	{
		var read = Envelope(5.0, [_windowFrom, _windowTo]);

		var range = Visible(_windowFrom, _windowTo, withRightMargin: false);

		var (drawn, _) = EnvelopeSplice.Over(read, _inHand, range, _expandedSpan);

		drawn.Timestamps.Should().Equal(
			HalfHoursAfterSeven(0), HalfHoursAfterSeven(1), _windowFrom, _windowTo,
			HalfHoursAfterSeven(5), HalfHoursAfterSeven(6));
	}

	[Fact]
	public void AReadOffTheRangeInHandKeepsNothing()
	{
		var (from, to) = (_windowFrom.AddHours(-4.0), _windowTo.AddHours(-4.0));
		var read = Envelope(5.0, [from, to.AddMinutes(-1.0)]);

		EnvelopeSplice.Over(read, _inHand, Visible(from, to, withRightMargin: false), _expandedSpan)
			.Envelope.Should().BeSameAs(read);
	}

	[Fact]
	public void AReadWithNoRangeInHandKeepsNothing()
	{
		var read = Envelope(5.0, [_windowFrom, _windowTo.AddMinutes(-1.0)]);

		EnvelopeSplice.Over(read, _inHand, Visible(_windowFrom, _windowTo, withRightMargin: false), inHandSpan: null)
			.Envelope.Should().BeSameAs(read);
	}

	[Fact]
	public void TheSpanOfASplicedEnvelopeReachesThroughTheColumnsKept()
	{
		var (from, to) = (_windowFrom.AddMinutes(-20.0), _windowTo.AddMinutes(-20.0));
		var read = Envelope(5.0, [from, from.AddMinutes(30.0)]);

		var (_, span) = EnvelopeSplice.Over(read, _inHand, Visible(from, to, withRightMargin: false), _expandedSpan);

		span.Should().Be(_expandedSpan);
	}

	[Fact]
	public void TheSpanOfAReadWithItsMarginsIsItsRange()
	{
		var read = Envelope(5.0, [_windowFrom.AddMinutes(-50.0), _windowTo.AddMinutes(50.0)]);
		var range = HistoryPrefetch.Expand(
			_windowFrom.AddMinutes(-10.0),
			_windowTo.AddMinutes(-10.0),
			AggregationLayer.Raw,
			ReportedColumns,
			_archiveStart);

		var (_, span) = EnvelopeSplice.Over(read, _inHand, range, _expandedSpan);

		span.Should().Be(new ReadSpan(range.FromUtc, range.ToUtc));
	}

	[Fact]
	public void TheSpanOfAReadOffTheSpanInHandIsItsRange()
	{
		var (from, to) = (_windowFrom.AddHours(-4.0), _windowTo.AddHours(-4.0));
		var read = Envelope(5.0, [from, to.AddMinutes(-1.0)]);

		var (_, span) = EnvelopeSplice.Over(read, _inHand, Visible(from, to, withRightMargin: false), _expandedSpan);

		span.Should().Be(new ReadSpan(from, to));
	}

	private static FetchRange Visible(DateTime from, DateTime to, bool withRightMargin)
	{
		return HistoryPrefetch.Visible(from, to, AggregationLayer.Raw, ReportedColumns, _archiveStart, withRightMargin);
	}

	private static DateTime HalfHoursAfterSeven(int count)
	{
		return _windowFrom.AddHours(-1.0).AddMinutes(30.0 * count);
	}

	private static PenHistoryEnvelope Envelope(double value, IReadOnlyList<DateTime> timestamps)
	{
		IReadOnlyList<double> values = [.. timestamps.Select(_ => value)];

		return new PenHistoryEnvelope(1, timestamps, values, values, values);
	}
}
