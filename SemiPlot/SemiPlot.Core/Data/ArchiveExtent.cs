namespace SemiPlot.Core.Data;

public sealed record ArchiveExtent(DateTime FirstUtc, DateTime LastUtc)
{
	// The configured variables span no time: either no rows, or no configured variables at all.
	public static ArchiveExtent Empty { get; } = new(default, default);

	// Derived from the bounds rather than carried in a field: a record's copy constructor runs before `with`
	// applies its initializers and cannot know what the result will hold, so any stored flag is wrong for
	// one of the two directions.
	public bool IsEmpty => FirstUtc == default && LastUtc == default;
}
