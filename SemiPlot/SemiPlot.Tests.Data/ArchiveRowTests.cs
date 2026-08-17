using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ArchiveRowTests
{
	private static readonly DateTime _base = new(2026, 1, 1, 13, 50, 44, DateTimeKind.Unspecified);

	[Fact]
	public void TheConstructorTruncatesTheTimestampToWholeMilliseconds()
	{
		var row = new ArchiveRow(1000, ArchiveRow.RawLayer, _base.AddTicks(19_999), 1.0, ArchiveRow.OrdinaryQuality);

		Assert.Equal(_base.AddMilliseconds(1), row.Timestamp);
	}

	[Fact]
	public void CopyingWithANewTimestampTruncatesItAsWell()
	{
		var row = new ArchiveRow(1000, ArchiveRow.RawLayer, _base, 1.0, ArchiveRow.OrdinaryQuality);

		var copy = row with { Timestamp = _base.AddTicks(19_999) };

		Assert.Equal(_base.AddMilliseconds(1), copy.Timestamp);
	}

	[Fact]
	public void TruncationKeepsTheTimestampNaive()
	{
		var row = new ArchiveRow(1000, ArchiveRow.RawLayer, _base.AddTicks(1), 1.0, ArchiveRow.OrdinaryQuality);

		Assert.Equal(DateTimeKind.Unspecified, row.Timestamp.Kind);
	}

	// Two rows a fraction of a millisecond apart collapse onto one key, which is what the archive's
	// primary key does after PostgreSQL rounds them.
	[Fact]
	public void RowsWithinTheSameMillisecondCompareEqual()
	{
		var first = new ArchiveRow(1000, ArchiveRow.RawLayer, _base.AddTicks(1), 1.0, ArchiveRow.OrdinaryQuality);
		var second = new ArchiveRow(1000, ArchiveRow.RawLayer, _base.AddTicks(9_999), 1.0, ArchiveRow.OrdinaryQuality);

		Assert.Equal(first, second);
	}
}
