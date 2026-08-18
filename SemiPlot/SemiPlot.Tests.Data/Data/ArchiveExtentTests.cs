using SemiPlot.Core.Data;

using Xunit;

namespace SemiPlot.Tests.Data.Data;

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
		Assert.True(ArchiveExtent.Empty.IsEmpty);
	}

	[Fact]
	public void ConstructedExtentIsNotEmpty()
	{
		var extent = new ArchiveExtent(_firstUtc, _lastUtc);

		Assert.False(extent.IsEmpty);
	}

	[Fact]
	public void TheTwoArgumentConstructorRoundTripsBothTimestamps()
	{
		var extent = new ArchiveExtent(_firstUtc, _lastUtc);

		Assert.Equal(_firstUtc, extent.FirstUtc);
		Assert.Equal(_lastUtc, extent.LastUtc);
	}

	[Fact]
	public void ADefaultValuedExtentIsEmpty()
	{
		var defaultValued = new ArchiveExtent(default, default);

		Assert.True(defaultValued.IsEmpty);
		Assert.Equal(ArchiveExtent.Empty, defaultValued);
	}

	[Fact]
	public void TwoExtentsOverTheSameSpanAreEqual()
	{
		Assert.Equal(new ArchiveExtent(_firstUtc, _lastUtc), new ArchiveExtent(_firstUtc, _lastUtc));
	}

	[Fact]
	public void ACopyOfEmptyCarryingRealTimestampsIsNotEmpty()
	{
		var copied = ArchiveExtent.Empty with { FirstUtc = _firstUtc, LastUtc = _lastUtc };

		Assert.False(copied.IsEmpty);
		Assert.Equal(new ArchiveExtent(_firstUtc, _lastUtc), copied);
	}

	[Fact]
	public void AnUnchangedCopyOfEmptyStaysEmpty()
	{
		var copied = ArchiveExtent.Empty with { };

		Assert.True(copied.IsEmpty);
		Assert.Equal(ArchiveExtent.Empty, copied);
	}

	[Fact]
	public void EmptyRendersDistinctlyFromASpan()
	{
		var rendered = ArchiveExtent.Empty.ToString();

		Assert.Contains("IsEmpty = true", rendered, StringComparison.Ordinal);
		Assert.DoesNotContain("0001", rendered, StringComparison.Ordinal);
		Assert.DoesNotContain("IsEmpty", new ArchiveExtent(_firstUtc, _lastUtc).ToString(), StringComparison.Ordinal);
	}
}
