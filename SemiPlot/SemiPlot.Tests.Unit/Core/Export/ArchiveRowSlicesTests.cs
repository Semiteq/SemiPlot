using AwesomeAssertions;

using SemiPlot.Core.Export;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Export;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ArchiveRowSlicesTests
{
	private static readonly DateTime _from = new(2026, 10, 13, 8, 0, 0);

	[Fact]
	public void AOneHourRange_IsFourSlicesOfTheWidth()
	{
		var slices = ArchiveRowSlices.Tile(_from, _from.AddHours(1)).ToList();

		slices.Should().Equal(
			(_from, _from.AddMinutes(15)),
			(_from.AddMinutes(15), _from.AddMinutes(30)),
			(_from.AddMinutes(30), _from.AddMinutes(45)),
			(_from.AddMinutes(45), _from.AddHours(1)));
	}

	[Fact]
	public void ATwentyMinuteRange_EndsItsSecondSliceAtTheEnd()
	{
		var slices = ArchiveRowSlices.Tile(_from, _from.AddMinutes(20)).ToList();

		slices.Should().Equal(
			(_from, _from.AddMinutes(15)),
			(_from.AddMinutes(15), _from.AddMinutes(20)));
	}

	[Fact]
	public void ARangeShorterThanASlice_IsOneSlice()
	{
		ArchiveRowSlices.Tile(_from, _from.AddSeconds(1)).Should().Equal((_from, _from.AddSeconds(1)));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void ARangeThatIsEmptyOrReversed_IsNoSlice(int seconds)
	{
		ArchiveRowSlices.Tile(_from, _from.AddSeconds(seconds)).Should().BeEmpty();
	}

	[Fact]
	public void TheSlicesOfADay_TileItWithNoGapOrOverlap()
	{
		var to = _from.AddHours(24);

		var slices = ArchiveRowSlices.Tile(_from, to).ToList();

		slices.Should().HaveCount(96);
		slices[0].From.Should().Be(_from);
		slices[^1].To.Should().Be(to);
		slices.Should().OnlyContain(slice => slice.To - slice.From <= ArchiveRowSlices.Width);
		slices.Zip(slices.Skip(1)).Should().OnlyContain(pair => pair.First.To == pair.Second.From);
	}
}
