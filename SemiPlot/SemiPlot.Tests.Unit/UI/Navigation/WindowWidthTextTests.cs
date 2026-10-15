using AwesomeAssertions;

using SemiPlot.UI.Navigation;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Navigation;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class WindowWidthTextTests
{
	[Theory]
	[InlineData(0, 5, 0, "0:05:00")]
	[InlineData(168, 0, 0, "168:00:00")]
	[InlineData(8760, 0, 0, "8760:00:00")]
	[InlineData(0, 0, 1, "0:00:01")]
	[InlineData(1, 2, 3, "1:02:03")]
	public void Format_WritesTotalHoursThenMinutesAndSeconds(int hours, int minutes, int seconds, string expected)
	{
		WindowWidthText.Format(new TimeSpan(hours, minutes, seconds)).Should().Be(expected);
	}

	[Fact]
	public void Format_DropsTheSubSecondPart()
	{
		WindowWidthText.Format(TimeSpan.FromMilliseconds(5_900)).Should().Be("0:00:05");
	}

	[Theory]
	[InlineData("0:05:00", 0, 5, 0)]
	[InlineData("168:00:00", 168, 0, 0)]
	[InlineData("00:05:00", 0, 5, 0)]
	[InlineData("  0:05:00 ", 0, 5, 0)]
	[InlineData("8760:00:00", 8760, 0, 0)]
	public void TryParse_ReadsHoursMinutesAndSeconds(string text, int hours, int minutes, int seconds)
	{
		WindowWidthText.TryParse(text, out var width).Should().BeTrue();

		width.Should().Be(new TimeSpan(hours, minutes, seconds));
	}

	[Theory]
	[InlineData("0:60:00")]
	[InlineData("0:05:60")]
	[InlineData("abc")]
	[InlineData("")]
	[InlineData("5:00")]
	[InlineData("0:5:00")]
	[InlineData("-1:00:00")]
	[InlineData("0:05:00.5")]
	[InlineData("1 0:05:00")]
	[InlineData("99999999999999999999:00:00")]
	[InlineData("9999999999:00:00")]
	public void TryParse_RefusesAnythingElse(string text)
	{
		WindowWidthText.TryParse(text, out _).Should().BeFalse();
	}

	[Theory]
	[InlineData(0, 0, 1)]
	[InlineData(0, 5, 0)]
	[InlineData(1, 0, 0)]
	[InlineData(168, 0, 0)]
	[InlineData(8760, 59, 59)]
	public void Format_RoundTripsThroughTryParse(int hours, int minutes, int seconds)
	{
		var width = new TimeSpan(hours, minutes, seconds);

		WindowWidthText.TryParse(WindowWidthText.Format(width), out var parsed).Should().BeTrue();

		parsed.Should().Be(width);
	}
}
