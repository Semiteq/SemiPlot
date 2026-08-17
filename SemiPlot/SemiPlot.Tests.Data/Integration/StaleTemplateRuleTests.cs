using System.Globalization;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The stamp rule the destructive sweep hangs on, tested without a server.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class StaleTemplateRuleTests
{
	private const long Now = 1_800_000_000L;

	private static readonly long _window = (long)ArchiveTemplate.StaleAfter.TotalSeconds;

	[Fact]
	public void ATemplateStampedInsideTheWindowIsInUse()
	{
		Assert.False(ArchiveTemplate.IsStale(Stamp(Now), Now));
		Assert.False(ArchiveTemplate.IsStale(Stamp(Now - _window + 1), Now));
		Assert.False(ArchiveTemplate.IsStale(Stamp(Now - _window), Now));
	}

	[Fact]
	public void ATemplateStampedBeforeTheWindowIsStale()
	{
		Assert.True(ArchiveTemplate.IsStale(Stamp(Now - _window - 1), Now));
		Assert.True(ArchiveTemplate.IsStale(Stamp(Now - (10L * _window)), Now));
	}

	// The sweep destroys, so a stamp it cannot read dates nothing and the database stays.
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("some other tool's comment")]
	[InlineData(ArchiveTemplate.MarkerPrefix)]
	[InlineData(ArchiveTemplate.MarkerPrefix + "yesterday")]
	[InlineData(ArchiveTemplate.MarkerPrefix + "99999999999999999999")]
	public void AnUnreadableStampIsNotAnOldOne(string? marker)
	{
		Assert.False(ArchiveTemplate.IsStale(marker, Now));
	}

	// A stamp from a clock running ahead of the sweep's is not stale either.
	[Fact]
	public void AStampFromTheFutureIsNotStale()
	{
		Assert.False(ArchiveTemplate.IsStale(Stamp(Now + _window), Now));
	}

	private static string Stamp(long epochSeconds)
	{
		return ArchiveTemplate.MarkerPrefix + epochSeconds.ToString(CultureInfo.InvariantCulture);
	}
}
