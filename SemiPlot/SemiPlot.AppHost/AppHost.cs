var builder = DistributedApplication.CreateBuilder(args);

// The Aspire AppHost SDK does not add a project resource's assembly as a compile reference, so the
// role names and passwords cannot come from BenchRoles.cs (SemiPlot.Tools.ArchiveSeeder) directly;
// they are repeated here, as they were in the now-deleted scripts/bench-demo.ps1.
const string standDatabase = "semiplot_app";
const string maintenanceDatabase = "postgres";
const string provisionedDatabase = "semiplot_provisioned";
const string superuserName = "postgres";
const string superuserPassword = "semibase-container-superuser";
const string writerRole = "scada_writer";
const string writerPassword = "semibase-container-writer";
const string readerPassword = "semibase-container-reader";
const ushort hostPort = 55432;
const ushort containerPort = 5432;
// One density for the seeded day and the live tail, so the chart shows no seam between them.
const string changeSeconds = "0.5";

var configDirectory = Path.Combine(builder.AppHostDirectory, "..", "Artifacts", "bench-config");
var logFilePath = Path.Combine(configDirectory, "semiplot.log");

var bench = builder.AddDockerfile("bench", "../bench")
	.WithEnvironment("POSTGRES_PASSWORD", superuserPassword)
	.WithEnvironment("SEMIBASE_WRITER_PASSWORD", writerPassword)
	.WithEnvironment("SEMIBASE_READER_PASSWORD", readerPassword)
	.WithEnvironment("SEMIPLOT_PROVISIONED_DATABASE", provisionedDatabase)
	.WithEndpoint(port: hostPort, targetPort: containerPort, scheme: "tcp", name: "postgres", isProxied: false);

var writerConnection = $"Host=localhost;Port={hostPort};Database={standDatabase};"
	+ $"Username={writerRole};Password={writerPassword}";
var adminConnection = $"Host=localhost;Port={hostPort};Database={maintenanceDatabase};"
	+ $"Username={superuserName};Password={superuserPassword}";

var converge = builder.AddProject<Projects.SemiPlot_Tools_ArchiveSeeder>("converge")
	.WithArgs(
		"converge",
		"--connection", writerConnection,
		"--admin-connection", adminConnection,
		"--config-dir", configDirectory,
		"--change-seconds", changeSeconds)
	.WaitFor(bench);

builder.AddProject<Projects.SemiPlot_Tools_ArchiveSeeder>("writer")
	.WithArgs("--connection", writerConnection, "--follow", "1", "--change-seconds", changeSeconds)
	.WaitForCompletion(converge);

builder.AddProject<Projects.SemiPlot_UI>("viewer")
	.WithArgs("--config-dir", configDirectory, "--log-file", logFilePath)
	.WaitForCompletion(converge);

builder.Build().Run();
