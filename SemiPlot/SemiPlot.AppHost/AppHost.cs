var builder = DistributedApplication.CreateBuilder(args);

// The Aspire AppHost SDK does not add a project resource's assembly as a compile reference, so the
// role names and passwords cannot come from BenchRoles.cs (SemiPlot.Tools.ArchiveSeeder) directly;
// they are repeated here, as they were in the now-deleted scripts/bench-demo.ps1.
const string StandDatabase = "semiplot_app";
const string MaintenanceDatabase = "postgres";
const string ProvisionedDatabase = "semiplot_provisioned";
const string SuperuserName = "postgres";
const string SuperuserPassword = "semibase-container-superuser";
const string WriterRole = "scada_writer";
const string WriterPassword = "semibase-container-writer";
const string ReaderPassword = "semibase-container-reader";
const ushort HostPort = 55432;
const ushort ContainerPort = 5432;
// One density for the seeded day and the live tail, so the chart shows no seam between them.
const string ChangeSeconds = "0.5";

var configDirectory = Path.Combine(builder.AppHostDirectory, "..", "Artifacts", "bench-config");
var logFilePath = Path.Combine(configDirectory, "semiplot.log");

var bench = builder.AddDockerfile("bench", "../bench")
	.WithEnvironment("POSTGRES_PASSWORD", SuperuserPassword)
	.WithEnvironment("SEMIBASE_WRITER_PASSWORD", WriterPassword)
	.WithEnvironment("SEMIBASE_READER_PASSWORD", ReaderPassword)
	.WithEnvironment("SEMIPLOT_PROVISIONED_DATABASE", ProvisionedDatabase)
	.WithEndpoint(port: HostPort, targetPort: ContainerPort, scheme: "tcp", name: "postgres", isProxied: false);

var writerConnection = $"Host=localhost;Port={HostPort};Database={StandDatabase};"
	+ $"Username={WriterRole};Password={WriterPassword}";
var adminConnection = $"Host=localhost;Port={HostPort};Database={MaintenanceDatabase};"
	+ $"Username={SuperuserName};Password={SuperuserPassword}";

var converge = builder.AddProject<Projects.SemiPlot_Tools_ArchiveSeeder>("converge")
	.WithArgs(
		"converge",
		"--connection", writerConnection,
		"--admin-connection", adminConnection,
		"--config-dir", configDirectory,
		"--change-seconds", ChangeSeconds)
	.WaitFor(bench);

builder.AddProject<Projects.SemiPlot_Tools_ArchiveSeeder>("writer")
	.WithArgs("--connection", writerConnection, "--follow", "1", "--change-seconds", ChangeSeconds)
	.WaitForCompletion(converge);

builder.AddProject<Projects.SemiPlot_UI>("viewer")
	.WithArgs("--config-dir", configDirectory, "--log-file", logFilePath)
	.WaitForCompletion(converge);

builder.Build().Run();
