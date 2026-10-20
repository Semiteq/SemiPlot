namespace SemiPlot.Tools.ArchiveSeeder;

// The demo writer's options. A follow run has no span: it appends to an archive somebody else seeded.
public sealed record FollowOptions(
	string ConnectionString,
	TimeSpan Interval,
	int PenCount,
	long Seed,
	double ChangeSeconds,
	TimeSpan ArchiveLag,
	int OpcPort)
{
	// The ceiling on --follow, --change-seconds and --archive-lag: an interval above one day emits nothing
	// and far above it overflows the tick arithmetic.
	public const double MaximumSeconds = 86400.0;

	/// <summary>The port a running Simple-Scada holds by default.</summary>
	internal const int SimpleScadaPort = 49875;
}
