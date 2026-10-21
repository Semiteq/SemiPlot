using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.DataSource.OpcUa;

using Xunit;

namespace SemiPlot.Tests.Unit.OpcUa;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class DeliveredStampsTests
{
	private static readonly DateTime _firstUtc = new(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _secondUtc = _firstUtc.AddSeconds(30);
	private static readonly TimeSpan _oneMillisecond = TimeSpan.FromMilliseconds(1);

	private readonly DeliveredStamps _stamps = new();

	[Fact]
	public void ALoss_BreaksEveryPenWithADeliveredValueOneMillisecondPastItsNewest()
	{
		_stamps.RecordValue(1, _firstUtc);
		_stamps.RecordValue(2, _secondUtc);

		var breaks = _stamps.LossBreaks([1, 2, 3]);

		breaks.Should().Equal(
			new Sample(1, _firstUtc + _oneMillisecond, null),
			new Sample(2, _secondUtc + _oneMillisecond, null));
	}

	[Fact]
	public void ASecondLossInARow_WritesNoSecondBreak()
	{
		_stamps.RecordValue(1, _firstUtc);
		_stamps.LossBreaks([1]);

		_stamps.LossBreaks([1]).Should().BeEmpty();
	}

	[Fact]
	public void AValueAfterABreak_ArmsTheNextLossBreak()
	{
		_stamps.RecordValue(1, _firstUtc);
		_stamps.LossBreaks([1]);
		_stamps.RecordValue(1, _secondUtc);

		_stamps.LossBreaks([1]).Should().Equal(new Sample(1, _secondUtc + _oneMillisecond, null));
	}

	[Fact]
	public void AnOlderValueDeliveredAgain_KeepsTheNewestStamp()
	{
		_stamps.RecordValue(1, _secondUtc);
		_stamps.RecordValue(1, _firstUtc);

		_stamps.LossBreaks([1]).Should().Equal(new Sample(1, _secondUtc + _oneMillisecond, null));
	}

	[Fact]
	public void TheNewestStamp_SurvivesANewSubscriptionOfTheSamePen()
	{
		_stamps.RecordValue(5, _secondUtc);

		var breaksOfTheNextSubscription = _stamps.LossBreaks([5, 6]);

		breaksOfTheNextSubscription.Should().Equal(new Sample(5, _secondUtc + _oneMillisecond, null));
	}

	[Fact]
	public void ABadBreak_IsClampedOneMillisecondPastTheNewest()
	{
		_stamps.RecordValue(1, _secondUtc);

		_stamps.BadBreak(1, _firstUtc).Should().Be(new Sample(1, _secondUtc + _oneMillisecond, null));
	}

	[Fact]
	public void ABadBreakPastTheNewest_KeepsItsOwnStamp()
	{
		_stamps.RecordValue(1, _firstUtc);

		_stamps.BadBreak(1, _secondUtc).Should().Be(new Sample(1, _secondUtc, null));
	}

	[Fact]
	public void ABadWithNoStamp_BreaksOneMillisecondPastTheNewest()
	{
		_stamps.RecordValue(1, _firstUtc);

		_stamps.BadBreak(1, null).Should().Be(new Sample(1, _firstUtc + _oneMillisecond, null));
	}

	[Fact]
	public void ABadWithNeitherStampNorDeliveredValue_WritesNothing()
	{
		_stamps.BadBreak(1, null).Should().BeNull();
	}

	[Fact]
	public void ABadOnAPenWithoutADeliveredValue_BreaksAtItsStamp()
	{
		_stamps.BadBreak(1, _firstUtc).Should().Be(new Sample(1, _firstUtc, null));
	}

	[Fact]
	public void ASecondBadOnAPenWithoutADeliveredValue_WritesNoSecondBreak()
	{
		_stamps.BadBreak(1, _firstUtc);

		_stamps.BadBreak(1, _secondUtc).Should().BeNull();
		_stamps.LossBreaks([1]).Should().BeEmpty();
	}

	[Fact]
	public void AValueAfterABadBreak_ArmsTheNextBadBreak()
	{
		_stamps.RecordValue(1, _firstUtc);
		_stamps.BadBreak(1, null);
		_stamps.RecordValue(1, _secondUtc);

		_stamps.BadBreak(1, null).Should().Be(new Sample(1, _secondUtc + _oneMillisecond, null));
	}

	[Fact]
	public void AValueAfterABadOnAPenWithoutADeliveredValue_ArmsTheNextLossBreak()
	{
		_stamps.BadBreak(1, _firstUtc);
		_stamps.RecordValue(1, _secondUtc);

		_stamps.LossBreaks([1]).Should().Equal(new Sample(1, _secondUtc + _oneMillisecond, null));
	}

	[Fact]
	public void ALossAfterABadBreak_WritesNoSecondBreak()
	{
		_stamps.RecordValue(1, _firstUtc);
		_stamps.BadBreak(1, _secondUtc);

		_stamps.LossBreaks([1]).Should().BeEmpty();
		_stamps.BadBreak(1, _secondUtc.AddSeconds(1)).Should().BeNull();
	}
}
