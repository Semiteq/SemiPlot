using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using Testcontainers.PostgreSql;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// One server per test run. A container start costs seconds while a CREATE DATABASE costs under a
// second, so the container is the wrong isolation unit and the database is the right one.
//
// The container provisions itself: the image built from bench/ carries the provisioner and runs it
// from the entrypoint's init hook, so nothing is resolved from the machine running the suite.
//
// Initialisation never throws. The runtimes it needs are optional on a developer machine, so their
// absence is captured as a reason and handed to DatabaseGate, which skips or fails according to
// SEMIPLOT_REQUIRE_DB.
public sealed class PostgresContainerFixture : IAsyncLifetime
{
	// Scopes the built image to this repository, so a count of dangling bench images does not also count
	// the moving semibase:latest tag this bench tracks on purpose.
	public const string BenchLabel = "semiplot.bench";

	public const string BenchLabelValue = "1";

	private const ushort PostgresPort = 5432;

	// The build context is copied to the output directory: a test assembly runs from there with no path
	// back to the repository.
	private const string BenchContextDirectory = "bench";

	private const string BaseImageArgument = "BASE_IMAGE";

	private const string ProvisionerImageArgument = "PROVISIONER_IMAGE";

	private const string ProvisionedDatabaseVariable = "SEMIPLOT_PROVISIONED_DATABASE";

	private const string BenchImageRepository = "semiplot-bench";

	// The wait strategy's psql runs as an exec, which inherits the container environment and nothing
	// else, and a TCP login to the base image is password-authenticated.
	private const string PasswordVariable = "PGPASSWORD";

	// One bound for the registry pull and for readiness, so a broken bench is a stated reason inside two
	// minutes on every path instead of an await that hangs the test executable and locks the next build.
	private static readonly TimeSpan _startupBound = TimeSpan.FromMinutes(2);

	// CREATE DATABASE ... TEMPLATE fails while another session holds the source, so a clone must never
	// overlap another clone or a drop. Never disposed: disposing it here would race the clone disposals
	// xunit runs while tearing the collection down.
	private readonly SemaphoreSlim _creationGate = new(1, 1);

	private PostgreSqlContainer? _postgreSqlContainer;

	private PostgresServer? _postgresServer;

	private ProvisionerResolution? _provisionerResolution;

	public string? UnavailableReason { get; private set; }

	public bool IsAvailable => UnavailableReason is null;

	// The provisioner this run's databases came from, so a `latest` that fails tomorrow on an unchanged
	// commit can be told apart from a change in this repository.
	internal ProvisionerResolution Provisioner =>
		_provisionerResolution ?? throw new InvalidOperationException(
			UnavailableReason ?? "The fixture was used before it was initialised.");

	public PostgresServer Server =>
		_postgresServer ?? throw new InvalidOperationException(
			UnavailableReason ?? "The fixture was used before it was initialised.");

	public Task<ArchiveDatabase> CloneTemplateAsync(CancellationToken cancellationToken = default)
	{
		return ArchiveDatabase.CloneAsync(Server, _creationGate, ArchiveTemplate.Name, cancellationToken);
	}

	// A database carrying the roles, the grants and an empty public.trends, and no seeded row. The clone
	// is how a test reaches that state: provisioning it a second time would spawn a binary the container
	// path deliberately does not have.
	public Task<ArchiveDatabase> CloneProvisionedAsync(CancellationToken cancellationToken = default)
	{
		return ArchiveDatabase.CloneAsync(
			Server,
			_creationGate,
			BenchNames.ProvisionedDatabase,
			cancellationToken);
	}

	public void RequireAvailable()
	{
		DatabaseGate.Require(UnavailableReason, TestEnvironment.DatabaseRequired);
	}

