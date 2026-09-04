namespace SemiPlot.Tools.ArchiveSeeder;

// Every name and credential the bench container answers to, in one place: a role and the password the
// container gave it are read together, so neither can be changed without the other in view. The
// container is ephemeral and holds no secret, so the passwords are fixed dummies rather than a setting.
public static class BenchRoles
{
	public const string ProvisionedDatabase = "semiplot_provisioned";

	public const string MaintenanceDatabase = "postgres";

	public const string SuperuserName = "postgres";

	public const string WriterRole = "scada_writer";

	public const string ReaderRole = "semiplot_reader";

	public const string SuperuserPassword = "semibase-container-superuser";

	public const string WriterPassword = "semibase-container-writer";

	public const string ReaderPassword = "semibase-container-reader";

	// Passwords travel through the environment rather than through flags, so they never appear in a
	// process listing.
	public const string WriterPasswordVariable = "SEMIBASE_WRITER_PASSWORD";

	public const string ReaderPasswordVariable = "SEMIBASE_READER_PASSWORD";
}
