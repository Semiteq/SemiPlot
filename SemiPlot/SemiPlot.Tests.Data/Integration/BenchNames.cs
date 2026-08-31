namespace SemiPlot.Tests.Data.Integration;

// Every name and credential the run's own container answers to, in one place: a role and the password
// the fixture gave it are read together, so neither can be changed without the other in view.
public static class BenchNames
{
	public const string ProvisionedDatabase = "semiplot_provisioned";

	public const string MaintenanceDatabase = "postgres";

	public const string SuperuserName = "postgres";

	public const string WriterRole = "scada_writer";

	public const string ReaderRole = "semiplot_reader";

	// The container is ephemeral and holds no secret, and a developer must not need environment
	// variables to run the suite, so the bench carries fixed dummy passwords of its own.
	public const string SuperuserPassword = "semibase-container-superuser";

	public const string WriterPassword = "semibase-container-writer";

	public const string ReaderPassword = "semibase-container-reader";

	// Passwords travel through the environment rather than through flags, so they never appear in a
	// process listing.
	public const string WriterPasswordVariable = "SEMIBASE_WRITER_PASSWORD";

	public const string ReaderPasswordVariable = "SEMIBASE_READER_PASSWORD";
}
