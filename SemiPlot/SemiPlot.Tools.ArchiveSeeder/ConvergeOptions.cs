namespace SemiPlot.Tools.ArchiveSeeder;

// Bench only: recreates the database ConnectionString names from BenchRoles.ProvisionedDatabase, seeds
// it up to End or now at ChangeSeconds, fills the tag catalogue and writes the connection file into
// ConfigDirectory.
public sealed record ConvergeOptions(
	string ConnectionString,
	string AdminConnectionString,
	string ConfigDirectory,
	DateTime? End,
	double ChangeSeconds,
	int Days = SeederOptions.DefaultDays,
	int PenCount = SeederOptions.DefaultPenCount,
	ArchiveSchedule Schedule = ArchiveSchedule.Continuous)
{
	/// <summary>The seeding run converge performs on the recreated database, ending at End or at localNow.</summary>
	public SeederOptions ToSeederOptions(string standAdminConnectionString, DateTime localNow)
	{
		return new SeederOptions(
			ConnectionString,
			DateTime.SpecifyKind(End ?? localNow, DateTimeKind.Unspecified),
			Days,
			PenCount,
			SeederOptions.DefaultSeed,
			ChangeSeconds,
			SeederOptions.DefaultBreakCount,
			standAdminConnectionString,
			Schedule);
	}
}
