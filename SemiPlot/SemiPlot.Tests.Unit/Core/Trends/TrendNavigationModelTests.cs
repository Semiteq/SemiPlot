using AwesomeAssertions;

using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Trends;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class TrendNavigationModelTests
{
	private static readonly DateTime _origin = new(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _firstSample = new(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void OnLiveEdge_WhenSticky_AdvancesWindowAndKeepsWidth()
	{
		var model = Model(isSticky: true, windowWidth: TimeSpan.FromMinutes(10.0));
		var width = model.Width;
		var now = _origin.AddMinutes(5.0);

		model.OnLiveEdge(now);

		model.To.Should().Be(now);
		model.Width.Should().Be(width);
		model.From.Should().Be(now - width);
		model.IsSticky.Should().BeTrue();
	}

	[Fact]
	public void OnLiveEdge_WhenNotSticky_LeavesWindowUnchanged()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromMinutes(10.0));
		var from = model.From;
		var to = model.To;

		model.OnLiveEdge(_origin.AddMinutes(5.0));

		model.From.Should().Be(from);
		model.To.Should().Be(to);
	}

	[Fact]
	public void JumpToNow_ReattachesStickyAndPlacesNowAtRightEdge()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromMinutes(10.0));
		var width = model.Width;
		var now = _origin.AddHours(3.0);

		model.JumpToNow(now);

		model.IsSticky.Should().BeTrue();
		model.To.Should().Be(now);
		model.From.Should().Be(now - width);
		model.Width.Should().Be(width);
	}

	[Fact]
	public void Pan_PastLiveEdgeIntoThePast_AutoDetachesSticky()
	{
		var model = Model(isSticky: true, windowWidth: TimeSpan.FromMinutes(10.0));
		var now = model.To;

		model.Pan(TimeSpan.FromMinutes(-15.0), now);

		model.IsSticky.Should().BeFalse();
		(now > model.To).Should().BeTrue();
	}

	[Fact]
	public void Pan_ForwardPastLiveEdge_LiveEdgeBeforeWindowStart_AutoDetachesSticky()
	{
		var model = Model(isSticky: true, windowWidth: TimeSpan.FromMinutes(10.0));
		var now = model.To;

		// Pan far enough that the live edge lands before the window's new From.
		model.Pan(TimeSpan.FromMinutes(30.0), now);

		model.IsSticky.Should().BeFalse();
		(now < model.From).Should().BeTrue();
	}

	[Fact]
	public void Pan_WhileLiveEdgeStaysInsideWindow_KeepsSticky()
	{
		var model = Model(isSticky: true, windowWidth: TimeSpan.FromMinutes(10.0));
		var now = model.To - TimeSpan.FromMinutes(6.0);

		model.Pan(TimeSpan.FromMinutes(-3.0), now);

		model.IsSticky.Should().BeTrue();
		(now <= model.To && now >= model.From).Should().BeTrue();
	}

	[Fact]
	public void Pan_BackBeforeFirstSample_ClampsFromToFirstSample()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromMinutes(10.0));
		var width = model.Width;

		model.Pan(TimeSpan.FromDays(-365.0), now: _origin);

		model.From.Should().Be(_firstSample);
		model.Width.Should().Be(width);
		model.To.Should().Be(_firstSample + width);
	}

	[Fact]
	public void Zoom_OutBeyondOneYear_ClampsWidthToOneYear()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromDays(200.0));

		model.Zoom(factor: 10.0, anchor: model.To);

		model.Width.Should().Be(TimeSpan.FromDays(365.0));
	}

	[Fact]
	public void Zoom_InBelowOneSecond_ClampsWidthToOneSecond()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromSeconds(2.0));

		model.Zoom(factor: 0.1, anchor: model.From);

		model.Width.Should().Be(TimeSpan.FromSeconds(1.0));
	}

	[Fact]
	public void Zoom_HoldsAnchorPositionWhileScalingWidth()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromMinutes(10.0));
		var anchor = model.From + TimeSpan.FromMinutes(2.0);
		var anchorFractionBefore = (anchor - model.From) / model.Width;

		model.Zoom(factor: 0.5, anchor: anchor);

		// Width quantization snaps onto the zoom ladder (near, not exactly, the requested half-width); the
		// anchor's relative position is still preserved.
		var anchorFractionAfter = (anchor - model.From) / model.Width;
		anchorFractionAfter.Should().BeApproximately(anchorFractionBefore, 1e-9);
		model.Width.Should().BeCloseTo(TimeSpan.FromMinutes(5.0), TimeSpan.FromMinutes(1.0));
	}

	[Fact]
	public void Zoom_InThenOut_ReturnsToOriginWindowWithinTolerance()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromHours(1.0));
		var anchor = model.From + (model.Width / 2.0);

		// Capture the origin after one zoom snaps onto the ladder, so the cycle is measured between two
		// on-ladder windows (the starting 1h width is not itself a ladder point).
		model.Zoom(factor: 0.8, anchor: anchor);
		model.Zoom(factor: 1.25, anchor: anchor);
		var fromOrigin = model.From;
		var toOrigin = model.To;
		var widthOrigin = model.Width;

		// Centred anchor: reciprocal in/out factors land on the same ladder points, so the cycle retraces
		// to the origin window exactly.
		for (var notch = 0; notch < 8; notch++)
		{
			model.Zoom(factor: 0.8, anchor: anchor);
		}

		for (var notch = 0; notch < 8; notch++)
		{
			model.Zoom(factor: 1.25, anchor: anchor);
		}

		model.Width.Should().BeCloseTo(widthOrigin, TimeSpan.FromMilliseconds(1.0));
		model.From.Should().BeCloseTo(fromOrigin, TimeSpan.FromMilliseconds(1.0));
		model.To.Should().BeCloseTo(toOrigin, TimeSpan.FromMilliseconds(1.0));
	}

	[Fact]
	public void Zoom_OutFarPast_ClampsFromToFirstSample()
	{
		var model = Model(isSticky: true, windowWidth: TimeSpan.FromHours(1.0));
		var anchor = model.From + TimeSpan.FromMinutes(30.0);

		for (var notch = 0; notch < 40; notch++)
		{
			model.Zoom(factor: 1.25, anchor: anchor);
		}

		model.From.Should().BeOnOrAfter(_firstSample);
	}

	[Fact]
	public void Constructor_ClampsInitialWidthAboveOneYear()
	{
		var from = _firstSample;
		var to = from.AddDays(400.0);

		var model = new TrendNavigationModel(from, to, _firstSample, isSticky: false);

		model.Width.Should().Be(TimeSpan.FromDays(365.0));
		model.From.Should().Be(from);
	}

	[Fact]
	public void Constructor_WithNonPositiveWindow_Throws()
	{
		var act = () => new TrendNavigationModel(_origin, _origin, _firstSample, isSticky: false);

		act.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void SetWindow_DropsStickyAndSetsBothEdgesExactly()
	{
		var model = Model(isSticky: true, windowWidth: TimeSpan.FromMinutes(10.0));
		var from = _origin.AddMinutes(-97.0).AddMilliseconds(123.0);
		var to = _origin.AddMinutes(-31.0);

		model.SetWindow(from, to);

		model.From.Should().Be(from);
		model.To.Should().Be(to);
		model.IsSticky.Should().BeFalse();
	}

	[Fact]
	public void SetWindow_StartingBeforeFirstSample_MovesRightToFirstSampleAndKeepsTheWidth()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromMinutes(10.0));
		var width = TimeSpan.FromHours(2.0);
		var from = _firstSample.AddHours(-1.0);

		model.SetWindow(from, from + width);

		model.From.Should().Be(_firstSample);
		model.Width.Should().Be(width);
	}

	[Theory]
	[InlineData(0.2, 1.0)]
	[InlineData(400.0 * 86_400.0, 365.0 * 86_400.0)]
	public void SetWindow_WithASpanOutsideTheLimits_ClampsTheWidthAndKeepsTheStart(
		double requestedSeconds,
		double expectedSeconds)
	{
		var model = Model(isSticky: true, windowWidth: TimeSpan.FromHours(1.0));
		var from = _firstSample.AddHours(1.0);

		model.SetWindow(from, from.AddSeconds(requestedSeconds));

		model.From.Should().Be(from);
		model.Width.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
		model.IsSticky.Should().BeFalse();
	}

	[Theory]
	[InlineData(0.0)]
	[InlineData(-5.0)]
	public void SetWindow_WithAnEndNotAfterTheStart_Throws(double spanMinutes)
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromMinutes(10.0));
		var from = _origin.AddHours(-1.0);

		var act = () => model.SetWindow(from, from.AddMinutes(spanMinutes));

		act.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void SetWidth_FiveMinutes_KeepsTheRightEdgeAndSetsExactlyFiveMinutes()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromHours(1.0));
		var to = model.To;

		model.SetWidth(TimeSpan.FromMinutes(5.0));

		model.To.Should().Be(to);
		model.Width.Should().Be(TimeSpan.FromMinutes(5.0));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void SetWidth_LeavesStickyAsItWas(bool isSticky)
	{
		var model = Model(isSticky, windowWidth: TimeSpan.FromHours(1.0));

		model.SetWidth(TimeSpan.FromMinutes(3.0));

		model.IsSticky.Should().Be(isSticky);
	}

	[Theory]
	[InlineData(0.2, 1.0)]
	[InlineData(400.0 * 86_400.0, 365.0 * 86_400.0)]
	public void SetWidth_OutsideTheLimits_ClampsTheWidth(double requestedSeconds, double expectedSeconds)
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromHours(1.0));

		model.SetWidth(TimeSpan.FromSeconds(requestedSeconds));

		model.Width.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
	}

	[Fact]
	public void SetWidth_ReachingBeforeFirstSample_StartsAtFirstSampleAndKeepsTheWidth()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromHours(1.0));
		var width = TimeSpan.FromDays(2.0);

		model.SetWidth(width);

		model.From.Should().Be(_firstSample);
		model.Width.Should().Be(width);
	}

	// 5 min x 0.8 is 240 s; the nearest rung is 1.25^25 s, about 264.7 s.
	[Fact]
	public void SetWidth_ThenOneWheelNotch_LandsOnTheLadder()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromHours(1.0));
		model.SetWidth(TimeSpan.FromMinutes(5.0));

		model.Zoom(factor: 0.8, anchor: model.To);

		model.Width.Should().BeCloseTo(TimeSpan.FromSeconds(Math.Pow(1.25, 25.0)), TimeSpan.FromMilliseconds(1.0));
	}

	[Fact]
	public void Zoom_WithNonPositiveFactor_Throws()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromMinutes(10.0));

		var act = () => model.Zoom(factor: 0.0, anchor: model.From);

		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	private static TrendNavigationModel Model(bool isSticky, TimeSpan windowWidth)
	{
		var to = _origin;
		var from = _origin - windowWidth;

		return new TrendNavigationModel(from, to, _firstSample, isSticky);
	}
}
