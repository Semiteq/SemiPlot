namespace SemiPlot.Tools.ArchiveSeeder;

// The demo writer's options. A follow run has no span: it appends to an archive somebody else seeded.
public sealed record FollowOptions(
	string ConnectionString,
	TimeSpan Interval,
	int PenCount,
	long Seed,
	double ChangeSeconds)
{
	// A follow run states no span of its own, so the ceiling a seeding run takes from its span is a
	// literal here. A change interval longer than a day emits nothing anyway, and a value far above it
	// overflows the tick arithmetic behind the generator.
	public const double MaximumSeconds = 86400.0;
}
