namespace SemiPlot.Tools.ArchiveSeeder;

// docs/architecture/bench.md#the-converge-verb
public sealed record ConvergeOptions(
	string ConnectionString,
	string AdminConnectionString,
	string ConfigDirectory,
	DateTime? End,
	double ChangeSeconds,
	int OpcPort);
