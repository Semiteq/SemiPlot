using System.Globalization;

using DotNet.Testcontainers.Builders;

using SemiPlot.Tools.ArchiveSeeder;

using Testcontainers.PostgreSql;

using Xunit;

namespace SemiPlot.Tests.Integration;

// One server per test run; the database is the isolation unit, not the container.
// docs/architecture/bench.md#the-test-bench
public sealed class PostgresContainerFixture : IAsyncLifetime
{
	private const ushort PostgresPort = 5432;

	// The build context is copied to the output directory: a test assembly runs from there with no path
	// back to the repository.
	private const string BenchContextDirectory = "bench";

	private const string BaseImageArgument = "BASE_IMAGE";

	private const string ProvisionedDatabaseVariable = "SEMIPLOT_PROVISIONED_DATABASE";

	// SemiBase installs vanilla PostgreSQL 17 on a site, so the bench runs the same major version.
	private const string BaseImage = "postgres:17-alpine";

	private const string BenchImage = "semiplot-bench:test";

	// The wait strategy's psql runs as an exec, which inherits the container environment and nothing
	// else, and a TCP login to the base image is password-authenticated.
	private const string PasswordVariable = "PGPASSWORD";

	// Bounds the build, the start and the readiness wait; the pull carries the same span under its own
	// token. A bench that never comes up fails the collection rather than hanging the executable.
	private static readonly TimeSpan _startupBound = TimeSpan.FromMinutes(2);

	private PostgreSqlContainer? _postgreSqlContainer;

	private PostgresServer? _postgresServer;

	public PostgresServer Server =>
		_postgresServer ?? throw new InvalidOperationException("The fixture was used before it was initialised.");

	public Task<ArchiveDatabase> CloneTemplateAsync(CancellationToken cancellationToken = default)
	{
		return ArchiveDatabase.CloneAsync(Server, ArchiveTemplate.Name, cancellationToken);
	}

	// A database carrying the roles, the grants and an empty public.trends, and no seeded row.
	public Task<ArchiveDatabase> CloneProvisionedAsync(CancellationToken cancellationToken = default)
	{
		return ArchiveDatabase.CloneAsync(Server, BenchRoles.ProvisionedDatabase, cancellationToken);
	}

	public async ValueTask InitializeAsync()
	{
		_postgresServer = await StartContainerAsync();

		await ArchiveTemplate.BuildAsync(_postgresServer);
	}

	public async ValueTask DisposeAsync()
	{
		if (_postgreSqlContainer is not null)
		{
			await _postgreSqlContainer.DisposeAsync();
		}
	}

	private async Task<PostgresServer> StartContainerAsync()
	{
		using var startup = new CancellationTokenSource(_startupBound);

		// Ahead of the build, so the tag the Dockerfile's FROM names is the newest one the registry serves;
		// `docs/architecture/bench.md` states why the tag moves on purpose.
		await DockerCli.PullProvisionerAsync(_startupBound);

		var image = new ImageFromDockerfileBuilder()
			.WithName(BenchImage)
			.WithDockerfileDirectory(Path.Combine(AppContext.BaseDirectory, BenchContextDirectory))
			.WithBuildArgument(BaseImageArgument, BaseImage)
			.Build();

		await image.CreateAsync(startup.Token);

		var container = new PostgreSqlBuilder(image)
			.WithUsername(BenchRoles.SuperuserName)
			.WithPassword(BenchRoles.SuperuserPassword)
			.WithDatabase(BenchRoles.MaintenanceDatabase)
			.WithEnvironment(BenchRoles.WriterPasswordVariable, BenchRoles.WriterPassword)
			.WithEnvironment(BenchRoles.ReaderPasswordVariable, BenchRoles.ReaderPassword)
			.WithEnvironment(PasswordVariable, BenchRoles.SuperuserPassword)
			.WithEnvironment(ProvisionedDatabaseVariable, BenchRoles.ProvisionedDatabase)
			// Replaces the module's own pg_isready wait, which passes on the entrypoint's temporary
			// server, before `semibase bench` has provisioned anything.
			.WithWaitStrategy(
				Wait.ForUnixContainer().UntilCommandIsCompleted(
					ProvisionedWaitCommand(),
					options => options.WithTimeout(_startupBound)))
			.Build();

		// Before the start, not after it: a container that comes up and never becomes ready fails the wait
		// strategy, and an assignment past that point would leave DisposeAsync with nothing to dispose.
		_postgreSqlContainer = container;

		await container.StartAsync(startup.Token);

		return new PostgresServer(container.Hostname, container.GetMappedPublicPort(PostgresPort));
	}

	// The query goes over TCP, to exclude the entrypoint's temporary unix-socket server.
	private static string[] ProvisionedWaitCommand()
	{
		return
		[
			"psql",
			"--host",
			"localhost",
			"--port",
			PostgresPort.ToString(CultureInfo.InvariantCulture),
			"--username",
			BenchRoles.SuperuserName,
			"--dbname",
			BenchRoles.ProvisionedDatabase,
			"--tuples-only",
			"--no-align",
			"--command",
			"SELECT count(*) FROM public.trends;"
		];
	}
}
