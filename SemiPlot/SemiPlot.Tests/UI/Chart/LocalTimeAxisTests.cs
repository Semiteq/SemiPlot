using AwesomeAssertions;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class LocalTimeAxisTests
{
	[Fact]
	public void ToAxis_ConvertsUtcToTheLocalTimeOaDate()
	{
		var utc = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

		var axisValue = LocalTimeAxis.ToAxis(utc);

		axisValue.Should().Be(utc.ToLocalTime().ToOADate());
	}

	[Fact]
	public void FromAxis_ReturnsUtcThatRoundTripsThroughToAxis()
	{
		var utc = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

		var roundTripped = LocalTimeAxis.FromAxis(LocalTimeAxis.ToAxis(utc));

		roundTripped.Kind.Should().Be(DateTimeKind.Utc);
		roundTripped.Should().BeCloseTo(utc, TimeSpan.FromMilliseconds(1.0));
	}

	[Fact]
	public void ToAxis_TreatsUnspecifiedKindInputAsUtc()
	{
		var utc = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
		var unspecified = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);

		LocalTimeAxis.ToAxis(unspecified).Should().Be(LocalTimeAxis.ToAxis(utc));
	}
}
