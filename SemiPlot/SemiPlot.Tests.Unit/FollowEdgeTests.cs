using AwesomeAssertions;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Unit;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class FollowEdgeTests
{
	private static readonly DateTime _seed = new(2026, 1, 2, 8, 0, 0, DateTimeKind.Unspecified);

	private static readonly TimeSpan _lag = TimeSpan.FromSeconds(60);

	[Fact]
	public void AnEndPastTheEdgeReturnsTheWindowFromTheEdge()
	{
		var edge = new FollowEdge(_seed);

		var window = edge.Advance(_seed.AddSeconds(1));

		window.Should().Be((_seed, _seed.AddSeconds(1)));
	}

	[Theory]
	[InlineData(0.0)]
	[InlineData(-5.0)]
	public void AnEndAtOrBehindTheEdgeReturnsNothingAndKeepsTheEdge(double secondsFromEdge)
	{
		var edge = new FollowEdge(_seed);

		var window = edge.Advance(_seed.AddSeconds(secondsFromEdge));

		window.Should().BeNull();
		edge.Advance(_seed.AddSeconds(2)).Should().Be((_seed, _seed.AddSeconds(2)));
	}

	// A one-second tick over a lagged clock that steps back twice, as a clock correction moves it.
	[Fact]
	public void ALaggedClockYieldsContiguousWindowsCoveringTheWholeSpan()
	{
		var wallClock = _seed + _lag;
		double[] tickSeconds = [1.0, 1.0, -1.5, 2.0, 0.0, 1.0, -0.2, 3.0];
		var edge = new FollowEdge(_seed);
		var windows = new List<(DateTime After, DateTime To)>();
		var nullCount = 0;

		foreach (var seconds in tickSeconds)
		{
			wallClock = wallClock.AddSeconds(seconds);

			if (edge.Advance(wallClock - _lag) is { } window)
			{
				windows.Add(window);
			}
			else
			{
				nullCount++;
			}
		}

		nullCount.Should().BeGreaterThan(0);
		windows.Should().AllSatisfy(window => window.To.Should().BeAfter(window.After));
		windows[0].After.Should().Be(_seed);
		windows.Zip(windows.Skip(1)).Should().AllSatisfy(pair => pair.Second.After.Should().Be(pair.First.To));
		windows[^1].To.Should().Be(wallClock - _lag);
	}

	[Fact]
	public void AFinalAdvanceAtTheUnlaggedClockCoversTheHeldBackSpan()
	{
		var wallClock = _seed + _lag;
		var edge = new FollowEdge(_seed);

		for (var tick = 0; tick < 5; tick++)
		{
			wallClock = wallClock.AddSeconds(1);
			edge.Advance(wallClock - _lag);
		}

		var heldBack = edge.Advance(wallClock);

		heldBack.Should().Be((wallClock - _lag, wallClock));
	}
}
