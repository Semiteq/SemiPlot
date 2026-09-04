using AwesomeAssertions;

using SemiPlot.Core.Data;

using Xunit;

namespace SemiPlot.Tests.Unit.Data;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ArchiveExtentTests
{
	private static readonly DateTime _firstUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _lastUtc = new(2026, 1, 2, 12, 30, 0, DateTimeKind.Utc);

	[Fact]
	public void EmptyReportsItself()
	{
		ArchiveExtent.Empty.IsEmpty.Should().BeTrue();
	}

	[Fact]
	public void ConstructedExtentIsNotEmpty()
	{
		var extent = new ArchiveExtent(_firstUtc, _lastUtc);

		extent.IsEmpty.Should().BeFalse();
	}

	[Fact]
	public void TheTwoArgumentConstructorRoundTripsBothTimestamps()
	{
		var extent = new ArchiveExtent(_firstUtc, _lastUtc);

		extent.FirstUtc.Should().Be(_firstUtc);
		extent.LastUtc.Should().Be(_lastUtc);
	}

	[Fact]
	public void ADefaultValuedExtentIsEmpty()
	{
		var defaultValued = new ArchiveExtent(default, default);

		defaultValued.IsEmpty.Should().BeTrue();
		defaultValued.Should().Be(ArchiveExtent.Empty);
	}

	[Fact]
	public void TwoExtentsOverTheSameSpanAreEqual()
	{
		new ArchiveExtent(_firstUtc, _lastUtc).Should().Be(new ArchiveExtent(_firstUtc, _lastUtc));
	}

	[Fact]
	public void ACopyOfEmptyCarryingRealTimestampsIsNotEmpty()
	{
		var copied = ArchiveExtent.Empty with { FirstUtc = _firstUtc, LastUtc = _lastUtc };

		copied.IsEmpty.Should().BeFalse();
		copied.Should().Be(new ArchiveExtent(_firstUtc, _lastUtc));
	}

	[Fact]
	public void AnUnchangedCopyOfEmptyStaysEmpty()
	{
		var copied = ArchiveExtent.Empty with { };

		copied.IsEmpty.Should().BeTrue();
		copied.Should().Be(ArchiveExtent.Empty);
	}

	[Fact]
	public void EmptyRendersDistinctlyFromASpan()
	{
		var rendered = ArchiveExtent.Empty.ToString();

		rendered.Should().Contain("IsEmpty = true");
		rendered.Should().NotContain("0001");
		new ArchiveExtent(_firstUtc, _lastUtc).ToString().Should().NotContain("IsEmpty");
	}
}
