namespace SemiPlot.Tools.ArchiveSeeder;

public sealed record SeederOptions(
	string ConnectionString,
	DateTime End,
	int Days,
	int PenCount,
	long Seed,
	double ChangeSeconds,
	int BreakCount,
	string? AdminConnectionString)
{
	public const int DefaultDays = 1;
	public const int DefaultPenCount = 8;
	public const long DefaultSeed = 1;
	public const double DefaultChangeSeconds = 5.0;
	public const int DefaultBreakCount = 4;

	public DateTime Start => End - TimeSpan.FromDays(Days);
}
