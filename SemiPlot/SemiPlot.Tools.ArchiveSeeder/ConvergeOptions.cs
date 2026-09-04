namespace SemiPlot.Tools.ArchiveSeeder;

// Bench only: recreates the database ConnectionString names from BenchRoles.ProvisionedDatabase, seeds
// it up to End or now at ChangeSeconds, fills the tag catalogue and writes the connection file into
// ConfigDirectory.
public sealed record ConvergeOptions(
	string ConnectionString,
	string AdminConnectionString,
	string ConfigDirectory,
	DateTime? End,
	double ChangeSeconds);
