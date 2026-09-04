namespace SemiPlot.Tools.ArchiveSeeder;

// The demo writer's options. A follow run has no span: it appends to an archive somebody else seeded.
public sealed record FollowOptions(
	string ConnectionString,
	TimeSpan Interval,
	int PenCount,
	long Seed,
	double ChangeSeconds)
{
	// The ceiling on both --follow and --change-seconds: an interval above one day emits nothing and far
	// above it overflows the tick arithmetic.
	public const double MaximumSeconds = 86400.0;
}
