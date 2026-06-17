using AwesomeAssertions;

using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Core.Trends;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class MinimapGeometryTests
{
	private static readonly DateTime _first = new(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _last = new(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void WindowFraction_MidWindow_MapsToCenteredRectangle()
	{
		var from = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc);
		var to = new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc);

		var (start, width) = MinimapGeometry.WindowFraction(_first, _last, from, to);

		start.Should().BeApproximately(0.2, 1e-9);
		width.Should().BeApproximately(0.4, 1e-9);
	}

	[Fact]
	public void WindowFraction_WindowEqualsExtent_FillsStrip()
	{
		var (start, width) = MinimapGeometry.WindowFraction(_first, _last, _first, _last);

		start.Should().Be(0.0);
		width.Should().BeApproximately(1.0, 1e-9);
	}

	[Fact]
	public void WindowFraction_WindowReachesBeyondExtent_ClampsToStrip()
	{
		var from = _first - TimeSpan.FromDays(5.0);
		var to = _last + TimeSpan.FromDays(5.0);

		var (start, width) = MinimapGeometry.WindowFraction(_first, _last, from, to);

		start.Should().Be(0.0);
		width.Should().Be(1.0);
	}

	[Fact]
	public void WindowFraction_ZeroSpanExtent_ReturnsFullStrip()
	{
		var (start, width) = MinimapGeometry.WindowFraction(_first, _first, _first, _first);

		start.Should().Be(0.0);
		width.Should().Be(1.0);
	}

	[Fact]
	public void TimeAtFraction_Half_ReturnsExtentMidpoint()
	{
		var time = MinimapGeometry.TimeAtFraction(_first, _last, 0.5);

		time.Should().Be(new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc));
	}

	[Theory]
	[InlineData(-0.5)]
	[InlineData(1.5)]
	public void TimeAtFraction_OutOfRange_ClampsToExtentEdge(double fraction)
	{
		var time = MinimapGeometry.TimeAtFraction(_first, _last, fraction);

		time.Should().BeOnOrAfter(_first);
		time.Should().BeOnOrBefore(_last);
	}
}