	// OperationCanceledException is the one exclusion, because a cancelled run is the caller's outcome
	// and not an unavailable runtime.
	public async ValueTask InitializeAsync()
	{
		try
		{
			_postgresServer = await StartContainerAsync();

			await ArchiveTemplate.BuildAsync(_postgresServer, _creationGate);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_postgresServer = null;

			UnavailableReason =
				$"no container runtime started a bench image over {TestEnvironment.Image}: {exception.Message}";
		}
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
		// Ahead of the build, and the build takes the digest it resolved rather than the tag:
		// `docs/architecture/bench.md` states why.
		var provisioner = await ProvisionerImage.ResolveAsync(_startupBound);

		if (provisioner.IsFailed)
		{
			throw new InvalidOperationException(provisioner.Errors[0].Message);
		}

		WarnIfStale(provisioner.Value);

		var image = new ImageFromDockerfileBuilder()
			.WithName(BenchImageFor(TestEnvironment.Image))
			.WithDockerfileDirectory(Path.Combine(AppContext.BaseDirectory, BenchContextDirectory))
			.WithBuildArgument(BaseImageArgument, TestEnvironment.Image)
			.WithBuildArgument(ProvisionerImageArgument, provisioner.Value.Digest)
			.WithLabel(BenchLabel, BenchLabelValue)
			.WithDeleteIfExists(false)
			.WithCleanUp(true)
			.Build();

		await image.CreateAsync();

		var container = new PostgreSqlBuilder(image)
			.WithUsername(BenchNames.SuperuserName)
			.WithPassword(BenchNames.SuperuserPassword)
			.WithDatabase(BenchNames.MaintenanceDatabase)
			.WithEnvironment(BenchNames.WriterPasswordVariable, BenchNames.WriterPassword)
			.WithEnvironment(BenchNames.ReaderPasswordVariable, BenchNames.ReaderPassword)
			.WithEnvironment(PasswordVariable, BenchNames.SuperuserPassword)
			.WithEnvironment(ProvisionedDatabaseVariable, BenchNames.ProvisionedDatabase)
			.WithWaitStrategy(
				Wait.ForUnixContainer().UntilCommandIsCompleted(
					ProvisionedWaitCommand(),
					options => options.WithTimeout(_startupBound)))
			.Build();

		// Before the start, not after it: a container that comes up and never becomes ready fails the wait
		// strategy, and an assignment past that point would leave DisposeAsync with nothing to dispose.
		_postgreSqlContainer = container;

		await container.StartAsync();

		_provisionerResolution = await DescribeProvisionerAsync(container, provisioner.Value);

		return new PostgresServer(container.Hostname, container.GetMappedPublicPort(PostgresPort));
	}

	// Asked of the container that actually provisioned this run's databases, rather than of the registry:
	// the binary it carries is the one that ran. The digest identifies the same image and is already in
	// hand, so an executable that declines to report a version costs the run nothing.
	private static async Task<ProvisionerResolution> DescribeProvisionerAsync(
		IContainer container,
		ProvisionerResolution resolution)
	{
		try
		{
			var reported = await container.ExecAsync(
				[ProvisionerImage.ExecutablePath, ProvisionerImage.VersionArgument]);

			if (reported.ExitCode == 0 && reported.Stdout.Trim() is { Length: > 0 } version)
			{
				return resolution with { Version = version };
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			// The digest already names the image; the version string is only its legible form.
		}

		return resolution;
	}

	// Standard error rather than the test output: a stale provisioner is not a failure, and a passing
	// test's output is what a console logger drops. The process's own stderr reaches the CI log
	// whatever the outcome of every test is.
	private static void WarnIfStale(ProvisionerResolution resolution)
	{
		if (resolution.StalenessReason is null)
		{
			return;
		}

		Console.Error.WriteLine($"[bench] {resolution.Describe()}");
	}

	// The readiness gate is the provisioned table, not the server: a container whose init script failed
	// never becomes ready, and one still inside initdb answers on the unix socket only, so the query
	// goes over TCP to exclude the entrypoint's temporary server.
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
			BenchNames.SuperuserName,
			"--dbname",
			BenchNames.ProvisionedDatabase,
			"--tuples-only",
			"--no-align",
			"--command",
			"SELECT count(*) FROM public.trends;"
		];
	}

	// The tag carries a digest of the base image, so a run under a changed SEMIPLOT_PG_IMAGE is never
	// served the build made over the previous base.
	internal static string BenchImageFor(string baseImage)
	{
		var digest = SHA256.HashData(Encoding.UTF8.GetBytes(baseImage));

		return $"{BenchImageRepository}:{Convert.ToHexStringLower(digest)[..12]}";
	}
}
